using Microsoft.EntityFrameworkCore;

var options = new DbContextOptionsBuilder<QuoteDbContext>()
    .UseSqlite("Data Source=query-demo.db")
    .LogTo(Console.WriteLine)
    .EnableSensitiveDataLogging()
    .Options;

await using var db = new QuoteDbContext(options);

await db.Database.EnsureDeletedAsync();
await db.Database.EnsureCreatedAsync();

db.Quotes.AddRange(
    new Quote
    {
        Author = "Albert Einstein",
        Text = "Life is like riding a bicycle."
    },
    new Quote
    {
        Author = "Mark Twain",
        Text = "The secret of getting ahead is getting started."
    }
);

await db.SaveChangesAsync();


// ============================================================
// ORIGINAL QUERY — FULL ENTITY
// ============================================================

Console.WriteLine();
Console.WriteLine("===== ORIGINAL: FULL ENTITY =====");

var fullEntities = await db.Quotes
    .Where(q => q.Author.Contains("a"))
    .ToListAsync();

foreach (var quote in fullEntities)
{
    Console.WriteLine(
        $"{quote.Id}: {quote.Author} - {quote.Text}");
}


// ============================================================
// CLIENT-SIDE EVALUATION / TRANSLATION TEST
// ============================================================

Console.WriteLine();
Console.WriteLine("===== CLIENT-SIDE EVALUATION TEST =====");

try
{
    var clientEval = await db.Quotes
        .Where(q => ClientOnlyHelpers.IsLongAuthor(q.Author))
        .ToListAsync();

    Console.WriteLine($"Rows: {clientEval.Count}");
}
catch (Exception ex)
{
    Console.WriteLine(
        $"Client-side evaluation caught: {ex.GetType().Name}");

    Console.WriteLine(ex.Message);
}


// ============================================================
// FIXED VERSION — SQL-TRANSLATABLE QUERY
// ============================================================

Console.WriteLine();
Console.WriteLine("===== FIXED: SQL-TRANSLATABLE QUERY =====");

var fixedQuery = await db.Quotes
    .Where(q => q.Author.Length > 5)
    .ToListAsync();

foreach (var quote in fixedQuery)
{
    Console.WriteLine(
        $"{quote.Id}: {quote.Author}");
}


// ============================================================
// PROJECTION — ONLY NEEDED COLUMNS
// ============================================================

Console.WriteLine();
Console.WriteLine("===== PROJECTION: ONLY NEEDED COLUMNS =====");

var projected = await db.Quotes
    .Where(q => q.Author.Contains("a"))
    .Select(q => new QuoteDto
    {
        Id = q.Id,
        Author = q.Author
    })
    .ToListAsync();

foreach (var quote in projected)
{
    Console.WriteLine(
        $"{quote.Id}: {quote.Author}");
}


// ============================================================
// EF CORE CONTEXT
// ============================================================

public class QuoteDbContext : DbContext
{
    public QuoteDbContext(
        DbContextOptions<QuoteDbContext> options)
        : base(options)
    {
    }

    public DbSet<Quote> Quotes => Set<Quote>();
}


// ============================================================
// ENTITY
// ============================================================

public class Quote
{
    public int Id { get; set; }

    public string Author { get; set; } = "";

    public string Text { get; set; } = "";
}


// ============================================================
// DTO
// ============================================================

public class QuoteDto
{
    public int Id { get; set; }

    public string Author { get; set; } = "";
}


// ============================================================
// NON-TRANSLATABLE HELPER
// ============================================================

public static class ClientOnlyHelpers
{
    public static bool IsLongAuthor(string author)
    {
        return author.Length > 5;
    }
}