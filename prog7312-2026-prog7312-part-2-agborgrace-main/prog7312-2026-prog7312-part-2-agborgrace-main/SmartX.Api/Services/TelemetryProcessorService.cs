namespace SmartX.Api.Services;

/// <summary>
/// Background worker that drains the queues: critical alerts first (priority queue), then the FIFO
/// TelemetryQueue in arrival order. It also watches for nodes that stopped reporting (DisconnectedNodes).
/// Interval/batch size come from appsettings.json ("Processing" section) so the queue behaviour is easy to
/// demonstrate: with the defaults (3 packets / 300 ms) a burst of 20 packets takes a couple of seconds to
/// drain, while a critical alert is processed instantly.
/// </summary>
public class TelemetryProcessorService : BackgroundService
{
    private readonly SmartXDataStore _store;
    private readonly IConfiguration _config;
    private readonly ILogger<TelemetryProcessorService> _logger;

    public TelemetryProcessorService(SmartXDataStore store, IConfiguration config, ILogger<TelemetryProcessorService> logger)
    {
        _store = store;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(50, _config.GetValue("Processing:IntervalMs", 300)));
        var batchSize = Math.Max(1, _config.GetValue("Processing:BatchSize", 3));
        var disconnectAfter = TimeSpan.FromSeconds(Math.Max(5, _config.GetValue("Telemetry:DisconnectAfterSeconds", 15)));

        using var timer = new PeriodicTimer(interval);
        var lastDisconnectCheck = DateTime.UtcNow;

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    // Adaptive batch: if the queue is deep, drain a quarter of it per tick so it can never run away.
                    int take = Math.Max(batchSize, _store.TelemetryQueueDepth / 4);
                    _store.ProcessQueuedBatch(take);

                    if (DateTime.UtcNow - lastDisconnectCheck >= TimeSpan.FromSeconds(1))
                    {
                        _store.CheckForDisconnects(disconnectAfter);
                        lastDisconnectCheck = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    // The processor must never die because of one bad packet.
                    _logger.LogError(ex, "Telemetry processing tick failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }
}
