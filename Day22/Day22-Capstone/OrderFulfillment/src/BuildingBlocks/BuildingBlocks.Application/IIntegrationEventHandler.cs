namespace BuildingBlocks.Application;

/// <summary>
/// A module's reaction to another module's integration event. Must be idempotent — the outbox
/// gives at-least-once delivery, so the same event can arrive twice (e.g. after a process crash
/// between "published" and "marked processed").
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
