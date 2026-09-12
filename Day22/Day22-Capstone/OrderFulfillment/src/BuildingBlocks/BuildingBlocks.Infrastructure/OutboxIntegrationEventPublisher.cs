using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Application;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// The Infrastructure-layer implementation Application handlers depend on through
/// IIntegrationEventPublisher. Serializes the event and hands it to the module's IOutboxWriter —
/// it does not publish to the bus itself, that's OutboxProcessor's job, running out-of-band.
/// </summary>
public class OutboxIntegrationEventPublisher(IOutboxWriter outboxWriter) : IIntegrationEventPublisher
{
    public Task PublishAsync(IntegrationEvent @event, CancellationToken ct = default)
    {
        outboxWriter.Add(new OutboxMessage
        {
            Id = @event.EventId,
            Type = @event.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(@event, @event.GetType()),
            OccurredOn = @event.OccurredOn,
            // Activity.Current is the ASP.NET Core request's own activity at this point (this
            // runs inside the same HTTP request that triggered the domain change) — its .Id is
            // the W3C traceparent string. Recording it now is the only way OutboxProcessor,
            // running seconds later on its own timer with no HTTP context at all, can later
            // reconnect to it.
            TraceParent = Activity.Current?.Id,
        });

        return Task.CompletedTask;
    }
}
