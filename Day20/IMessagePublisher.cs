namespace Day20;

// Stands in for a real broker (Service Bus, in Day 19's terms). What
// matters for this exercise is the relay's behavior around it, not the
// transport - so this is a plain local file the "downstream" side reads
// from, not a network call.
public interface IMessagePublisher
{
    Task PublishAsync(OutboxMessage message);
}

public sealed class FileMessagePublisher(string inboxPath) : IMessagePublisher
{
    public async Task PublishAsync(OutboxMessage message)
    {
        var line = $"{message.Id}|{message.Type}|{message.Payload}";
        await File.AppendAllTextAsync(inboxPath, line + Environment.NewLine);
    }
}
