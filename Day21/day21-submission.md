# Day 21 — HybridCache + stampede protection

Ran against a real local Redis (Docker, `redis:7-alpine`) for the L2 tier, plus HybridCache's built-in in-memory L1 - both genuinely exercised, not simulated.

## The cache wiring

`Program.cs`:

```csharp
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
```

The hot read, with stampede protection built entirely into `GetOrCreateAsync`:

```csharp
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
```

Nothing in the endpoint checks "is this a duplicate call" - `HybridCache` does that coalescing internally; the handler code is identical to what you'd write with no stampede protection at all.

## The load test, before/after — real numbers

50 concurrent requests (`Task.WhenAll` over already-created tasks, not a sequential loop) against the same `QuoteRepository`, which simulates a ~100ms "hot but slow" read and counts every real call it receives.

```
=== Baseline: 50 concurrent requests, NO cache ===
50 requests in 0.303s (165.2 req/s) - p50 294.6 ms, p99 300.9 ms - DB calls so far: 50

=== HybridCache, cold cache: 50 concurrent requests for the SAME key (stampede scenario) ===
50 requests in 0.139s (359.6 req/s) - p50 125.9 ms, p99 130.2 ms - DB calls so far: 1

=== HybridCache, warm cache: 50 more concurrent requests for the SAME key ===
50 requests in 0.015s (3365.9 req/s) - p50 0.4 ms, p99 0.7 ms - DB calls so far: 1

=== Summary ===
Baseline (no cache):      50 DB calls for 50 requests, p99 300.9 ms
Cold cache (stampede):    1 DB call for 50 requests, p99 130.2 ms
Warm cache:               0 DB calls for 50 requests, p99 0.7 ms
```

| | DB calls for 50 concurrent requests | p99 latency |
|---|---|---|
| No cache (baseline) | **50** | 300.9 ms |
| HybridCache, cold (stampede scenario) | **1** | 130.2 ms |
| HybridCache, warm | **0** | 0.7 ms |

**Stampede protection, proven, not asserted:** without it, 50 concurrent callers hitting an empty cache would each trigger their own DB read - 50 DB calls, same as the no-cache baseline. Instead: **1**. All 50 concurrent requests for the cold key shared the single in-flight factory call and its result, and every one of them still got their answer in ~130ms (close to the ~100ms of one DB read) instead of a naive implementation's worst case of up to 50× that if the DB serialized those 50 identical queries.

**Confirmed the data actually lives in Redis**, not just in-process memory - queried the running container directly:

```
$ docker exec day21-redis redis-cli TYPE "quote:1"
hash
$ docker exec day21-redis redis-cli TTL "quote:1"
283
$ docker exec day21-redis redis-cli HGETALL "quote:1"
data
{"Id":1,"Author":"Ada Lovelace","Text":"That brain of mine is something more than merely mortal."}
absexp
639240081948261332
sldexp
-1
```

`TTL: 283` lines up with the 5-minute (`300s`) `Expiration` configured above, minus the time already elapsed - this is HybridCache's L2 write landing in the real Redis instance, independently verified from outside the .NET process entirely.

## What I learned this session

Stampede protection isn't a setting you turn on - it's the default behavior of `GetOrCreateAsync` itself. There's no separate API to opt into "don't fan out on a miss"; the naive-looking one-liner (`cache.GetOrCreateAsync(key, factory)`) already coalesces concurrent misses for you, which means the *dangerous* version of this code is the one that manually checks `TryGetValue` then calls the factory itself - that's the pattern that reintroduces the stampede HybridCache exists to prevent.

## A real finding along the way: L2 failures aren't free

Ran the same load test once *before* Redis was available (Docker wasn't running yet). `AbortOnConnectFail = false` correctly let the app start and kept serving requests, and the stampede protection number was unaffected - still exactly 1 DB call for 50 concurrent requests. But the *latency* told a different story: with StackExchange.Redis's default 5000ms `SyncTimeout`/`AsyncTimeout`, a genuinely unreachable Redis turned each request's p99 into **~6 seconds** (the connection attempt had to time out before HybridCache could fall through). Lowering `SyncTimeout`/`AsyncTimeout` to 500ms brought that down to ~1 second for the same unavailable-Redis scenario - still a real cost, but an order of magnitude smaller, and configured deliberately rather than left at a default nobody chose on purpose.

## What would break this

- The stampede protection is per-process. In a multi-instance deployment, N running instances each protect their own concurrent callers from a stampede, but a cold cache hitting N instances at once still means up to N DB calls (one per instance), not one — the coalescing doesn't span processes. A distributed lock, or accepting that Redis L2 warms one instance's writes for the others to read, is what actually bounds that.
- `LocalCacheExpiration` (30s here) is shorter than the Redis `Expiration` (5 minutes) on purpose - the in-memory L1 entry expires and re-checks L2 well before the shared Redis entry does, so a single instance can't serve a stale L1 copy for the full 5 minutes after another instance has already invalidated/updated the shared value. Setting them equal (or L1 longer) would silently widen that staleness window.
- Nothing here invalidates the cache when the underlying quote actually changes (an edit or delete). A write path that doesn't also call `HybridCache.RemoveAsync` for the affected key leaves stale data being served correctly and quickly right up until the TTL expires - which is a much harder bug to notice than a slow cache, because everything *looks* like it's working.
