# Testing

## Test projects and what each actually covers

### `QuotesApi.Tests` — unit tests, no DB, no HTTP (7 tests)
- `CancellationTests.cs` (1) — verifies a collection-item delete request honors `CancellationToken`
  cancellation against a blocking fake repository.
- `CanDeleteOwnQuoteHandlerTests.cs` (5) — exercises `CanDeleteOwnQuoteHandler` directly, in isolation:
  succeeds via `ClaimTypes.NameIdentifier`, succeeds via the raw `"sub"` claim, fails with no user id,
  fails for a nonexistent quote, fails for a quote owned by someone else. (This handler's logic is
  fully tested but not wired into any live endpoint — see `docs/authentication.md`.)
- `ClockTests.cs` (1) — sanity check on the `IClock` abstraction.

### `Tests.Domain` — pure domain unit tests, FluentAssertions (21 tests)
- `QuoteTests.cs` (12) — exhaustive `Quote.Create` invariant coverage: empty/whitespace/too-long author
  and text all throw `DomainInvariantException`; valid creation trims input and preserves the user id;
  `SoftDelete()` sets the flag without mutating content.
- `CollectionTests.cs` (6) — empty/too-long name throws; a 51st item throws (50-item cap); a duplicate
  item throws; removing a missing item throws; add-then-remove leaves an empty collection.
- `RefreshTokenServiceTests.cs` (3) — `RevokeTokenFamily` revokes the presented token, cascades through
  a multi-token replacement chain, and stops gracefully if a chain link points at a token that doesn't
  exist. Runs against a real in-memory SQLite connection rather than mocks.

### `Quotes.Tests.Integration` — full HTTP pipeline, real SQL Server (6 tests, requires Docker)
Uses `WebApplicationFactory<Program>` plus **Testcontainers** to spin up a real SQL Server 2022
container (`CustomWebApplicationFactory.cs`) — a deliberate choice to test against the same database
engine class a real deployment would use, rather than SQLite, catching provider-specific differences
integration tests are meant to catch. Covers: quote-not-found → 404; create without a token → 401;
create with a token lacking the write scope → 403 (correctly distinguishing "not authenticated" from
"authenticated but not authorized"); create with a valid scoped token → 201; create with an expired
token → 401; refresh-token reuse of an already-revoked token → 401, and confirms the entire replacement
chain gets revoked (this is the one place the reuse-detection logic is verified end-to-end against a
real database, not just the isolated `RefreshTokenServiceTests`).

**This suite could not be run in the environment this documentation was written in** — Docker Desktop is
installed but was not running. Everything else in this document was verified with real command output;
this one is reported as unverified rather than assumed passing.

## Frontend tests (`Day15App`, Vitest via the Angular CLI)
- `app.spec.ts` (2) — the root shell component creates and renders.
- `quote.spec.ts` / `quote.service.spec.ts` (1 each) — trivial scaffold tests for an unused, never-wired
  `quote.ts` service left over from Angular CLI generation (`ng generate service quote`) — it's dead
  code alongside the real `quote.service.ts` that the app actually uses. Noted here rather than silently
  deleted, per this project's "don't remove things without being asked" rule.

## Exact commands and last verified results

Run from `C:\Users\273760\thinkschool\QuotesApi\QuotesApi`:

```powershell
dotnet build QuotesApi.csproj
dotnet test QuotesApi.Tests/QuotesApi.Tests.csproj
dotnet test Tests.Domain/Tests.Domain.csproj
# Requires Docker Desktop running:
dotnet test Quotes.Tests.Integration/Quotes.Tests.Integration.csproj
```

Last run (this session):
- `dotnet build QuotesApi.csproj` — **succeeded**, 0 errors, 10 NuGet advisory warnings (see
  "Known Technical Considerations" in the root README).
- `QuotesApi.Tests` — **7/7 passed**.
- `Tests.Domain` — **21/21 passed**.
- `Quotes.Tests.Integration` — **not run**, Docker Desktop not running in this environment.

Run from `C:\Users\273760\thinkschool\QuotesApi\QuotesApi\Day15\Day15App`:

```powershell
npm start          # or: ng serve
ng build --configuration production
ng test --watch=false
```

Last run (this session):
- `ng build --configuration production` — **succeeded**, no errors.
- `ng test --watch=false` — **5/5 passed** (3 test files).

## Bugs found through testing, not assumption, during this project

1. **JWT claim-mapping bug** (`docs/authentication.md`) — found by manually driving the real
  login → create-quote flow over HTTP and observing an unexpected `401`, then confirmed via the API's
  own request log, not by reading the code and guessing.
2. **Zoneless change-detection bug** (`docs/architecture.md`, `docs/day-by-day.md` Day 16) — found the
  same way: the API log showed the `GET /api/quotes` request succeeding, but the UI stayed on
  "Loading…" — which pointed at a rendering bug rather than a network bug.

Both are documented here as real examples of end-to-end verification catching what unit tests alone
did not (and, for the JWT bug, specifically *could* not without Docker available to run the integration
suite where the correct config already existed).
