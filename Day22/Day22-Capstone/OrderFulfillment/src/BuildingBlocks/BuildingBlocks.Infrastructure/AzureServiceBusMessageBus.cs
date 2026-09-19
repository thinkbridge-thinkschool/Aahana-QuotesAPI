using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BuildingBlocks.Application;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// The real cross-process implementation of IMessageBus — sends to the Service Bus topic
/// provisioned in Day 23's infra/modules/servicebus.bicep, authenticating as the Container App's
/// own managed identity (the ServiceBusClient below is constructed with a TokenCredential, never
/// a connection string — see Program.cs). Stamps "EventType" on every message because that's
/// exactly the property each subscription's SQL filter matches on
/// (modules/servicebus.bicep's subscriptionEventMap) — without it every message would sit
/// unmatched by any subscription's filter and never be delivered anywhere.
/// </summary>
public class AzureServiceBusMessageBus : IMessageBus, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;

    public AzureServiceBusMessageBus(ServiceBusClient client, string topicName)
    {
        _sender = client.CreateSender(topicName);
    }

    public async Task PublishAsync(IntegrationEvent @event, CancellationToken ct = default)
    {
        var eventType = @event.GetType();

        var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(@event, eventType))
        {
            MessageId = @event.EventId.ToString(),
            ApplicationProperties =
            {
                // Day 32 fix: eventType.Name is NOT a "short name" — for OrderPlacedIntegrationEvent
                // it's literally the string "OrderPlacedIntegrationEvent". modules/servicebus.bicep's
                // subscriptionEventMap filters on the DESIGN.md-style short names ('OrderPlaced',
                // 'OrderCancelled', ...), with no "IntegrationEvent" suffix — so every message sent
                // with the old eventType.Name value matched zero subscription filters, on every
                // subscription, silently (a message matching no filter is simply never delivered,
                // no error anywhere). Confirmed live: every subscription's rule list was actually
                // empty (the rule resource had never been deployed at all — a separate, compounding
                // infra-drift bug, see DAY32-SHIP-DEMO-POSTMORTEM.md), so this was masked until that
                // was fixed too; stamping the wrong value would have kept nothing flowing even once
                // the rules existed.
                ["EventType"] = ShortEventTypeName(eventType),
                // Full assembly-qualified name — what the receiving side (ServiceBusSubscriptionProcessor)
                // uses to deserialize back to the exact concrete type, without a hand-maintained lookup table.
                ["EventClrType"] = eventType.AssemblyQualifiedName,
            },
        };

        await _sender.SendMessageAsync(message, ct);
    }

    /// <summary>
    /// "OrderPlacedIntegrationEvent" -> "OrderPlaced" — the naming convention every
    /// IntegrationEvent subclass in this codebase already follows (see
    /// BuildingBlocks.Application.IntegrationEvents), and the one modules/servicebus.bicep's
    /// subscriptionEventMap filters key on.
    /// </summary>
    internal static string ShortEventTypeName(Type eventType) =>
        eventType.Name.EndsWith("IntegrationEvent", StringComparison.Ordinal)
            ? eventType.Name[..^"IntegrationEvent".Length]
            : eventType.Name;

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
    }
}
