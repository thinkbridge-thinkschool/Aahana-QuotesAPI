\# Day 12 — When to Reach for Dapper



\## EF Core Implementation



```csharp

var efOptions =

&#x20;   new DbContextOptionsBuilder<QuotesDbContext>()

&#x20;       .UseSqlite(connectionString)

&#x20;       .Options;



using var db = new QuotesDbContext(efOptions);



var stopwatch = Stopwatch.StartNew();



var efQuotes = await db.Quotes

&#x20;   .AsNoTracking()

&#x20;   .OrderBy(q => q.Id)

&#x20;   .Select(q => new QuoteReadModel(

&#x20;       q.Id,

&#x20;       q.Author,

&#x20;       q.Text))

&#x20;   .ToListAsync();



stopwatch.Stop();



var efTime = stopwatch.Elapsed.TotalMilliseconds;

