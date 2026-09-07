namespace BuildingBlocks.Application.IntegrationEvents;

// Published by Payments, in response to OrderConfirmedIntegrationEvent.

public sealed record PaymentCapturedIntegrationEvent(Guid OrderId, Guid PaymentId) : IntegrationEvent;

public sealed record PaymentFailedIntegrationEvent(Guid OrderId, string Reason) : IntegrationEvent;
