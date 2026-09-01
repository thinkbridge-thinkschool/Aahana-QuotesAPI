using Azure.Messaging.ServiceBus;

using Day19;

const string TopicName = "quote-events";
const string CompetingConsumerSubscription = "audit-log";

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run -- <publish|consume|verify-dlq>");
    return 1;
}

switch (args[0])
{
    case "publish":
    {
        var connectionString = RequireEnv("SB_SEND_CONNECTION_STRING");
        await using var client = new ServiceBusClient(connectionString);
        await Publisher.PublishDemoBatchAsync(client, TopicName);
        break;
    }

    case "consume":
    {
        var connectionString = RequireEnv("SB_LISTEN_CONNECTION_STRING");
        await using var client = new ServiceBusClient(connectionString);

        var worker = new QuoteEventWorker(new InMemoryIdempotencyStore());

        Console.WriteLine(
            $"Competing-consumer worker draining '{CompetingConsumerSubscription}' " +
            "for 20 seconds (MaxConcurrentCalls: 3)...");

        await worker.RunAsync(
            client,
            TopicName,
            CompetingConsumerSubscription,
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        Console.WriteLine("Consumer stopped.");
        break;
    }

    case "verify-dlq":
    {
        var connectionString = RequireEnv("SB_LISTEN_CONNECTION_STRING");
        await using var client = new ServiceBusClient(connectionString);

        await DlqInspector.PrintDeadLetterQueueAsync(
            client,
            TopicName,
            CompetingConsumerSubscription);

        break;
    }

    default:
        Console.WriteLine($"Unknown command: {args[0]}");
        return 1;
}

return 0;

static string RequireEnv(string name)
{
    return Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Environment variable {name} is not set.");
}
