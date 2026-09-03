using Day21;

using Microsoft.Extensions.Caching.Hybrid;

using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<QuoteRepository>();

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";

    options.ConfigurationOptions = new ConfigurationOptions
    {
        EndPoints = { "localhost:6379" },
        AbortOnConnectFail = false,
        ConnectTimeout = 1000,

        // The defaults (5000ms) are far too generous for a cache that's
        // supposed to degrade gracefully - a genuinely down Redis
        // shouldn't cost every request several seconds before HybridCache
        // falls through to the factory. Confirmed live: without this,
        // an unavailable L2 turned a ~100ms DB call into a ~6s one.
        SyncTimeout = 500,
        AsyncTimeout = 500
    };
});

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };
});

var app = builder.Build();

app.MapGet("/quotes/{id:int}/cached", async (
    int id,
    HybridCache cache,
    QuoteRepository repository,
    CancellationToken cancellationToken) =>
{
    // GetOrCreateAsync is where the stampede protection lives: if N
    // concurrent callers ask for the same key while it's missing, the
    // factory below runs once, not N times - every caller shares the
    // single in-flight result.
    var quote = await cache.GetOrCreateAsync(
        $"quote:{id}",
        async ct => await repository.GetQuoteAsync(id, ct),
        cancellationToken: cancellationToken);

    return Results.Ok(quote);
});

app.MapGet("/quotes/{id:int}/uncached", async (
    int id,
    QuoteRepository repository,
    CancellationToken cancellationToken) =>
{
    var quote = await repository.GetQuoteAsync(id, cancellationToken);
    return Results.Ok(quote);
});

app.MapPost("/admin/reset/{id:int}", async (
    int id,
    HybridCache cache,
    QuoteRepository repository) =>
{
    repository.ResetCallCount();
    await cache.RemoveAsync($"quote:{id}");
    return Results.NoContent();
});

app.MapGet("/admin/stats", (QuoteRepository repository) =>
    Results.Ok(new { dbCallCount = repository.DbCallCount }));

if (args.Contains("loadtest"))
{
    await RunLoadTestAsync(app);
}
else
{
    app.Run();
}

return;

static async Task RunLoadTestAsync(WebApplication app)
{
    await app.StartAsync();

    var baseUrl = app.Urls.First();
    using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };

    const int Concurrency = 50;

    Console.WriteLine($"=== Baseline: {Concurrency} concurrent requests, NO cache ===");

    var baselineResult = await LoadTester.RunConcurrentAsync(
        client, "/quotes/1/uncached", Concurrency);

    var baselineDbCalls = (await client.GetFromJsonAsync<StatsResponse>("/admin/stats"))!.DbCallCount;

    PrintResult(baselineResult, baselineDbCalls, Concurrency);

    await client.PostAsync("/admin/reset/1", null);

    Console.WriteLine();
    Console.WriteLine($"=== HybridCache, cold cache: {Concurrency} concurrent requests for the SAME key (stampede scenario) ===");

    var stampedeResult = await LoadTester.RunConcurrentAsync(
        client, "/quotes/1/cached", Concurrency);

    var stampedeDbCalls = (await client.GetFromJsonAsync<StatsResponse>("/admin/stats"))!.DbCallCount;

    PrintResult(stampedeResult, stampedeDbCalls, Concurrency);

    Console.WriteLine();
    Console.WriteLine($"=== HybridCache, warm cache: {Concurrency} more concurrent requests for the SAME key ===");

    var warmResult = await LoadTester.RunConcurrentAsync(
        client, "/quotes/1/cached", Concurrency);

    var warmDbCalls = (await client.GetFromJsonAsync<StatsResponse>("/admin/stats"))!.DbCallCount;

    PrintResult(warmResult, warmDbCalls, Concurrency);

    Console.WriteLine();
    Console.WriteLine("=== Summary ===");
    Console.WriteLine($"Baseline (no cache):      {baselineDbCalls} DB calls for {Concurrency} requests, p99 {baselineResult.P99Ms:F1} ms");
    Console.WriteLine($"Cold cache (stampede):    {stampedeDbCalls - baselineDbCalls} DB calls for {Concurrency} requests, p99 {stampedeResult.P99Ms:F1} ms");
    Console.WriteLine($"Warm cache:               {warmDbCalls - stampedeDbCalls} DB calls for {Concurrency} requests, p99 {warmResult.P99Ms:F1} ms");

    await app.StopAsync();
}

static void PrintResult(LoadTestResult result, long dbCalls, int concurrency)
{
    Console.WriteLine(
        $"{result.RequestCount} requests in {result.TotalSeconds:F3}s " +
        $"({result.RequestsPerSecond:F1} req/s) - p50 {result.P50Ms:F1} ms, p99 {result.P99Ms:F1} ms - " +
        $"DB calls so far: {dbCalls}");
}

sealed record StatsResponse(long DbCallCount);
