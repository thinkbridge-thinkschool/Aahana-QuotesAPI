namespace BuildingBlocks.Application.IntegrationEvents;

// Published by Ordering. These are the only shape of Ordering's state that other modules ever
// see — never a reference to the Order aggregate itself.

public sealed record OrderPlacedIntegrationEvent(
    Guid OrderId, Guid CustomerId, IReadOnlyCollection<OrderLineDto> Lines) : IntegrationEvent;

public sealed record OrderConfirmedIntegrationEvent(Guid OrderId, Guid CustomerId, decimal Total, string Currency) : IntegrationEvent;

public sealed record OrderPaymentReceivedIntegrationEvent(Guid OrderId, Guid CustomerId) : IntegrationEvent;

public sealed record OrderCancelledIntegrationEvent(Guid OrderId, string Reason) : IntegrationEvent;

public sealed record OrderLineDto(string Sku, int Quantity);
