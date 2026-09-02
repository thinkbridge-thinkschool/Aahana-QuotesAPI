using Day20;

using Microsoft.EntityFrameworkCore;

const string DbPath = "day20.db";
const string InboxPath = "downstream-inbox.jsonl";

File.Delete(DbPath);
File.Delete(InboxPath);

var options = new DbContextOptionsBuilder<OutboxDbContext>()
    .UseSqlite($"Data Source={DbPath}")
    .Options;

await using (var setupDb = new OutboxDbContext(options))
{
    await setupDb.Database.EnsureCreatedAsync();
}

Console.WriteLine("=== Scenario A: crash before commit ===");
await using (var db = new OutboxDbContext(options))
{
    var service = new QuoteService(db);

    try
    {
        await service.CreateQuoteAsync(
            "Crash Test",
            "This write should never be persisted.",
            simulateCrashBeforeCommit: true);
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"Caught simulated crash: {ex.Message}");
    }
}

await using (var db = new OutboxDbContext(options))
{
    var quoteCount = await db.Quotes.CountAsync();
    var outboxCount = await db.OutboxMessages.CountAsync();

    Console.WriteLine(
        $"After crash-before-commit: Quotes={quoteCount}, OutboxMessages={outboxCount} " +
        "(expect 0, 0 - the transaction rolled back atomically)");
}

Console.WriteLine();
Console.WriteLine("=== Scenario B: crash after publish, before marking sent ===");

Quote createdQuote;

await using (var db = new OutboxDbContext(options))
{
    var service = new QuoteService(db);

    createdQuote = await service.CreateQuoteAsync(
        "Ada Lovelace",
        "That brain of mine is something more than merely mortal.");

    Console.WriteLine($"Created quote {createdQuote.Id} and its outbox row, in one transaction.");
}

await using (var db = new OutboxDbContext(options))
{
    var publisher = new FileMessagePublisher(InboxPath);
    var relay = new OutboxRelay(db, publisher);

    try
    {
        await relay.ProcessPendingAsync(simulateCrashAfterFirstPublish: true);
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"Caught simulated crash: {ex.Message}");
    }
}

await using (var db = new OutboxDbContext(options))
{
    var outboxMessage = await db.OutboxMessages.SingleAsync();
    var inboxLineCount = (await File.ReadAllLinesAsync(InboxPath))
        .Count(l => !string.IsNullOrWhiteSpace(l));

    Console.WriteLine(
        $"After the crash: OutboxMessage.Sent={outboxMessage.Sent} (expect false - " +
        $"the row was never lost, just never marked complete), inbox has {inboxLineCount} line(s)");
}

Console.WriteLine();
Console.WriteLine("=== Scenario C: relay 'restarts' and finds the still-unsent row ===");

await using (var db = new OutboxDbContext(options))
{
    var publisher = new FileMessagePublisher(InboxPath);
    var relay = new OutboxRelay(db, publisher);

    var sentCount = await relay.ProcessPendingAsync();

    Console.WriteLine($"Relay processed {sentCount} message(s) on this pass.");
}

await using (var db = new OutboxDbContext(options))
{
    var outboxMessage = await db.OutboxMessages.SingleAsync();
    var inboxLineCount = (await File.ReadAllLinesAsync(InboxPath))
        .Count(l => !string.IsNullOrWhiteSpace(l));

    Console.WriteLine(
        $"After the restart: OutboxMessage.Sent={outboxMessage.Sent}, " +
        $"inbox now has {inboxLineCount} line(s) for the same message id " +
        "(the earlier crash's publish plus this one - a genuine duplicate delivery)");
}

Console.WriteLine();
Console.WriteLine("=== Consumer processes the inbox ===");

var (applied, duplicatesSkipped) = await Consumer.ProcessInboxAsync(InboxPath);

Console.WriteLine(
    $"Consumer result: applied={applied}, duplicatesSkipped={duplicatesSkipped} " +
    "(the quote was delivered twice but applied exactly once)");
