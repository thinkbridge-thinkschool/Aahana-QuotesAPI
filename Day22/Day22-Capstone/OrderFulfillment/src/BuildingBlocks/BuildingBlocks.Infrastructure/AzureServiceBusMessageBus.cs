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
                // Short name — what every subscription's SQL filter matches against.
                ["EventType"] = eventType.Name,
                // Full assembly-qualified name — what the receiving side (ServiceBusSubscriptionProcessor)
                // uses to deserialize back to the exact concrete type, without a hand-maintained lookup table.
                ["EventClrType"] = eventType.AssemblyQualifiedName,
            },
        };

        await _sender.SendMessageAsync(message, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
    }
}
