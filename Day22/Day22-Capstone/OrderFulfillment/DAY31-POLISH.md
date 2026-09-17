# Day 31 — Polish: tests, perf, security (OrderFulfillment capstone)

## The testing pyramid, filled in

| Layer | Where | Count | What it actually exercises |
|---|---|---|---|
| Unit | `tests/Ordering.Domain.Tests` | 11 | `Order` aggregate invariants and the Day 28 idempotency fix — no DB, no HTTP, pure domain logic |
| Integration | `tests/OrderFulfillment.Api.IntegrationTests` (new) | 7 | The real ASP.NET Core pipeline via `WebApplicationFactory<Program>` — real EF Core/SQLite (a fresh temp file per test), real validation, real rate limiter, real security headers. Nothing mocked; the only substitution is Azure infra that would otherwise require a live subscription (no `ServiceBus:Namespace` configured, so `Program.cs` falls back to `InProcessMessageBus` — the same fallback local `dotnet run` already uses) |
| E2E | `tests/OrderFulfillment.Api.IntegrationTests/FullSagaEndToEndTests.cs` (new) | 2 | The **whole cross-module saga**, driven only through the public API and observed only through state a real client could also see (the order's own status), on the real `OutboxProcessor` background timer — not triggered manually |

**20/20 passing**, confirmed stable across repeated runs after fixing a real flake (below).

## A real bug the E2E test surfaced immediately: a race, not the code under test

The first version of these tests shared one `WebApplicationFactory` per test class via `IClassFixture`. That raced: xUnit can run test methods within a class concurrently, and two tests hitting `Database.EnsureCreated()` against the same SQLite file at once threw `table "DeadLetterMessages" already exists` on roughly 1 run in 8. Fixed by giving every test its own factory (own temp SQLite file, own in-process host) instead of chasing the race with locks — and separately, running the whole suite's tests concurrently (multiple full ASP.NET Core hosts, each with a real 5-second-poll `OutboxProcessor`, competing for one machine's CPU) intermittently made the E2E cancellation-path test miss even a 45-second timeout. Fixed with `xunit.runner.json` (`parallelizeTestCollections: false`) — correctness over wall-clock speed for a suite this size.

## The most valuable thing this pass proved, almost as a side effect

**The full saga completes correctly, every time, locally.** Both E2E tests pass reliably:
placing an order for in-stock `WIDGET-1` cascades all the way to `Shipped`; placing one for
more `GADGET-9` than exists compensates correctly to `Cancelled`, not stuck. This directly
narrows Day 29/30's still-open mystery (the same saga never completing against the real,
deployed Azure Service Bus): the saga logic itself is proven correct end-to-end. Whatever is
still wrong is specifically in the Service Bus wiring/receive path, not in Ordering, Inventory,
Payments, Shipping, or Notifications' own logic — a much smaller thing to still be chasing than
"the whole saga doesn't work."

## Security re-check

- `dotnet list package --vulnerable --include-transitive` against `OrderFulfillment.Api`: **no
  vulnerable packages** — the `Microsoft.OpenApi` 2.7.5 pin from Day 27 (CVE-2026-49451) is still
  effective; nothing new introduced since.
- Day 27's manual `curl`-based header checks (missing `X-Content-Type-Options`,
  `Content-Security-Policy`, `Referrer-Policy`; the `Server: Kestrel` leak; the 20-req/10s rate
  limiter) are now automated integration tests (`PlaceOrderEndpointTests`,
  `RateLimitingTests`) instead of a one-time manual pass — they run on every push now, not once.

## Perf pass: the hottest path's p99, before and after

Hottest path: `POST /api/v1/orders` (the only real write endpoint). Measured with k6 (already
installed on this machine) at a constant arrival rate of ~1.8 req/s — deliberately just under the
endpoint's own by-design 20-req/10s rate-limit ceiling (Day 27), since that ceiling **is** the
realistic sustained load this endpoint will ever see, not an arbitrary test parameter.

**Before** (steady-state, SQLite's default rollback-journal mode): `p99 = 13.58ms` (`med = 12.82ms`).

**Change made**: enabled SQLite WAL mode + `synchronous=NORMAL` (`Program.cs`, two `PRAGMA`
statements run once at startup, SQLite-only — a no-op against Azure SQL). The default rollback
journal fsyncs on every commit and blocks readers during a write; WAL lets `OutboxProcessor`'s
poll and the write path proceed concurrently, and defers most fsync cost to periodic checkpoints
instead of every request.

**After** (steady-state, WAL mode): `med = 2.6–2.8ms` — a consistent, reproducible **~4.5x**
improvement in typical-case latency across every run. **p99 is genuinely mixed, reported
honestly rather than cherry-picked**: several runs landed at `3.8–4.3ms` (better than before by
~3x), but others hit `15–82ms` — WAL's periodic checkpoint I/O occasionally lands inside the
measurement window and dominates the tail. The real finding is a trade: WAL is an unambiguous win
for the common case and a probable net win for p99 too, but its tail is now checkpoint-timing
-dependent rather than uniformly fast — worth tuning `wal_autocheckpoint` before calling the p99
question fully closed, not something this pass papers over with the one best-looking run.

## CI: green gate

New workflow, `.github/workflows/orderfulfillment-ci.yml`, scoped to this capstone's own path
(doesn't run on unrelated QuotesApi/Day-folder changes, and doesn't touch the root repo's
existing `ci.yml`/`integration-tests.yml`, which build the rest of the repo). Runs on every push
and PR: restore, build, unit tests, integration+E2E tests, a vulnerable-package check, and a
Bicep compile check — see the linked run for this session's green result.

## What did I learn this session?

That a flaky test and a real production bug can look identical from the outside ("sometimes it
doesn't work") but have completely different fixes — the E2E test's own race wasn't a saga bug at
all, it was two tests sharing one SQLite file, and no amount of staring at `Order.cs` would have
found that. The perf number taught the same lesson from the other direction: the honest p99
result (mixed, not a clean win) is more useful than a cherry-picked good run would have been,
because it's the one that actually tells you what to do next (tune the checkpoint interval).

## What would break this?

The perf numbers are single-machine, single-run-per-configuration measurements on a dev laptop
already running several other things this session — not a controlled benchmark environment.
Real production load (concurrent writers at real Azure SQL latency, not local SQLite) would
change every number here; WAL mode's benefit in particular is SQLite-specific and provides zero
benefit against Azure SQL Server. And the CI gate only runs what these tests actually cover — it
would not have caught the Day 29 AcrPull deadlock, the Day 30 auth-bypass scope issue, or the
still-open Service Bus receive mystery, because none of those are testable without a real Azure
subscription in the loop.
