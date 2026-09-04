# Day 22 submission — resilience with Polly

## 1. Brief

See [`day22-brief.md`](./day22-brief.md).

## 2. Agent output

| Piece | File(s) |
|---|---|
| Resilience pipeline (bulkhead + timeout + retry + circuit breaker), reusable by production and tests | `Extensions/HttpResilienceExtensions.cs` |
| Outbound dependency abstraction + real implementation (calls `api.chucknorris.io`) | `Abstractions/IFunFactProvider.cs`, `Services/ChuckNorrisFactProvider.cs` |
| Wiring: typed `HttpClient` for the fact API + pipeline attached + breaker transitions logged | `Program.cs` (replaces the old dead `AddHttpClient("my-service")` scaffolding) |
| New endpoint that actually calls the wrapped dependency, degrading to 503 on failure | `Extensions/QuoteEndpointExtensions.cs` (`GET /api/quotes/{id}/fact`) |
| Deterministic proof: breaker opens → fast-fails → half-opens → closes; retry is idempotent-only | `QuotesApi.Tests/FactApiResilienceTests.cs` |

## 3. The resilience pipeline

```csharp
// Extensions/HttpResilienceExtensions.cs
public static IHttpResiliencePipelineBuilder AddFactApiResilience(
    this IHttpClientBuilder httpClientBuilder,
    FactApiResilienceOptions options,
    Action<TimeSpan>? onOpened = null,
    Action? onHalfOpened = null,
    Action? onClosed = null)
{
    return httpClientBuilder.AddResilienceHandler("fact-api", builder =>
    {
        // Bulkhead: cap concurrent outbound calls so a stalled dependency
        // can't exhaust the thread/connection pool. Reject over capacity
        // immediately rather than queue - a queued caller is just a
        // slower failure.
        builder.AddConcurrencyLimiter(
            options.ConcurrencyLimit,
            options.ConcurrencyQueueLimit);

        // Total budget across all attempts for one logical call.
        builder.AddTimeout(options.TotalTimeout);

        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = options.RetryMaxAttempts,
            BackoffType = DelayBackoffType.Exponential,
            Delay = options.RetryBaseDelay,
            UseJitter = true,
            ShouldHandle = args =>
            {
                var method = args.Context.GetRequestMessage()?.Method;

                var isIdempotent = method is not null &&
                    (method == HttpMethod.Get || method == HttpMethod.Head ||
                     method == HttpMethod.Put || method == HttpMethod.Delete ||
                     method == HttpMethod.Options);

                if (!isIdempotent) return ValueTask.FromResult(false);

                return ValueTask.FromResult(
                    HttpClientResiliencePredicates.IsTransient(args.Outcome));
            }
        });

        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = options.CircuitBreakerFailureRatio,
            SamplingDuration = options.CircuitBreakerSamplingDuration,
            MinimumThroughput = options.CircuitBreakerMinimumThroughput,
            BreakDuration = options.CircuitBreakerBreakDuration,
            OnOpened = args => { onOpened?.Invoke(args.BreakDuration); return ValueTask.CompletedTask; },
            OnHalfOpened = _ => { onHalfOpened?.Invoke(); return ValueTask.CompletedTask; },
            OnClosed = _ => { onClosed?.Invoke(); return ValueTask.CompletedTask; }
        });

        // Per-attempt timeout, tighter than the total budget so one hung
        // attempt can't burn the whole retry allowance.
        builder.AddTimeout(options.AttemptTimeout);
    });
}
```

Wired in `Program.cs`:

```csharp
builder.Services
    .AddHttpClient<IFunFactProvider, ChuckNorrisFactProvider>(client =>
    {
        client.BaseAddress = new Uri("https://api.chucknorris.io/");
    })
    .AddFactApiResilience(
        FactApiResilienceOptions.Default,
        onOpened: breakDuration => Log.Warning(
            "Circuit breaker for fact-api OPENED for {BreakDuration}", breakDuration),
        onHalfOpened: () => Log.Information(
            "Circuit breaker for fact-api HALF-OPENED, testing recovery"),
        onClosed: () => Log.Information(
            "Circuit breaker for fact-api CLOSED, dependency recovered"));
```

