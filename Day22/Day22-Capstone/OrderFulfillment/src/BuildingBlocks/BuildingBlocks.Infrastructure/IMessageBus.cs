using BuildingBlocks.Application;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// What OutboxProcessor publishes onto once an outbox row is due. In this monolith it's
/// in-process (see InProcessMessageBus) — swapping it for a real broker (Azure Service Bus,
/// RabbitMQ) later is exactly the seam a future extraction into separate services would use,
/// without touching a single module's Domain or Application layer.
/// </summary>
public interface IMessageBus
{
    Task PublishAsync(IntegrationEvent @event, CancellationToken ct = default);
}
