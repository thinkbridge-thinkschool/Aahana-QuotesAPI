namespace Day20;

// Reads the simulated broker's inbox and applies each event exactly
// once, no matter how many times the same message id shows up in the
// file. This is what makes "the relay might publish the same message
// twice after a crash" a non-issue: at-least-once delivery plus an
// idempotent consumer is equivalent, from the outside, to
// exactly-once - the duplicate arrives, but it's a no-op.
public static class Consumer
{
    public static async Task<(int applied, int duplicatesSkipped)> ProcessInboxAsync(
        string inboxPath)
    {
        if (!File.Exists(inboxPath))
        {
            return (0, 0);
        }

        var seen = new HashSet<Guid>();
        var applied = 0;
        var duplicatesSkipped = 0;

        foreach (var line in await File.ReadAllLinesAsync(inboxPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('|', 3);
            var messageId = Guid.Parse(parts[0]);
            var type = parts[1];
            var payload = parts[2];

            if (!seen.Add(messageId))
            {
                Console.WriteLine(
                    $"[consumer] duplicate delivery of {messageId} - already applied, skipping");

                duplicatesSkipped++;
                continue;
            }

            Console.WriteLine($"[consumer] applying {type} {messageId}: {payload}");
            applied++;
        }

        return (applied, duplicatesSkipped);
    }
}
