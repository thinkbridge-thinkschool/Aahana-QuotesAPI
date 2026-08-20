using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

const string connectionString = "Data Source=day10.db";

await using var connection = new SqliteConnection(connectionString);
await connection.OpenAsync();

await using (var command = connection.CreateCommand())
{
    command.CommandText = """
        DROP TABLE IF EXISTS Quotes;

        CREATE TABLE Quotes
        (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Author TEXT NOT NULL,
            Text TEXT NOT NULL
        );
        """;

    await command.ExecuteNonQueryAsync();
}

await using (var command = connection.CreateCommand())
{
    command.CommandText =
        "INSERT INTO Quotes (Author, Text) VALUES ($author, $text);";

    var author = command.CreateParameter();
    author.ParameterName = "$author";

    var text = command.CreateParameter();
    text.ParameterName = "$text";

    command.Parameters.Add(author);
    command.Parameters.Add(text);

    for (var i = 1; i <= 10000; i++)
    {
        author.Value = $"Author {i}";
        text.Value = $"Quote text {i}";
        await command.ExecuteNonQueryAsync();
    }
}

var options = new DbContextOptionsBuilder<BenchmarkDbContext>()
    .UseSqlite(connectionString)
    .Options;

await using var db = new BenchmarkDbContext(options);

Console.WriteLine("10,000 rows created.");
Console.WriteLine();

Console.WriteLine("CHANGE TRACKER DEMONSTRATION");

var trackedQuote = await db.Quotes.FirstAsync();

var sameTrackedQuote = await db.Quotes
    .FirstAsync(q => q.Id == trackedQuote.Id);

Console.WriteLine(
    $"Same instance with tracking: " +
    $"{ReferenceEquals(trackedQuote, sameTrackedQuote)}");

Console.WriteLine(
    $"Tracked entities: " +
    $"{db.ChangeTracker.Entries().Count()}");

var untrackedQuote = await db.Quotes
    .AsNoTracking()
    .FirstAsync();

Console.WriteLine(
    $"AsNoTracking entity tracked: " +
    $"{db.ChangeTracker.Entries().Any(e => e.Entity == untrackedQuote)}");

Console.WriteLine();

await RunBenchmark(
    "WITH TRACKING",
    () => db.Quotes.ToListAsync());

await RunBenchmark(
    "AS NO TRACKING",
    () => db.Quotes.AsNoTracking().ToListAsync());

static async Task RunBenchmark(
    string name,
    Func<Task<List<Quote>>> query)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var before = GC.GetAllocatedBytesForCurrentThread();

    var stopwatch = Stopwatch.StartNew();

    var rows = await query();

    stopwatch.Stop();

    var after = GC.GetAllocatedBytesForCurrentThread();

    Console.WriteLine(name);
    Console.WriteLine($"Rows: {rows.Count}");
    Console.WriteLine($"Time: {stopwatch.ElapsedMilliseconds} ms");
    Console.WriteLine($"Allocated: {after - before:N0} bytes");
    Console.WriteLine();
}

public class BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options)
    : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();
}

public class Quote
{
    public int Id { get; set; }
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
}