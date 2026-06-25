using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Entities;

namespace ReplayTool.Application.UseCases;

public class RetryRunUseCase
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;
    private readonly RunQueue _runQueue;

    public RetryRunUseCase(IFileStorage storage, string storageRoot, RunQueue runQueue)
    {
        _storage = storage;
        _storageRoot = storageRoot;
        _runQueue = runQueue;
    }

    // Returns null when the case or run is not found.
    // Throws InvalidOperationException when the run is still in progress or has no failed steps.
    public async Task<Run?> ExecuteAsync(Guid caseId, Guid runId)
    {
        var caseResult = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
        if (caseResult is null) return null;

        var (_, folder) = caseResult.Value;
        var run = await RunStore.ReadAsync(_storage, folder, runId);
        if (run is null) return null;

        if (run.Status is RunStatus.Pending or RunStatus.Running)
            throw new InvalidOperationException("Run is still in progress and cannot be retried yet.");

        if (!run.Steps.Any(s => s.Result == RunStepResult.Failed))
            throw new InvalidOperationException("Run has no failed steps to retry.");

        // Keep the existing Steps (succeeded ones stay as-is) — only mark the run Pending again
        // so the worker re-executes just the Failed entries.
        var pending = run with { Status = RunStatus.Pending };
        await RunStore.WriteAsync(_storage, folder, pending);
        await _runQueue.Writer.WriteAsync((caseId, runId, true));

        return pending;
    }
}
