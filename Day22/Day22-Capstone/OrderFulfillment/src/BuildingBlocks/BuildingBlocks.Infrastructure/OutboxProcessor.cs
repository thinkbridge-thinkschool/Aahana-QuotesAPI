using System.Diagnostics;
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
            // Re-parents this activity under the ORIGINAL HTTP request's trace (captured in
            // TraceParent when the outbox row was written) instead of starting a disconnected
            // one — this is the one line that makes "API -> worker -> DB" show up as a single
            // stitched trace in Application Insights rather than two unrelated operations that
            // merely happen to reference the same event by coincidence. Falls back to an
            // unparented activity (still exported, just its own root trace) if TraceParent is
            // missing or unparseable, e.g. rows written before this column existed.
            Activity? activity;

            if (message.TraceParent is not null
                && ActivityContext.TryParse(message.TraceParent, null, out var parentContext))
            {
                activity = OutboxTelemetry.Source.StartActivity("outbox.process", ActivityKind.Consumer, parentContext);
            }
            else
            {
                activity = OutboxTelemetry.Source.StartActivity("outbox.process", ActivityKind.Consumer);
            }

            using var _ = activity;

            activity?.SetTag("outbox.module", store.ModuleName);
            activity?.SetTag("outbox.message_type", message.Type);
            activity?.SetTag("outbox.message_id", message.Id);

            try
            {
                var integrationEvent = store.Deserialize(message);
                await messageBus.PublishAsync(integrationEvent, ct);
                await store.MarkProcessedAsync(message.Id, ct);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

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
