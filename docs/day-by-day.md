# Day-by-Day Learning Journey

**Methodology note, read this first:** only two commits in the entire git history carry an explicit
`Day N` label in their message — Day 2 (`62f043d`) and Day 3 (`8b2f405`). Day 1 is inferred as "the
first calendar day of work, before any Day label appears." Days 4 and 6 have no label at all; what
follows for them is a dated reconstruction from commit content, clearly marked as inference, not fact.
Days 7–15 are grounded in dedicated folders and (for most of them) a submission `.md` file written at
the time. Day 16 covers this session's work, verified by this session directly. Nowhere below is content
invented to fill a gap — where evidence runs out, that's stated plainly instead.

---

### Day 1
- **Goal:** stand up a minimal working Quotes API and its first aggregate.
- **What I implemented:** the initial Minimal API skeleton — `QuoteDbContext`, the `Quote` model,
  `IQuoteRepository`/`QuoteRepository`, `QuoteEndpointExtensions` (GET/POST), `ExceptionHandlingMiddleware`,
  the first EF Core migration (`InitialCreate`), and a `Collection` aggregate with `CollectionItem` as an
  EF-owned value object.
- **Why I implemented it:** needed a working vertical slice — API → domain → database — before building
  anything else on top of it.
- **Technologies used:** ASP.NET Core Minimal APIs, EF Core, SQLite.
- **Important files:** `Data/QuoteDbContext.cs`, `Models/Quote.cs`, `Models/Collection.cs`,
  `Repositories/QuoteRepository.cs`, `Migrations/20260810084602_InitialCreate.cs`.
- **What I learned:** how to compose a Minimal API project from scratch — endpoint groups, DbContext
  registration, and the repository seam between them.
- **Interview question I should be ready for:** "Why Minimal APIs instead of Controllers?" — be ready to
  discuss the tradeoff (less ceremony for a small surface area, vs. losing some MVC conventions/filters).
- **Confidence:** inferred from the earliest commits on this repo (`7ddc2df`, `06a0eae`), not a labeled
  "Day 1" commit.

### Day 2
- **Goal:** introduce testable abstractions and dependency injection.
- **What I implemented:** `IClock`/`IQuoteFormatter` abstractions with `SystemClock`/`QuoteFormatter`
  implementations, registered through DI in `InfrastructureExtensions`; the first unit test project
  (`QuotesApi.Tests`) with `ClockTests.cs`.
- **Why I implemented it:** direct calls to `DateTimeOffset.UtcNow` inside domain/service code aren't
  unit-testable — wrapping time behind an interface makes it fakeable in tests.
- **Technologies used:** .NET's built-in DI container, interface-based abstraction.
- **Important files:** `Abstractions/IClock.cs`, `Abstractions/IQuoteFormatter.cs`,
  `Infrastructure/SystemClock.cs`, `Infrastructure/QuoteFormatter.cs`.
- **What I learned:** the seam-for-testability pattern — abstract the thing you can't control (the clock)
  behind an interface you can fake.
- **Interview question I should be ready for:** "Why wrap `DateTime.UtcNow` at all?" — deterministic
  tests for time-dependent logic (e.g. token expiry, `RevokeTokenFamily`).
- **Confidence:** directly evidenced — commit `62f043d`, explicitly labeled "Day 2 dependency injection."
- **Known follow-up:** `Services/SystemClock.cs` is a second, unused, near-duplicate of
  `Infrastructure/SystemClock.cs` that exists in the repo today — likely an artifact of later refactoring
  rather than Day 2 itself, but worth being able to point to as an example of dead code you can identify
  and explain, not something to be caught off guard by.

### Day 3
- **Goal:** add policy-based authorization and quote ownership.
- **What I implemented:** `CanDeleteOwnQuoteHandler`/`CanDeleteOwnQuoteRequirement` (a resource-based
  authorization handler), the `Quote.UserId` column (migration `AddQuoteUserId`), and the auth endpoint
  extensions.
- **Why I implemented it:** wanted a real example of ASP.NET Core's resource-based authorization model —
  a handler that has to load data (the quote) to make its decision, not just check a claim.
