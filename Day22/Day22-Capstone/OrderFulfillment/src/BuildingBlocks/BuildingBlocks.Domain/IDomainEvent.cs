namespace BuildingBlocks.Domain;

/// <summary>
/// Something that happened inside an aggregate. Raised in-process during a single module's
/// unit of work; the outbox is what turns a subset of these into cross-module integration events.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredOn { get; }
}
