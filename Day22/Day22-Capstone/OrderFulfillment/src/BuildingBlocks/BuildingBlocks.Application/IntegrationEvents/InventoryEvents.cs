namespace BuildingBlocks.Application.IntegrationEvents;

// Published by Inventory, in response to OrderPlacedIntegrationEvent.

public sealed record StockReservedIntegrationEvent(Guid OrderId) : IntegrationEvent;

public sealed record StockReservationFailedIntegrationEvent(Guid OrderId, string Reason) : IntegrationEvent;
