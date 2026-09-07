namespace BuildingBlocks.Application.IntegrationEvents;

// Published by Shipping, in response to OrderPaymentReceivedIntegrationEvent.

public sealed record ShipmentDispatchedIntegrationEvent(Guid OrderId, Guid ShipmentId, string Carrier, string TrackingNumber) : IntegrationEvent;
