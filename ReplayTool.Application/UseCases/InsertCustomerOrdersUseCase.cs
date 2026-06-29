using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Events;

namespace ReplayTool.Application.UseCases;

public class InsertCustomerOrdersUseCase
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;
    private readonly IJobServiceRepository _repository;
    private readonly string _connectionString;
    private readonly bool _allowRemoteDb;

    public InsertCustomerOrdersUseCase(
        IFileStorage storage,
        string storageRoot,
        IJobServiceRepository repository,
        string connectionString,
        bool allowRemoteDb)
    {
        _storage = storage;
        _storageRoot = storageRoot;
        _repository = repository;
        _connectionString = connectionString;
        _allowRemoteDb = allowRemoteDb;
    }

    // Returns null when the case is not found.
    // Throws InvalidOperationException when the DB target is non-local and no override is set.
    // When onlyOrderIds is provided, only those OrderIds are processed (used to retry failed steps).
    public async Task<IReadOnlyList<SeedStep>?> ExecuteAsync(Guid caseId, IReadOnlySet<string>? onlyOrderIds = null)
    {
        if (!_allowRemoteDb && !LocalTargetGuard.IsLocalConnectionString(_connectionString))
            throw new InvalidOperationException(
                "The configured JobService DB target is not local. " +
                "Set REPLAY_ALLOW_REMOTE_DB=true to override this safety guard.");

        var result = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
        if (result is null) return null;

        var (_, folder) = result.Value;

        var filePath = Path.Combine(folder, CaseFileType.Filename(CaseFileType.CustomerOrder));
        if (!await _storage.FileExistsAsync(filePath))
            return [];

        var content = await _storage.ReadFileAsync(filePath);
        var parsed = ParseTypeFilesUseCase.ParseContent<CustomerOrderEvent>(content, normalizeTimestamps: false);

        var steps = new List<SeedStep>();

        // Report parse errors immediately — never silently drop.
        foreach (var err in parsed.Errors)
            steps.Add(new SeedStep("Seed", $"[parse error at index {err.Index}]", SeedStepResult.Failed, err.Reason));

        // Deduplicate within the file: first occurrence of each OrderId wins.
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var evt in parsed.Events)
        {
            if (onlyOrderIds is not null && !onlyOrderIds.Contains(evt.OrderId))
                continue;

            if (!seenInFile.Add(evt.OrderId))
            {
                steps.Add(new SeedStep("Seed", evt.OrderId, SeedStepResult.Skipped, "Duplicate OrderId in file"));
                continue;
            }

            if (await _repository.OrderExistsAsync(evt.OrderId))
            {
                steps.Add(new SeedStep("Seed", evt.OrderId, SeedStepResult.Skipped, "Already in database"));
                continue;
            }

            try
            {
                int jobFk;

                if (evt.Job is not null)
                {
                    // Event carries full job info — upsert and use the resulting integer PK.
                    jobFk = await _repository.UpsertJobAsync(evt.Job);
                }
                else
                {
                    // No job info — look up the Job row by business UUID.
                    var found = await _repository.FindJobPkAsync(evt.JobId);
                    if (found is null)
                    {
                        steps.Add(new SeedStep("Seed", evt.OrderId, SeedStepResult.Failed,
                            $"Job with business id {evt.JobId} not found in database"));
                        continue;
                    }
                    jobFk = found.Value;
                }

                await _repository.InsertCustomerOrderAsync(evt, jobFk);
                steps.Add(new SeedStep("Seed", evt.OrderId, SeedStepResult.Inserted));
            }
            catch (Exception ex)
            {
                steps.Add(new SeedStep("Seed", evt.OrderId, SeedStepResult.Failed, ex.Message));
            }
        }

        return steps;
    }
}
