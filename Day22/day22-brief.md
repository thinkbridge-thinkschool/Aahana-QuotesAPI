# Day 22 brief — resilience with Polly

## Target

- **Outbound dependency to wrap:** `IFunFactProvider` / `ChuckNorrisFactProvider`
  (`Services/ChuckNorrisFactProvider.cs`), a genuine outbound HTTP call to
  `https://api.chucknorris.io/jokes/random` — a free, unauthenticated public
  API. Not on any write path: it enriches a quote read, so a failure here
  should degrade the enrichment, never break the quote itself.
- **Why this dependency and not a mocked one for everything:** the app had a
  named `HttpClient("my-service")` sitting in `Program.cs` since an earlier
  day, with a Polly pipeline wrapped around it that got stripped out
  (`git show 3bd5b64`: *"drop the unused Polly resilience pipeline
  registration"*) because nothing ever called it. This exercise gives it a
  real caller instead of re-adding dead scaffolding.
- **New endpoint:** `GET /api/quotes/{id}/fact` — looks up the quote, then
  calls the fact API through the resilience pipeline; returns
  `{ quote, fact }` on success, `404` if the quote doesn't exist, `503` if
  the fact API is unavailable (circuit open, timed out, rate-limited, or a
  network error) after retries are exhausted.

## Resilience pipeline (`Extensions/HttpResilienceExtensions.cs`)

Built on `Microsoft.Extensions.Http.Resilience` (Polly v8 under the hood),
already referenced in `QuotesApi.csproj`. Order, outermost to innermost —
matches the ordering `AddStandardResilienceHandler` uses, because each
outer stage exists to bound how much damage a failing inner stage can do
before it even gets a chance to run:

1. **Bulkhead** — `AddConcurrencyLimiter` caps concurrent outbound calls to
   the fact API; excess calls are rejected immediately (no queueing), so a
   stalled dependency can't exhaust the app's threads/connections.
2. **Total timeout** — one wall-clock budget across every attempt of a
   single logical call.
3. **Retry** — exponential backoff + jitter, **idempotent methods only**
   (GET/HEAD/PUT/DELETE/OPTIONS — checked via the request's own
   `HttpMethod`, not just "this client only ever does GETs"), and only for
   transient outcomes (`HttpClientResiliencePredicates.IsTransient`: 5xx,
   408, timeouts, network errors).
4. **Circuit breaker** — opens once a failure ratio threshold is exceeded
   over a minimum sampled throughput within a sampling window; while open,
   calls fast-fail with `BrokenCircuitException` without ever reaching the
   handler; after the break duration it allows one half-open trial call,
   closing on success.
5. **Per-attempt timeout** — tighter than the total budget, so one hung
   attempt can't burn the whole retry allowance.

`FactApiResilienceOptions` holds the tunables (record, with a `Default` for
production); tests build the same pipeline with a shorter `BreakDuration`
so the breaker's full open → half-open → closed cycle runs in under a
second instead of 15+.

## Non-negotiables

- Retry must provably skip non-idempotent methods (a POST must not be
  retried even against a sustained-failing dependency).
- The breaker must provably open under sustained failure, fast-fail while
  open (handler not invoked), then half-open and close on recovery — shown
  with logs/assertions, not just described.
- The enrichment endpoint must degrade gracefully (503, not a 500 or a
  hung request) when the dependency is down, and must not affect
  `GET /api/quotes/{id}` (unrelated, unchanged).
