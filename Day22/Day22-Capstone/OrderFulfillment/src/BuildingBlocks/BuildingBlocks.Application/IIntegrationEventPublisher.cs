namespace BuildingBlocks.Application;

/// <summary>
/// What an Application-layer command handler depends on to hand off an integration event.
/// The Infrastructure-layer implementation is what actually writes to the outbox table in the
/// same DB transaction as the aggregate save — see BuildingBlocks.Infrastructure.OutboxMessage.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(IntegrationEvent @event, CancellationToken ct = default);
}
