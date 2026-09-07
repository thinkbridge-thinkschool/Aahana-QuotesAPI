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
        });

        return Task.CompletedTask;
    }
}
