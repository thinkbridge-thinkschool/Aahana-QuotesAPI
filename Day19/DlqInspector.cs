using Azure.Messaging.ServiceBus;

namespace Day19;

public static class DlqInspector
{
    public static async Task PrintDeadLetterQueueAsync(
        ServiceBusClient client,
        string topicName,
        string subscriptionName)
    {
        await using var receiver = client.CreateReceiver(
            topicName,
            subscriptionName,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter
            });

        var messages = await receiver.ReceiveMessagesAsync(
            maxMessages: 10,
            maxWaitTime: TimeSpan.FromSeconds(5));

        if (messages.Count == 0)
        {
            Console.WriteLine(
                $"No messages in {topicName}/{subscriptionName}/$DeadLetterQueue.");
            return;
        }

        foreach (var message in messages)
        {
            Console.WriteLine(
                $"DLQ message - MessageId: {message.MessageId}, " +
                $"DeliveryCount: {message.DeliveryCount}, " +
                $"DeadLetterReason: {message.DeadLetterReason}, " +
                $"DeadLetterErrorDescription: {message.DeadLetterErrorDescription}");

            Console.WriteLine($"  Body: {message.Body}");

            // Peek-only for this proof - not completing/deleting it, so
            // it stays visible for whoever inspects the DLQ next.
        }
    }
}
