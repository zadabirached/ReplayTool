using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Entities;

namespace ReplayTool.Application.UseCases;

// Called once on startup to recover runs that were interrupted by a process restart.
public class RunRecoveryService
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;

    public RunRecoveryService(IFileStorage storage, string storageRoot)
    {
        _storage = storage;
        _storageRoot = storageRoot;
    }

    public async Task RecoverAsync(RunQueue runQueue, CancellationToken ct)
    {
        if (!await _storage.DirectoryExistsAsync(_storageRoot)) return;

        var caseDirs = await _storage.ListDirectoriesAsync(_storageRoot);

        foreach (var caseDir in caseDirs)
        {
            if (!Guid.TryParse(Path.GetFileName(caseDir), out var caseId)) continue;

            var runs = await RunStore.ListAsync(_storage, caseDir);

            foreach (var run in runs)
            {
                if (run.Status == RunStatus.Running)
                {
                    // Restart interrupted this run — mark it failed so it is not stuck.
                    var failed = run with { Status = RunStatus.Failed, CompletedAt = DateTime.UtcNow };
                    await RunStore.WriteAsync(_storage, caseDir, failed);

                    // Also revert the case status.
                    var caseResult = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
                    if (caseResult is not null)
                        await CaseStore.WriteAsync(_storage, caseDir, caseResult.Value.@case with { Status = CaseStatus.Failed });
                }
                else if (run.Status == RunStatus.Pending)
                {
                    // A fresh trigger always starts with an empty Steps list; a queued retry
                    // keeps the previous attempt's steps. Use that to resume the right mode
                    // so a crash before the worker picks it up doesn't re-publish succeeded steps.
                    var isRetry = run.Steps.Count > 0;
                    await runQueue.Writer.WriteAsync((caseId, run.Id, isRetry), ct);
                }
            }
        }
    }
}
