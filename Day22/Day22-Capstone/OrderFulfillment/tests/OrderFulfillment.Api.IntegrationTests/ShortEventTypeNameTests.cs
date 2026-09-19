using BuildingBlocks.Application.IntegrationEvents;
using BuildingBlocks.Infrastructure;

namespace OrderFulfillment.Api.IntegrationTests;

/// <summary>
/// Day 32: a regression test for the exact bug that kept the deployed saga from ever completing.
/// AzureServiceBusMessageBus stamped "EventType" with the full CLR type name
/// ("OrderPlacedIntegrationEvent") while every subscription's SQL filter
/// (modules/servicebus.bicep's subscriptionEventMap) checks for the short name ("OrderPlaced") —
/// a mismatch that matched zero filters, on every message, on every subscription, with no error
/// anywhere. Confirmed live against the real deployed namespace before this fix; this pins the
/// fix down so it can't silently regress.
/// </summary>
public class ShortEventTypeNameTests
{
    [Theory]
    [InlineData(typeof(OrderPlacedIntegrationEvent), "OrderPlaced")]
    [InlineData(typeof(OrderConfirmedIntegrationEvent), "OrderConfirmed")]
    [InlineData(typeof(OrderCancelledIntegrationEvent), "OrderCancelled")]
    [InlineData(typeof(StockReservedIntegrationEvent), "StockReserved")]
    [InlineData(typeof(PaymentCapturedIntegrationEvent), "PaymentCaptured")]
    [InlineData(typeof(ShipmentDispatchedIntegrationEvent), "ShipmentDispatched")]
    public void StripsTheIntegrationEventSuffix_ToMatchServicebusBicepsSubscriptionEventMap(Type eventType, string expected)
    {
        Assert.Equal(expected, AzureServiceBusMessageBus.ShortEventTypeName(eventType));
    }
}
