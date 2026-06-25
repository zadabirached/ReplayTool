using ReplayTool.Application.Timeline;
using ReplayTool.Domain.Entities;
using ReplayTool.Domain.Events;

namespace ReplayTool.Application.UseCases;

public record TriggerRunRequest
{
    public bool DryRun { get; init; } = false;
    public double? SpeedFactor { get; init; }
    public ReplayMode? Mode { get; init; }
}

public class TriggerRunUseCase
{
    private readonly Interfaces.IFileStorage _storage;
    private readonly string _storageRoot;
    private readonly RunQueue _runQueue;

    public TriggerRunUseCase(Interfaces.IFileStorage storage, string storageRoot, RunQueue runQueue)
    {
        _storage = storage;
        _storageRoot = storageRoot;
        _runQueue = runQueue;
    }

    // Returns null when the case is not found.
    // DryRun=true → returns a completed plan run with no side effects.
    // DryRun=false → persists a Pending run, enqueues it, returns immediately.
    public async Task<Run?> ExecuteAsync(Guid caseId, TriggerRunRequest request)
    {
        var result = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
        if (result is null) return null;

        var (_, folder) = result.Value;

        if (request.DryRun)
            return await BuildDryRunPlanAsync(caseId, folder, request);

        var run = new Run
        {
            Id = Guid.NewGuid(),
            Status = RunStatus.Pending,
            DryRun = false,
            SpeedFactor = request.SpeedFactor ?? 1.0,
            Mode = request.Mode ?? ReplayMode.Normal,
            CreatedAt = DateTime.UtcNow,
        };

        await RunStore.WriteAsync(_storage, folder, run);
        await _runQueue.Writer.WriteAsync((caseId, run.Id, false));

        return run;
    }

    private async Task<Run> BuildDryRunPlanAsync(Guid caseId, string folder, TriggerRunRequest request)
    {
        var steps = new List<RunStep>();

        // Plan seed steps from customer order file.
        var coPath = Path.Combine(folder, CaseFileType.Filename(CaseFileType.CustomerOrder));
        if (await _storage.FileExistsAsync(coPath))
        {
            var content = await _storage.ReadFileAsync(coPath);
            var parsed = ParseTypeFilesUseCase.ParseContent<CustomerOrderEvent>(content, normalizeTimestamps: false);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var evt in parsed.Events)
            {
                var r = seenIds.Add(evt.OrderId) ? RunStepResult.Inserted : RunStepResult.Skipped;
                steps.Add(new RunStep("Seed", evt.OrderId, r));
            }
        }

        // Plan replay steps from merged timeline.
        var parseUseCase = new ParseTypeFilesUseCase(_storage, _storageRoot);
        var allParsed = await parseUseCase.ExecuteAsync(caseId);
        if (allParsed is not null)
        {
            var timeline = new BuildTimelineUseCase().Execute(
                allParsed.RoutingResponses?.Events,
                allParsed.AssignmentSolutions?.Events);

            var speedFactor = request.SpeedFactor ?? 1.0;
            foreach (var entry in timeline)
            {
                var id = entry.EventType == ReplayEventType.RoutingResponse
                    ? $"routing:{entry.RoutingEvent!.CorrelationId}"
                    : $"assignment:{entry.AssignmentEvent!.AreaId}@{entry.Timestamp:O}";
                var scheduledOffsetMs = (long)(entry.OffsetMs / speedFactor);
                steps.Add(new RunStep("Replay", id, RunStepResult.Published, entry.OffsetMs, scheduledOffsetMs));
            }
        }

        return new Run
        {
            Id = Guid.NewGuid(),
            Status = RunStatus.Completed,
            DryRun = true,
            SpeedFactor = request.SpeedFactor ?? 1.0,
            Mode = request.Mode ?? ReplayMode.Normal,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Steps = steps,
        };
    }
}
