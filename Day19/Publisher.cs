using System.Text.Json;

using Azure.Messaging.ServiceBus;

namespace Day19;

// Publishes quote-created events to the topic. The Service Bus
// MessageId is set to the event's own EventId (a stable id the producer
// controls), not a random SDK-generated one - that's what lets a
// consumer recognize "I've already handled this" even if the exact same
// business event gets published twice (a retried publish after a
// timeout, a duplicate delivery upstream, etc).
public static class Publisher
{
    public static async Task PublishDemoBatchAsync(
        ServiceBusClient client,
        string topicName)
    {
        await using var sender = client.CreateSender(topicName);

        var quote1 = new QuoteEvent(
            Guid.NewGuid(), 1, "Ada Lovelace", "That brain of mine is something more than merely mortal.");

        var quote2 = new QuoteEvent(
            Guid.NewGuid(), 2, "Grace Hopper", "The most dangerous phrase is: we've always done it this way.");

        var poison = new QuoteEvent(
            Guid.NewGuid(), 3, "Nobody", "This one is designed to fail every time.", ForcePoison: true);

        Console.WriteLine("Publishing quote1...");
        await sender.SendMessageAsync(ToMessage(quote1));

        Console.WriteLine("Publishing quote2...");
        await sender.SendMessageAsync(ToMessage(quote2));

        Console.WriteLine("Re-publishing quote1 again (same EventId - simulates an upstream retry/duplicate delivery)...");
        await sender.SendMessageAsync(ToMessage(quote1));

        Console.WriteLine("Publishing the poison message (always fails processing)...");
        await sender.SendMessageAsync(ToMessage(poison));

        Console.WriteLine("Done. 4 messages sent: 2 unique events, 1 duplicate, 1 poison.");
    }

    private static ServiceBusMessage ToMessage(QuoteEvent quoteEvent)
    {
        var body = JsonSerializer.Serialize(quoteEvent);

        return new ServiceBusMessage(body)
        {
            MessageId = quoteEvent.EventId.ToString(),
            ContentType = "application/json",
            Subject = "quote.created"
        };
    }
}
