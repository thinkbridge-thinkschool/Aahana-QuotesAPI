namespace BuildingBlocks.Application;

/// <summary>
/// The subset of a module's domain events that other modules are allowed to react to.
/// Crosses the module boundary only through the outbox + bus — never a direct in-process call
/// from one module's Application layer into another's.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
