using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// Polls every module's outbox on a fixed interval and publishes due messages to the bus. A
/// message that keeps failing past MaxAttempts is moved to that module's dead-letter table
/// instead of being retried forever — a human resolves DLQ entries, the processor doesn't.
///
/// This is the piece that turns "the DB transaction committed" into "the rest of the monolith
/// eventually finds out" — the actual mechanism behind every async flow in DESIGN.md.
/// </summary>
public class OutboxProcessor(
    IEnumerable<IOutboxStore> stores,
    IMessageBus messageBus,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var store in stores)
            {
                await ProcessStoreAsync(store, stoppingToken);
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessStoreAsync(IOutboxStore store, CancellationToken ct)
    {
        var due = await store.GetUnprocessedAsync(BatchSize, ct);

        foreach (var message in due)
        {
            try
            {
                var integrationEvent = store.Deserialize(message);
                await messageBus.PublishAsync(integrationEvent, ct);
                await store.MarkProcessedAsync(message.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) from {Module} failed on attempt {Attempt}",
                    message.Id, message.Type, store.ModuleName, message.AttemptCount + 1);

                if (message.AttemptCount + 1 >= MaxAttempts)
                {
                    await store.DeadLetterAsync(message, ex.Message, ct);
                }
                else
                {
                    await store.RecordFailureAsync(message.Id, ex.Message, ct);
                }
            }
        }
    }
}
