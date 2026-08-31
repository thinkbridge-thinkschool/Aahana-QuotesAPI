namespace Day18;

// Drains IBackgroundTaskQueue for the lifetime of the host. The one
// subtlety here is which token gets used where:
//
//   - Waiting for the *next* item uses stoppingToken, so a shutdown
//     signal while idle stops the loop immediately instead of hanging
//     until a job that may never arrive.
//   - Running an item *already dequeued* uses CancellationToken.None,
//     so a job that's mid-flight when shutdown begins gets to finish
//     rather than being torn down half-done. The host's own shutdown
//     timeout (HostOptions.ShutdownTimeout) is the real backstop if a
//     job hangs - not this service cancelling it.
public sealed class QueuedHostedService : BackgroundService
{
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ILogger<QueuedHostedService> _logger;

    public QueuedHostedService(
        IBackgroundTaskQueue taskQueue,
        ILogger<QueuedHostedService> logger)
    {
        _taskQueue = taskQueue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queued hosted service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            Func<CancellationToken, ValueTask> workItem;

            try
            {
                workItem = await _taskQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown while waiting for the next item.
                break;
            }

            try
            {
                await workItem(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // One bad job must not take the whole drain loop down -
                // log it and keep processing the rest of the queue.
                _logger.LogError(
                    ex,
                    "Unhandled exception executing a queued work item");
            }
        }

        _logger.LogInformation("Queued hosted service drain loop exited");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Queued hosted service stopping - waiting for the current " +
            "item (if any) to finish, up to the host's shutdown timeout");

        await base.StopAsync(cancellationToken);
    }
}
