using ReplayTool.Application;
using ReplayTool.Application.UseCases;

namespace ReplayTool.API.Workers;

public class RunWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly RunQueue _runQueue;
    private readonly ILogger<RunWorker> _logger;
    private readonly string _storageRoot;

    public RunWorker(
        IServiceProvider services,
        RunQueue runQueue,
        ILogger<RunWorker> logger,
        IConfiguration configuration)
    {
        _services = services;
        _runQueue = runQueue;
        _logger = logger;
        _storageRoot = configuration["STORAGE_ROOT"] ?? "./cases";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverOnStartupAsync(stoppingToken);

        await foreach (var (caseId, runId, isRetry) in _runQueue.Reader.ReadAllAsync(stoppingToken))
        {
            _logger.LogInformation("Starting run {RunId} for case {CaseId} (retry={IsRetry})", runId, caseId, isRetry);

            try
            {
                using var scope = _services.CreateScope();
                var executor = scope.ServiceProvider.GetRequiredService<RunExecutionService>();
                if (isRetry)
                    await executor.RetryAsync(caseId, runId, stoppingToken);
                else
                    await executor.ExecuteAsync(caseId, runId, stoppingToken);
                _logger.LogInformation("Completed run {RunId} for case {CaseId}", runId, caseId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Run {RunId} cancelled", runId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Run {RunId} for case {CaseId} failed with unhandled exception", runId, caseId);
            }
        }
    }

    private async Task RecoverOnStartupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var recovery = scope.ServiceProvider.GetRequiredService<RunRecoveryService>();
            await recovery.RecoverAsync(_runQueue, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during startup run recovery");
        }
    }
}