- **Technologies used:** `Microsoft.AspNetCore.Authorization`, custom `IAuthorizationHandler`.
- **Important files:** `Authorization/CanDeleteOwnQuoteHandler.cs`,
  `Authorization/CanDeleteOwnQuoteRequirement.cs`, `Migrations/20260812043101_AddQuoteUserId.cs`.
- **What I learned:** the difference between claim-based authorization (`RequireClaim`) and
  resource-based authorization (a handler that inspects the specific resource being acted on).
- **Interview question I should be ready for:** "Is quote ownership enforced today?" — answer honestly:
  the handler is written and fully unit-tested (5 tests), but it was never wired into the `DELETE`
  endpoint's authorization policy. See `docs/authentication.md` for the exact gap and what wiring it in
  would look like. This is a genuinely good thing to be able to discuss candidly.
- **Confidence:** directly evidenced — commit `8b2f405`, explicitly labeled "Day 3 authorization policies
  and claims."
- **Git hygiene note:** this same commit also accidentally added a 20MB `github.copilot-chat-0.48.1.vsix`
  binary, removed from the working tree three commits later but still present in git history today
  (removing it fully would require a history rewrite — not done here without explicit approval, per
  this project's own rules).

### Day 4
- **Not enough repository evidence for a confident day-boundary reconstruction** — no commit in this
  window carries an explicit "Day 4" label. What *is* dated to the window between Day 3 (2026-08-12) and
  the first Day 7 commit (2026-08-17) includes: JWT login + refresh token issuance and rotation, and the
  first comprehensive unit/integration test coverage (SQL Server via Testcontainers). This is plausible
  Day 4 content, but presented as a dated inference, not a fact — fill in the actual boundary yourself
  from memory if you want this section to be precise for an interviewer.
- **Likely technologies (if this is correct):** JWT (`System.IdentityModel.Tokens.Jwt`), BCrypt,
  Testcontainers, xUnit/FluentAssertions.
- **Likely important files (if this is correct):** `Extensions/AuthEndpointExtensions.cs`,
  `Services/RefreshTokenService.cs`, `Quotes.Tests.Integration/CustomWebApplicationFactory.cs`.

### Day 5
- **Goal:** an observability exercise around a deliberately slow endpoint.
- **What I implemented:** `GET /api/quotes/{id}` contains `await Task.Delay(1500)`, with the code comment
  *"Day 5: Intentionally slow endpoint for observability exercise"* still present in
  `Extensions/QuoteEndpointExtensions.cs` today. This is direct, unambiguous repository evidence for
  Day 5's content, distinct from Day 11's separate N+1 performance exercise in the standalone `Day11`
  console app.
- **Why I implemented it:** needed something worth tracing/observing — a fast endpoint gives OpenTelemetry
  nothing interesting to show.
- **Technologies used:** OpenTelemetry, likely paired with the Azure Monitor wiring dated to the same
  window (2026-08-14–15) — `Program.cs`'s conditional `UseAzureMonitor` setup.
- **Important files:** `Extensions/QuoteEndpointExtensions.cs` (the delay + comment), `Program.cs`
  (OpenTelemetry/Azure Monitor registration).
- **What I learned:** how to instrument and observe latency in a running API, and how to make Azure
  Monitor opt-in via a configuration check rather than a hard dependency.
- **Interview question I should be ready for:** "Why is this endpoint slow?" — be direct: it's
  intentional, for the observability exercise, not a real performance issue (contrast with Day 11, where
  a real N+1 was found and fixed).
- **Confidence:** high for the "what" (direct code comment); the technology pairing with OpenTelemetry
  is a reasonable but not code-confirmed inference.

### Day 6
- **Not enough repository evidence for a confident day-boundary reconstruction.** Commits dated
  2026-08-13–15 in this general window cover: a CI workflow with a coverage gate, and containerization
  plus Polly-based HTTP resilience (retry/circuit-breaker/timeout, later simplified — see Day 16).
  As with Day 4, this is a plausible window, not a labeled fact.
- **Likely technologies (if this is correct):** GitHub Actions, Docker/`ContainerImageName` in
  `QuotesApi.csproj`, `Microsoft.Extensions.Http.Resilience` (Polly).
- **Likely important files (if this is correct):** `.github/workflows/ci.yml`,
  `.github/workflows/integration-tests.yml`, `azure.yaml`, `infra/main.bicep`.

### Day 7 — SQL: Joins, CTEs, Window Functions, Set Operations
- **Goal:** demonstrate intermediate/advanced SQL querying against the quotes schema.
- **What I implemented:** `joins-and-ctes.sql` — two CTEs (`RankedQuotes` via
  `ROW_NUMBER() OVER (PARTITION BY Author ORDER BY Id DESC)`, `AuthorStats` via `GROUP BY`) joined
  to get each author's quote count and most recent quote. `window-functions.sql` — `ROW_NUMBER()`,
  a running `COUNT() OVER (... ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)`, and `LAG()` for
  previous-row lookups, all partitioned by author. `set-operations.sql` — `EXCEPT` to find authors with
  quotes but no collection (returned zero rows against the real data); `INTERSECT`/`UNION` cases are
  explicitly marked as not executable against the real schema rather than faked.
- **Why I implemented it:** to practice SQL patterns that come up constantly in real reporting/analytics
  queries, against real (if small) data rather than toy examples.
- **Technologies used:** T-SQL (SQL Server), CTEs, window functions, set operators.
- **Important files:** `Day7/joins-and-ctes.sql`, `Day7/window-functions.sql`, `Day7/set-operations.sql`.
- **What I learned:** how to reach for a window function instead of a self-join for running
  aggregates, and to be honest when a schema doesn't support an exercise (the `INTERSECT`/`UNION` note)
  rather than fabricating data to force it.
- **Interview question I should be ready for:** "When would you use `LAG()` vs a self-join?" —
  window functions avoid the row-multiplication and extra join cost of a self-join for
  previous-row/running comparisons.

### Day 8 — Indexing (SQL Server)
- **Goal:** measure the real effect of indexing strategies on query cost.
- **What I implemented:** `indexes.sql` builds a 100k-row `QuotePerformance` table and compares baseline
  (no index) vs. a clustered index on `Id` vs. a non-clustered index on `Author`/`Category`, using
  `SET STATISTICS IO`. `covering-index.sql` adds a covering index
  (`IX_QuotePerformance_Author_Covering` with `INCLUDE (QuoteText, Category)`) to eliminate a Key Lookup.
- **Why I implemented it:** indexing decisions are easy to get wrong by intuition; measuring actual IO
  cost makes the tradeoffs concrete.
- **Technologies used:** T-SQL, SQL Server execution plans, `SET STATISTICS IO`.
- **Important files:** `Day8/indexes.sql`, `Day8/covering-index.sql`.
- **What I learned:** the difference between a non-clustered index that still needs a Key Lookup and a
  covering index that eliminates it entirely.
- **Interview question I should be ready for:** "What's a covering index and when do you reach for one?"
  — an index whose leaf level contains every column a query needs, so the engine never touches the base
  table/clustered index at all.

### Day 9 — Deadlocks and Isolation Levels (SQL Server, two-session experiments)
- **Goal:** reproduce real concurrency anomalies, not just describe them.
- **What I implemented:** `deadlocks.sql` reproduces a classic circular-wait deadlock across two sessions
  (SQL Server `Msg 1205`) and fixes it via consistent lock ordering. `isolation-levels.sql` reproduces a
  dirty read (READ UNCOMMITTED), a non-repeatable read (READ COMMITTED), repeatable-read blocking, and a
  phantom read (appears under REPEATABLE READ, blocked under SERIALIZABLE via a lock timeout), ending
  with a summary table mapping each anomaly to the lowest isolation level that prevents it.
- **Why I implemented it:** these anomalies are much easier to reason about after actually triggering
  them than from a definitions table alone.
- **Technologies used:** T-SQL, SQL Server transaction isolation levels, two concurrent sessions.
- **Important files:** `Day9/deadlocks.sql`, `Day9/isolation-levels.sql`.
- **What I learned:** deadlocks are frequently a lock-ordering problem, fixable by making all
  transactions acquire locks in the same order; isolation level choice is a direct tradeoff between
  anomaly prevention and blocking/throughput.
- **Interview question I should be ready for:** "How do you actually fix a deadlock in production?" —
  consistent lock ordering, shorter transactions, and/or a lower isolation level where the anomaly it
  permits is acceptable for that specific query.

### Day 10 — EF Core Internals (two sub-topics, same day)
- **Goal:** understand EF Core's change tracking and query translation, not just its high-level API.
- **What I implemented:** `Day10/` proves EF Core's identity map returns the same tracked instance on a
  repeated query (`true`) while `AsNoTracking()` does not (`false`); a 10k-row benchmark showed tracked
  queries costing ~118ms/9.6MB allocated vs. ~34ms/3.7MB for no-tracking. `Day10-QueryTranslation/` shows
  the raw SQL EF Core generates for a `.Where(q => q.Author.Contains("a"))` (translates to `instr(...) > 0`
  on SQLite) versus a `.Select(q => new QuoteDto{...})` projection.
- **Why two folders:** they're two distinct sub-topics under one day's curriculum (change tracking cost,
  and query/projection translation), not accidental duplicates — confirmed by both submission docs
  describing different EF Core internals.
- **Technologies used:** EF Core change tracking, `AsNoTracking()`, LINQ-to-SQL translation, SQLite.
- **Important files:** `Day10/Program.cs`, `Day10/day10-submission.md`,
  `Day10-QueryTranslation/Program.cs`, `Day10-QueryTranslation/day10-query-translation.md`.
- **What I learned:** `AsNoTracking()` isn't just a performance micro-optimization — it measurably
  changes both time and allocations for read-heavy paths (which is why the main API's repositories use
  it for all reads).
- **Interview question I should be ready for:** "When would you *not* want `AsNoTracking()`?" — when you
  need EF's change tracker to detect and persist mutations on the entities you just loaded.

### Day 11 — Profiling and Fixing a Slow Endpoint
- **Goal:** find and fix a real N+1 query problem using load-test evidence, not guesswork.
- **What I implemented:** a baseline `/slow` endpoint (standalone `Day11` console app) that issued one
  query for distinct authors plus one query per author (101 total queries for the dataset used). A k6
  load test against the baseline recorded 593 requests, p50 97.5ms, p99 709.8ms. The fix — a single
  indexed query (`IX_Quotes_Author`) instead of the N+1 pattern, plus a `/plan` endpoint exposing
  `EXPLAIN QUERY PLAN` — dropped p99 to 38.76ms (an 18.3× improvement, exceeding the stated 10× target)
  and query count from 101 to 2.
- **Why I implemented it:** wanted a before/after measurement with real load-test numbers, not just a
  code change asserted to be "faster."
- **Technologies used:** SQLite, k6 (load testing), `EXPLAIN QUERY PLAN`.
- **Important files:** `Day11/Program.cs`, `Day11/load-test.js`, `Day11/day11-submission.md`,
  `Day11/day11-fix-submission.md`.
- **What I learned:** how to identify an N+1 pattern from query counts and load-test percentiles rather
  than intuition, and that a covering index plus a single query eliminates it entirely.
- **Interview question I should be ready for:** "How do you detect an N+1 in the first place?" — query
  count relative to result-set size, and p99 latency that scales with data volume rather than staying
  flat.
- **Verified this session:** re-confirmed by re-running the load test and re-reading the fixed code
  before it was committed — the fix is real and working, not just described in the submission doc.

### Day 12 — CQRS-lite, MediatR, and Dapper vs. EF Core
- **Goal:** compare a raw-SQL data-access path (Dapper) against EF Core for reads, and try a
  lightweight CQRS write path.
- **What I implemented:** `day12-submission.md` documents a MediatR `CreateQuoteCommand`/
  `CreateQuoteCommandHandler` write path with validation and `Trim()` normalization over a raw
  `SqliteConnection` insert. `day12-dapper-submission.md` benchmarks EF Core (`AsNoTracking()` + a
  `Select` projection) against Dapper for the same read, using a stopwatch. The current
  `Day12/Program.cs` runs Dapper, EF Core Sqlite, and MediatR side by side for direct comparison.
- **Why I implemented it:** Dapper vs. EF Core is a common real-world tradeoff decision (raw SQL control
  and speed vs. ORM convenience and change tracking) worth measuring rather than asserting from opinion.
- **Technologies used:** Dapper, MediatR, EF Core (Sqlite provider).
- **Important files:** `Day12/Program.cs`, `Day12/Day12.csproj`, `Day12/day12-submission.md`,
  `Day12/day12-dapper-submission.md`. (`Day12/Program-cqrs-backup.txt` is an earlier draft kept locally,
  not part of the final submission — intentionally left untracked in git.)
- **What I learned:** when the ORM overhead (change tracking, materialization) actually shows up in a
  benchmark versus when it's negligible for the query shape in question.
- **Interview question I should be ready for:** "When would you reach for Dapper over EF Core?" — reads
  where you want full control over the exact SQL and minimal materialization overhead, at the cost of
  losing change tracking and migrations for that path.

### Day 13 — Angular Signals, First Frontend Pass
- **Goal:** build the first Angular UI against the real API and verify assumptions against source code.
- **What I implemented:** a standalone Angular app with signals, a quote list and detail view.
- **Why I implemented it:** first frontend integration point for the API built in Days 1–12.
- **Technologies used:** Angular (standalone components, signals), `HttpClient`.
- **Important files:** `Day13/src/app/quote-list/`, `Day13/src/app/services/quote.service.ts`,
  `Day13/day13-submission.md`.
- **What I learned/notable process detail:** `day13-submission.md` documents that an AI coding assistant
  initially assumed the API returned `{ items: Quote[] }`, and this was caught by checking
  `IQuoteRepository.GetPagedAsync`'s actual return type (`Task<IReadOnlyList<Quote>>`) and correcting the
  frontend model to a bare `Quote[]`. Also notes the API was verified against `localhost:5050` at the
  time — the standalone Day 10/11 console apps' default port, not the main API's `5145`.
- **Interview question I should be ready for:** "How do you catch an AI assistant's wrong assumption
  about an API contract?" — this is a genuine, documented example: check the actual repository method
  signature, don't trust an assumed response shape.

### Day 14 — Reactive Forms vs. Signal Forms
- **Goal:** build a create-quote form, then rebuild it with Angular's newer Signal Forms API for
  comparison.
- **What I implemented:** `Day14App` — classic Reactive Forms (`FormBuilder`,
  `Validators.required/minLength/maxLength`) posting to `POST /api/quotes/`; documents a real caught
  bug ("the first implementation only displayed a placeholder message... it did not actually call the
  real API"), fixed by wiring `QuoteService.createQuote()`. Also documents accessibility verification:
  labels, `aria-invalid`, `aria-describedby`, `role="alert"`/`role="status"`, and focus-to-first-invalid-
  field on submit. `Day14-SignalForms/SignalFormsApp` rebuilds the same author/text form using Angular's
  experimental Signal Forms API (`@angular/forms/signals` — `form()`, `FormField`, `required()`,
  `minLength()`, `maxLength()` as functions over a path) instead of `ReactiveFormsModule`, with the same
  validation rules and focus-management behavior.
- **Why I implemented it:** a genuine side-by-side comparison of Angular's established Reactive Forms API
  against its newer signals-based forms primitive, on the same real form.
- **Technologies used:** Angular Reactive Forms, Angular Signal Forms (experimental), ARIA attributes.
- **Important files:** `Day14/Day14App/day14-submission.md`,
  `Day14/Day14-SignalForms/SignalFormsApp/src/app/create-quote.ts` (or equivalent form component).
- **What I learned:** the ergonomic and mental-model differences between value-based reactive forms and
  signal-based forms, and how to verify a form is actually calling the real API rather than trusting
  that it "looks done" in the UI.
- **Interview question I should be ready for:** "How did you verify accessibility on this form?" — be
  ready to name the specific attributes used (`aria-invalid`, `aria-describedby`, `role="alert"`) and the
  focus-management behavior on submit.

### Day 15 — Angular Routing, Guards, Interceptors
- **Goal:** build a properly routed multi-page Angular app with route protection and a real HTTP
  interceptor pipeline.
- **What I implemented:** `app.routes.ts` (`/`, `/login`, `/quotes`, `/quotes/:id`, wildcard), lazy
  loading via `loadComponent`, `provideRouter` with `withViewTransitions`, an `authGuard` on the detail
  route, and three chained interceptors — `authInterceptor` (attaches the bearer token),
  `retryInterceptor` (retries GET requests on transient failure with exponential backoff, never on 4xx),
  `errorInterceptor` (normalizes `ProblemDetails`-shaped error bodies).
- **Why I implemented it:** wanted a realistic multi-page app shape — lazy-loaded routes, a route guard,
  and a real interceptor chain — rather than a single component calling `HttpClient` directly.
- **Technologies used:** Angular Router (standalone, signals-based), `HttpClient` with
  `withInterceptors`, `CanActivateFn` guards.
- **Important files:** `Day15/Day15App/src/app/app.routes.ts`, `app.config.ts`, `guards/auth.guard.ts`,
  `interceptors/*.ts`.
- **What I learned:** how to compose an interceptor chain where order matters (auth attaches the token
  before retry/error see the request), and that a route guard checking `localStorage` is a UX gate, not
  a security boundary (see `docs/authentication.md`).
- **Interview question I should be ready for:** "What does `withViewTransitions()` actually do?" — uses
  the browser's View Transitions API to animate between routed views, when supported.

### Day 16 — Making It Runnable, Workable, and Interview-Ready
- **Goal:** take the accumulated Day 1–15 work from "each piece works in isolation" to "the whole stack
  runs end-to-end and is presentable."
- **What I implemented, all verified this session, not just written and assumed correct:**
  1. **CORS** — the backend had no CORS policy at all; the Angular dev server couldn't call it. Added a
     Development-only policy scoped to `http://localhost:4200`.
  2. **JWT claim-mapping fix** — found via live testing (see `docs/authentication.md`): `POST /api/quotes`
     returned `401` for a valid token because `MapInboundClaims` wasn't set to `false` on the production
     JWT scheme, even though the integration test factory had it right all along.
  3. **Zoneless change-detection fix** (see `docs/architecture.md`) — the quotes list and quote-detail
     pages never left their "Loading…" state because their component state was plain fields, not
     signals, under this Angular version's zoneless-by-default change detection. Converted to `signal()`.
  4. **Real login wiring** — replaced a hardcoded demo-token button with an actual form posting to
     `POST /api/auth/login`, backed by a dedicated seeded local demo user.
  5. **Visual/accessibility pass** — a design system (`styles.css`), a header with a skip-to-content
     link and login/logout state, `aria-live` regions for loading/error states, descriptive link text,
     and per-route document titles — none of which existed before (the app had an unstyled Angular CLI
     scaffold and zero custom accessibility work).
  6. **This documentation set** — `README.md` and everything under `docs/`, written from a structured
     three-part repository audit rather than from memory or assumption.
- **Why I implemented it:** all of Days 1–15's work was real and individually correct, but nothing had
  been verified running together end-to-end in a browser against the real backend until this session —
  and an interviewer will run it, not read the code in isolation.
- **Technologies used:** ASP.NET Core CORS middleware, JWT claim mapping, Angular signals, CSS custom
  properties, WAI-ARIA.
- **Important files:** `Program.cs` (CORS + JWT fix), `Day15/Day15App/src/app/pages/*` (signals fix,
  real login), `Day15/Day15App/src/styles.css` (design system).
- **What I learned:** that "each Day's code builds and its own tests pass" is not the same claim as
  "the whole system works end-to-end" — both real bugs found this session (JWT claims, zoneless CD) only
  surfaced when the full stack was actually exercised live, not from reading the code.
- **Interview question I should be ready for:** "Walk me through a bug you found by testing the whole
  system rather than a unit." — use either the JWT claim-mapping bug or the zoneless change-detection
  bug; both have a clear symptom → investigation → root cause → fix → verification arc.
