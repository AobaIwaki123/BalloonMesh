namespace BalloonMesh.Server.Services;

public sealed class AggregationWorker : BackgroundService
{
    private readonly FtmAggregator _aggregator;
    private readonly TimeProvider _time;
    private readonly ILogger<AggregationWorker> _logger;

    public AggregationWorker(FtmAggregator aggregator, TimeProvider time, ILogger<AggregationWorker> logger)
    {
        _aggregator = aggregator;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _aggregator.ProcessDueAsync(_time.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "FTM aggregation tick failed");
            }
        }
    }
}