`FactApiResilienceOptions.Default` (production tuning): bulkhead 5 concurrent /
no queue, 8s total timeout, 3 retries with exponential backoff + jitter
(idempotent only), breaker at 50% failure ratio over a 10s window with a
minimum of 4 sampled calls, 15s break duration, 3s per-attempt timeout.

## 4. Verification log — live, not simulated

### Live call against the real dependency

The app was actually run (`dotnet run`, `http://localhost:5145`) and hit with
real HTTP requests against the real `api.chucknorris.io`:

```
$ curl http://localhost:5145/api/quotes/2/fact
{"quote":{"id":2,"userId":1,"author":"Test Author","text":"A test quote for my collection.","isDeleted":false},
 "fact":"Chuck Norris doesn't give you a 'pearl necklace', he simply kills you in a really gross way."}
HTTP 200

$ curl http://localhost:5145/api/quotes/99999/fact
HTTP 404
```

Server log for the successful call shows the pipeline's own attempt telemetry
(`Microsoft.Extensions.Http.Resilience`'s built-in instrumentation, emitted
through the same Serilog console sink as every other request):

```
[11:05:00 INF] Received HTTP response headers after 2499.8ms - 200
[11:05:00 INF] Execution attempt. Source: 'IFunFactProvider-fact-api//Retry',
               Operation Key: 'null', Result: '200', Handled: 'False',
               Attempt: '0', Execution Time: 2517.4ms
[11:05:00 INF] Executed endpoint 'HTTP: GET /api/quotes/{id:int}/fact'
[11:05:00 INF] Request completed 200
```

### Breaker opening → half-opening → recovering (deterministic test)

Live third-party APIs won't fail exactly N times on command, so the
open/half-open/closed cycle is proven against a scripted local
`HttpMessageHandler` driving the *exact* production pipeline
(`AddFactApiResilience`), only with a shorter `BreakDuration` (600ms instead
of 15s) so the full cycle runs in under a second
(`QuotesApi.Tests/FactApiResilienceTests.cs`):

```
$ dotnet test --filter FactApiResilienceTests --logger "console;verbosity=detailed"

Passed QuotesApi.Tests.FactApiResilienceTests.CircuitBreaker_Opens_Under_Sustained_Failure_Then_Recovers [1 s]
  Standard Output Messages:
 [11:07:05.734] --- sustained failures against the fact API ---
 [11:07:05.883] call 0: 503 (handler invocations so far: 3)
 [11:07:05.890] CIRCUIT OPENED for 00:00:00.6000000
 [11:07:05.914] call 1: BrokenCircuitException, fast-failed (handler invocations so far: 4)
 [11:07:05.931] call while open: BrokenCircuitException, fast-failed (handler invocations: 4, unchanged: True)
 [11:07:05.934] --- dependency recovers, waiting out the break duration ---
 [11:07:06.851] CIRCUIT HALF-OPENED, testing recovery
 [11:07:06.857] CIRCUIT CLOSED, dependency recovered
 [11:07:06.857] recovery call: 200

Passed QuotesApi.Tests.FactApiResilienceTests.Retry_Only_Applies_To_Idempotent_Methods [42 ms]

Test Run Successful.
Total tests: 2
     Passed: 2
```

What this proves, read line by line:

- **call 0** exhausted its retries (3 handler invocations: 1 initial + 2
  retries) against a sustained-failing dependency and still returned a
  plain 503 — retries don't turn a handled failure into a crash.
- **CIRCUIT OPENED** fired mid-flight, as soon as the sampled failure ratio
  crossed the threshold — not something the test forced, something the
  pipeline itself decided.
