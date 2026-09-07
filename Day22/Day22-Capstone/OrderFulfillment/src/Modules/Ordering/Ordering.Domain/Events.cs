using BuildingBlocks.Domain;

namespace Ordering.Domain;

public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId, Guid CustomerId,
    IReadOnlyCollection<(string Sku, int Quantity)> Lines) : IDomainEvent;

public sealed record OrderConfirmed(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IDomainEvent;

public sealed record OrderCancelled(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId, string Reason) : IDomainEvent;

public sealed record OrderPaymentReceived(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IDomainEvent;

public sealed record OrderShipped(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IDomainEvent;
