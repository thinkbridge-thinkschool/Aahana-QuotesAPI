using System.Diagnostics;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

const string connectionString = "Data Source=day12.db";

// ============================================================
// DATABASE SETUP
// ============================================================

using (var connection = new SqliteConnection(connectionString))
{
    connection.Open();

    using var command = connection.CreateCommand();

    command.CommandText =
        """
        CREATE TABLE IF NOT EXISTS Quotes
        (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Author TEXT NOT NULL,
            Text TEXT NOT NULL
        );
        """;

    command.ExecuteNonQuery();

    command.CommandText = "SELECT COUNT(*) FROM Quotes;";

    var count = Convert.ToInt32(command.ExecuteScalar());

    if (count == 0)
    {
        command.CommandText =
            """
            INSERT INTO Quotes (Author, Text)
            VALUES
            ('Albert Einstein', 'Life is like riding a bicycle.'),
            ('William Shakespeare', 'To be, or not to be.'),
            ('Oscar Wilde', 'Be yourself; everyone else is already taken.');
            """;

        command.ExecuteNonQuery();
    }
}

// ============================================================
// EF CORE IMPLEMENTATION
// ============================================================

var efOptions =
    new DbContextOptionsBuilder<QuotesDbContext>()
        .UseSqlite(connectionString)
        .Options;

using var db = new QuotesDbContext(efOptions);

var stopwatch = Stopwatch.StartNew();

var efQuotes = await db.Quotes
    .AsNoTracking()
    .OrderBy(q => q.Id)
    .Select(q => new QuoteReadModel(
        q.Id,
        q.Author,
        q.Text))
    .ToListAsync();

stopwatch.Stop();

var efTime = stopwatch.Elapsed.TotalMilliseconds;

// ============================================================
// DAPPER IMPLEMENTATION
// ============================================================

using var dapperConnection =
    new SqliteConnection(connectionString);

await dapperConnection.OpenAsync();

stopwatch.Restart();

var dapperQuotes =
    (await dapperConnection.QueryAsync<QuoteReadModel>(
        """
        SELECT
            Id,
            Author,
            Text
        FROM Quotes
        ORDER BY Id;
        """))
    .ToList();

stopwatch.Stop();

var dapperTime = stopwatch.Elapsed.TotalMilliseconds;

// ============================================================
// RESULTS
// ============================================================

Console.WriteLine("===== EF CORE =====");
Console.WriteLine($"Rows: {efQuotes.Count}");
Console.WriteLine($"Time: {efTime:F3} ms");

Console.WriteLine();

Console.WriteLine("===== DAPPER =====");
Console.WriteLine($"Rows: {dapperQuotes.Count}");
Console.WriteLine($"Time: {dapperTime:F3} ms");

Console.WriteLine();

Console.WriteLine("===== SQL =====");
Console.WriteLine(
    "SELECT Id, Author, Text FROM Quotes ORDER BY Id;");

Console.WriteLine();

Console.WriteLine("===== COMPARISON =====");

if (dapperTime < efTime)
{
    Console.WriteLine(
        $"Dapper was {(efTime / dapperTime):F2}x faster in this run.");
}
else if (efTime < dapperTime)
{
    Console.WriteLine(
        $"EF Core was {(dapperTime / efTime):F2}x faster in this run.");
}
else
{
    Console.WriteLine("Both implementations had the same timing.");
}

// ============================================================
// READ MODEL
// ============================================================

public sealed record QuoteReadModel(
    long Id,
    string Author,
    string Text);

// ============================================================
// EF ENTITY
// ============================================================

public sealed class QuoteEntity
{
    public int Id { get; set; }

    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

// ============================================================
// EF DB CONTEXT
// ============================================================

public sealed class QuotesDbContext : DbContext
{
    public QuotesDbContext(
        DbContextOptions<QuotesDbContext> options)
        : base(options)
    {
    }

    public DbSet<QuoteEntity> Quotes =>
        Set<QuoteEntity>();
}