- **call 1** onward got `BrokenCircuitException` immediately, and the
  handler invocation count froze at 4 — proof the breaker is genuinely
  short-circuiting calls, not just logging a warning while still hitting
  the dependency.
- After the break duration elapsed and the dependency was flipped healthy,
  the **next** call triggered **HALF-OPENED** then **CLOSED** in the same
  call, before returning **200** — the trial call and the recovery
  happened atomically, exactly as the circuit-breaker pattern promises.

### Retry is idempotent-only

The second test (`Retry_Only_Applies_To_Idempotent_Methods`) hits the same
sustained-failing handler with a POST and a GET: the POST reaches the
handler exactly once (no retry), the GET is retried multiple times. Both
passed.

### Regression check

Full existing suite still green after this change:

```
$ dotnet test  (QuotesApi.Tests)
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9
```

`Quotes.Tests.Integration` still builds clean against the changed
`Program.cs`/`QuoteEndpointExtensions.cs`.

## What I learned this session

The circuit breaker has to sit *inside* the retry, not outside it — every
retry attempt passes back through the breaker, so a breaker that trips
mid-retry-sequence turns the *next* attempt into an immediate
`BrokenCircuitException`, and because that exception isn't in the retry's
own "this is worth retrying" predicate, the retry strategy gives up
immediately instead of burning its remaining attempts hammering an already
-open circuit. Getting the pipeline order right (bulkhead → total timeout →
retry → circuit breaker → attempt timeout, the same order
`AddStandardResilienceHandler` uses) is what makes that handoff work — it's
not just a stylistic choice.

The other thing that actually mattered: proving "idempotent only" retry
means checking the *specific request's* HTTP method (`args.Context
.GetRequestMessage()?.Method`) inside `ShouldHandle`, not just trusting
that "this client only ever calls GET" — the pipeline is reusable across
any HTTP method sent through the same named handler, and only the request
itself can say whether retrying it is actually safe.

## What would break this

- **A live third-party API that's flaky in a way that doesn't cleanly hit
  the sampled failure ratio** (e.g. failing exactly at the sampling
  boundary, or with enough jitter that `MinimumThroughput` is never met
  within `SamplingDuration`) would leave the breaker permanently closed
  while the dependency is still visibly unhealthy to callers — the
  deterministic test sidesteps this by scripting failures, but the
  production tuning (`FactApiResilienceOptions.Default`) has never been
  validated against `api.chucknorris.io`'s actual failure characteristics
  under load, only against its (currently healthy) happy path.
- **If `ChuckNorrisFactProvider.GetFactAsync` is ever changed to call a
  non-idempotent method** (unlikely for a fact lookup, but a copy-paste
  into some other provider), the retry's idempotency check protects it
  automatically — but the circuit breaker's own `ShouldHandle` (the
  default `IsTransient`, unfiltered by method) will still count a failing
  POST against the shared breaker state, meaning a flaky non-idempotent
  call could trip the breaker for every other caller of the same client,
  GETs included.
- **The bulkhead's `queueLimit: 0`** means any burst past
  `ConcurrencyLimit` (5) is rejected outright with
  `RateLimiterRejectedException` rather than queued — correct for keeping
  latency bounded, but it means a legitimate traffic spike (not a failing
  dependency at all) surfaces to the caller as the exact same class of
  "service unavailable" as a genuinely broken dependency; nothing in the
  current logging distinguishes "bulkhead full" from "circuit open" for an
  operator glancing at the 503 rate.

## Notes for mentor

`ChuckNorrisFactProvider` is a real, unauthenticated public API
(`api.chucknorris.io`) chosen specifically so the outbound dependency is
genuine (not a mock) while being safe to demo without secrets or rate-limit
concerns. The breaker's open → half-open → closed cycle is proven against a
scripted local handler (not the live third-party API) because a real API
won't fail exactly N times on command — the test builds the *exact*
production pipeline (`HttpResilienceExtensions.AddFactApiResilience`) with
a shorter `BreakDuration`, so it's the shipped code under test, not a
reimplementation.
