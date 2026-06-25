using ReplayTool.Application.Interfaces;
using ReplayTool.Application.Timeline;
using ReplayTool.Domain.Entities;

namespace ReplayTool.Application.UseCases;

public class RunExecutionService
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;
    private readonly IJobServiceRepository _repository;
    private readonly string _connectionString;
    private readonly bool _allowRemoteDb;
    private readonly IMessagePublisher _publisher;

    public RunExecutionService(
        IFileStorage storage,
        string storageRoot,
        IJobServiceRepository repository,
        string connectionString,
        bool allowRemoteDb,
        IMessagePublisher publisher)
    {
        _storage = storage;
        _storageRoot = storageRoot;
        _repository = repository;
        _connectionString = connectionString;
        _allowRemoteDb = allowRemoteDb;
        _publisher = publisher;
    }

    public async Task ExecuteAsync(Guid caseId, Guid runId, CancellationToken ct)
    {
        var caseResult = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
        if (caseResult is null) return;

        var (caseEntity, folder) = caseResult.Value;
        var run = await RunStore.ReadAsync(_storage, folder, runId);
        if (run is null) return;

        // Mark both run and case as Running.
        run = run with { Status = RunStatus.Running, StartedAt = DateTime.UtcNow };
        await RunStore.WriteAsync(_storage, folder, run);
        await CaseStore.WriteAsync(_storage, folder, caseEntity with { Status = CaseStatus.Running });

        var steps = new List<RunStep>();
        var finalStatus = RunStatus.Completed;

        try
        {
            // Phase 1 — Seed: insert customer orders directly into the JobService DB.
            var seedUseCase = new InsertCustomerOrdersUseCase(
                _storage, _storageRoot, _repository, _connectionString, _allowRemoteDb);

            var seedSteps = await seedUseCase.ExecuteAsync(caseId);
            if (seedSteps is not null)
            {
                foreach (var s in seedSteps)
                    steps.Add(new RunStep("Seed", s.OrderId, MapSeedResult(s.Result), Error: s.Error));
            }

            ct.ThrowIfCancellationRequested();

            // Phase 2 — Timed replay: publish routing and assignment events in Timestamp order.
            var parseUseCase = new ParseTypeFilesUseCase(_storage, _storageRoot);
            var parsed = await parseUseCase.ExecuteAsync(caseId);

            if (parsed is not null)
            {
                var timeline = new BuildTimelineUseCase().Execute(
                    parsed.RoutingResponses?.Events,
                    parsed.AssignmentSolutions?.Events);

                // Anchor for absolute-time scheduling — avoids sleep accumulation drift.
                var t0 = DateTime.UtcNow;

                foreach (var entry in timeline)
                {
                    ct.ThrowIfCancellationRequested();

                    var scheduledOffsetMs = (long)(entry.OffsetMs / run.SpeedFactor);

                    // Normal mode: wait until the absolute target time derived from t0.
                    // Fast mode: no delay — publish in order immediately.
                    if (run.Mode == ReplayMode.Normal && entry.OffsetMs > 0)
                    {
                        var targetTime = t0.AddMilliseconds(scheduledOffsetMs);
                        var delay = targetTime - DateTime.UtcNow;
                        if (delay > TimeSpan.Zero)
                            await Task.Delay(delay, ct);
                    }

                    var actualOffsetMs = (long)(DateTime.UtcNow - t0).TotalMilliseconds;

                    var eventId = entry.EventType == ReplayEventType.RoutingResponse
                        ? $"routing:{entry.RoutingEvent!.CorrelationId}"
                        : $"assignment:{entry.AssignmentEvent!.AreaId}@{entry.Timestamp:O}";

                    try
                    {
                        if (entry.EventType == ReplayEventType.RoutingResponse)
                            await _publisher.PublishRoutingEventAsync(entry.RoutingEvent!, ct);
                        else
                            await _publisher.PublishAssignmentEventAsync(entry.AssignmentEvent!, ct);

                        steps.Add(new RunStep("Replay", eventId, RunStepResult.Published,
                            entry.OffsetMs, scheduledOffsetMs, actualOffsetMs));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        steps.Add(new RunStep("Replay", eventId, RunStepResult.Failed,
                            entry.OffsetMs, scheduledOffsetMs, actualOffsetMs, ex.Message));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            finalStatus = RunStatus.Failed;
        }
        catch (Exception)
        {
            finalStatus = RunStatus.Failed;
        }

        run = run with { Status = finalStatus, CompletedAt = DateTime.UtcNow, Steps = steps };
        await RunStore.WriteAsync(_storage, folder, run);
        await CaseStore.WriteAsync(_storage, folder, caseEntity with
        {
            Status = finalStatus == RunStatus.Completed ? CaseStatus.Completed : CaseStatus.Failed
        });
    }

    private static RunStepResult MapSeedResult(SeedStepResult r) => r switch
    {
        SeedStepResult.Inserted => RunStepResult.Inserted,
        SeedStepResult.Skipped => RunStepResult.Skipped,
        _ => RunStepResult.Failed,
    };

    // Re-executes only the Failed steps of a previous run, preserving the original
    // ordering and leaving succeeded steps untouched. Called by RunWorker for a retry.
    public async Task RetryAsync(Guid caseId, Guid runId, CancellationToken ct)
    {
        var caseResult = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
        if (caseResult is null) return;

        var (caseEntity, folder) = caseResult.Value;
        var run = await RunStore.ReadAsync(_storage, folder, runId);
        if (run is null) return;

        run = run with { Status = RunStatus.Running, StartedAt = DateTime.UtcNow };
        await RunStore.WriteAsync(_storage, folder, run);
        await CaseStore.WriteAsync(_storage, folder, caseEntity with { Status = CaseStatus.Running });

        var steps = run.Steps.ToList();

        try
        {
            var failedSeedOrderIds = steps
                .Where(s => s.Phase == "Seed" && s.Result == RunStepResult.Failed)
                .Select(s => s.EventId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (failedSeedOrderIds.Count > 0)
            {
                var seedUseCase = new InsertCustomerOrdersUseCase(
                    _storage, _storageRoot, _repository, _connectionString, _allowRemoteDb);

                var retried = await seedUseCase.ExecuteAsync(caseId, failedSeedOrderIds);
                if (retried is not null)
                {
                    foreach (var r in retried)
                    {
                        var idx = steps.FindIndex(s =>
                            s.Phase == "Seed" && s.EventId.Equals(r.OrderId, StringComparison.OrdinalIgnoreCase));
                        if (idx < 0) continue;

                        steps[idx] = steps[idx] with
                        {
                            Result = MapSeedResult(r.Result),
                            Error = r.Error,
                            Attempts = steps[idx].Attempts + 1,
                        };
                    }
                }
            }

            ct.ThrowIfCancellationRequested();

            var failedReplayEventIds = steps
                .Where(s => s.Phase == "Replay" && s.Result == RunStepResult.Failed)
                .Select(s => s.EventId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (failedReplayEventIds.Count > 0)
            {
                var parseUseCase = new ParseTypeFilesUseCase(_storage, _storageRoot);
                var parsed = await parseUseCase.ExecuteAsync(caseId);

                if (parsed is not null)
                {
                    var timeline = new BuildTimelineUseCase().Execute(
                        parsed.RoutingResponses?.Events,
                        parsed.AssignmentSolutions?.Events);

                    // Retries re-publish in original timeline order, but without re-applying
                    // the original timing gaps — only re-execution order is preserved here.
                    foreach (var entry in timeline)
                    {
                        var eventId = entry.EventType == ReplayEventType.RoutingResponse
                            ? $"routing:{entry.RoutingEvent!.CorrelationId}"
                            : $"assignment:{entry.AssignmentEvent!.AreaId}@{entry.Timestamp:O}";

                        if (!failedReplayEventIds.Contains(eventId)) continue;

                        ct.ThrowIfCancellationRequested();

                        var idx = steps.FindIndex(s =>
                            s.Phase == "Replay" && s.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase));
                        if (idx < 0) continue;

                        try
                        {
                            if (entry.EventType == ReplayEventType.RoutingResponse)
                                await _publisher.PublishRoutingEventAsync(entry.RoutingEvent!, ct);
                            else
                                await _publisher.PublishAssignmentEventAsync(entry.AssignmentEvent!, ct);

                            steps[idx] = steps[idx] with
                            {
                                Result = RunStepResult.Published,
                                Error = null,
                                Attempts = steps[idx].Attempts + 1,
                            };
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            steps[idx] = steps[idx] with
                            {
                                Error = ex.Message,
                                Attempts = steps[idx].Attempts + 1,
                            };
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Fall through — final status below is computed from remaining step results.
        }

        var finalStatus = steps.Any(s => s.Result == RunStepResult.Failed) ? RunStatus.Failed : RunStatus.Completed;

        run = run with { Status = finalStatus, CompletedAt = DateTime.UtcNow, Steps = steps };
        await RunStore.WriteAsync(_storage, folder, run);
        await CaseStore.WriteAsync(_storage, folder, caseEntity with
        {
            Status = finalStatus == RunStatus.Completed ? CaseStatus.Completed : CaseStatus.Failed
        });
    }
}
