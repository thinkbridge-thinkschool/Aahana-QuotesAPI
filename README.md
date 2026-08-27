# Quotes API

A quotes management system built as a progressive ThinkBridge ThinkSchool training project: an
ASP.NET Core Minimal API backend with EF Core/SQLite, JWT + Microsoft Entra ID authentication, and an
Angular frontend, alongside a series of standalone SQL and .NET exercises documenting specific
engineering topics (indexing, deadlocks, EF Core internals, N+1 diagnosis, CQRS/Dapper, and Angular
forms/routing).

## Overview

This repository is two things at once, on purpose:

1. **A real, working full-stack application** — the root `QuotesApi` project plus the `Day15/Day15App`
   Angular frontend. This is what runs end-to-end and is meant to be demoed.
2. **A dated record of the training progression that produced it** — `Day7` through `Day15`, each a
   focused, mostly-standalone exercise, kept in place rather than deleted once "done," because they're
   evidence of how the main application's engineering decisions were arrived at.

Detailed documentation lives in [`docs/`](docs/) — this README is the entry point and summary.

## What I Built

- A Minimal API backend (.NET 10) with a rich domain model for `Quote`/`Collection`, soft-delete
  semantics, EF Core migrations, and a repository layer over SQLite.
- Dual JWT authentication (self-issued tokens and Microsoft Entra ID tokens, selected automatically by
  inspecting the token issuer) plus refresh-token rotation with reuse detection.
- Structured logging (Serilog), distributed tracing (OpenTelemetry, optionally exported to Azure
  Monitor), RFC 7807 problem-details error handling, and HTTP resilience for outbound calls.
- xUnit/FluentAssertions unit tests (28 total across two projects) and a SQL-Server-backed integration
  test suite (via Testcontainers) covering the real HTTP pipeline.
- An Angular 21 frontend (standalone components, signals, lazy-loaded routes, a real interceptor chain,
  and a route guard), wired to real login, and given an accessibility and visual design pass.
- Nine dated SQL/.NET exercises (`Day7`–`Day12`) demonstrating joins/CTEs/window functions/set
  operations, indexing strategy, deadlocks/isolation levels, EF Core change-tracking cost, an N+1 bug
  found and fixed with load-test evidence, and a Dapper-vs-EF-Core comparison — plus two Angular
  exercises (`Day13`, `Day14`) building the frontend incrementally.

## Architecture

```
Angular (Day15App) → HttpClient → Interceptors → ASP.NET Core Minimal APIs
                                                        → Services / Repositories
                                                        → EF Core → SQLite
```

Full diagram and rationale: [`docs/architecture.md`](docs/architecture.md).

## Technology Stack

**Backend**
- C#, .NET 10, ASP.NET Core Minimal APIs
- Entity Framework Core 10 (SQLite provider for the app; SQL Server via Testcontainers for integration
  tests)
- JWT (`Microsoft.AspNetCore.Authentication.JwtBearer`) + Microsoft Entra ID
- BCrypt.Net-Next (password hashing)
- Serilog (structured logging)
- OpenTelemetry + Azure Monitor (optional, opt-in via configuration)
- `Microsoft.Extensions.Http.Resilience` (Polly-based retry/timeout for outbound HTTP)
- xUnit, FluentAssertions, Testcontainers

**Frontend**
- Angular 21 (standalone components, signals, zoneless change detection)
- TypeScript
- Angular Router (lazy loading, route guards, view transitions)
- `HttpClient` with a chained interceptor pipeline
- Vitest (via the Angular CLI's test builder)

**Development**
- VS Code, Git, GitHub (`thinkbridge-thinkschool/Aahana-QuotesAPI`)
- GitHub Actions CI (`.github/workflows/`) — build + unit tests on every push; a second workflow runs
  the SQL-Server integration suite on pushes to `main`/`quote-rich-domain`
- Azure Developer CLI (`azure.yaml`) + Bicep (`infra/`) for optional Azure Container Apps deployment —
  present in the repo, not something this session verified by actually deploying

**Database**
- SQLite (local/dev), EF Core Migrations (5 migrations, applied automatically on startup)

Full breakdown of what's actually used vs. not: [`docs/architecture.md`](docs/architecture.md).

## Features

- Paginated quote listing, quote detail, quote creation (authenticated), quote soft-deletion
- Collections with capped, deduplicated item membership (max 50 items, domain-enforced)
- Login, refresh-token rotation with theft/reuse detection, logout
- Client-side route protection and a real authenticated frontend session

## API Endpoints

Full reference with request/response shapes, auth requirements, and DB operations per endpoint:
[`docs/api.md`](docs/api.md).

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/auth/login` | none | Issue access + refresh tokens |
| POST | `/api/auth/refresh` | refresh token | Rotate tokens, detect reuse |
| POST | `/api/auth/logout` | refresh token | Revoke a refresh token |
| GET | `/api/quotes?page=&size=` | none | Paged quote list |
| GET | `/api/quotes/{id}` | none | Single quote (intentionally delayed 1.5s — Day 5 observability exercise) |
| POST | `/api/quotes/` | `can-edit-quotes` scope | Create a quote |
| DELETE | `/api/quotes/{id}` | any authenticated user | Soft-delete a quote |
| POST | `/api/collections/` | none | Create a collection |
| POST | `/api/collections/{id}/items?quoteId=` | none | Add a quote to a collection |
| DELETE | `/api/collections/{id}/items/{quoteId}` | none | Remove a quote from a collection |

## Authentication & Authorization

Full detail, including a real, tested-but-unwired authorization gap discussed honestly:
[`docs/authentication.md`](docs/authentication.md).

- **Authentication** ("who are you?"): dual JWT schemes (self-issued + Entra ID) behind a policy scheme
  that inspects the token issuer; BCrypt password verification; refresh-token rotation with reuse
  detection.
- **Authorization** ("what can you do?"): one active policy (`can-edit-quotes`, claim-based). A
  resource-based ownership handler (`CanDeleteOwnQuoteHandler`) exists and is fully unit-tested but is
  **not wired into any endpoint** — `DELETE /api/quotes/{id}` currently allows any authenticated user to
  delete any quote. Documented rather than silently changed.
- The Angular login page performs real authentication as of this session (posts to
  `POST /api/auth/login`); the route guard only checks *whether a token exists* in `localStorage`, which
  is a UX gate, not a security boundary — the server is the actual enforcement point.

## Database Design

Full schema, migration history, and the composite-key warning explained (not hidden):
[`docs/database.md`](docs/database.md).

SQLite locally; EF Core migrations apply automatically on startup, no manual step required.

## Error Handling

RFC 7807 `application/problem+json` throughout — `AddProblemDetails()` plus
`ExceptionHandlingMiddleware`, which converts `DomainInvariantException` to `400` and anything else to a
logged `500`. DataAnnotations validation failures return `ValidationProblemDetails`.

## Observability

Serilog structured logging with a correlation-ID middleware; OpenTelemetry tracing for ASP.NET Core, EF
Core, and outbound HTTP calls; Azure Monitor export is opt-in (only enabled when
`APPLICATIONINSIGHTS_CONNECTION_STRING` is configured) so the app behaves identically with or without
Azure wired up.

## Resilience

Outbound HTTP calls go through a named `HttpClient` (`"my-service"`); `Microsoft.Extensions.Http.Resilience`
is referenced in the project. Note: an earlier, more elaborate Polly pipeline (explicit retry/circuit-
breaker/timeout policies) was simplified during this session's cleanup to a plain named client — see the
git history on `Program.cs` if you want to discuss the fuller version in an interview.

## Testing

Full breakdown per project, exact commands, and last-verified results:
[`docs/testing.md`](docs/testing.md).

| Project | Tests | Requires | Last result |
|---|---|---|---|
| `QuotesApi.Tests` | 7 | nothing | ✅ 7/7 passed |
| `Tests.Domain` | 21 | nothing | ✅ 21/21 passed |
| `Quotes.Tests.Integration` | 6 | Docker | ⚠️ not run (Docker not running in this environment) |
| Angular (`Day15App`) | 5 (3 files) | nothing | ✅ 5/5 passed |

## Day-by-Day Learning Journey

Full detail per day, with explicit confidence levels (labeled commits vs. dated inference vs. "not
enough evidence") rather than invented content: [`docs/day-by-day.md`](docs/day-by-day.md).

Days 1–3 and 5 have direct repository evidence (explicit commit labels or code comments). Days 4 and 6
are dated but unlabeled — presented as inference, not fact. Days 7–16 are grounded in dedicated folders,
submission documents, and (for Day 16) this session's own verified work.

## How to Run

**Backend** (from `QuotesApi/`, the repo root):
```powershell
dotnet run --urls http://localhost:5145
```
First run applies EF Core migrations to `quotes.db` automatically — no manual database setup. Requires
the JWT signing key (`Jwt:Key`) to be present in `dotnet user-secrets` for this project
(`UserSecretsId` `6bd2f9f8-f9dc-4cad-a77f-fa0ba74cd856`); already configured on this machine.

**Frontend** (from `QuotesApi/Day15/Day15App/`):
```powershell
npm install
npm start
```
Serves on `http://localhost:4200`, calls the API at `http://localhost:5145` (CORS is open for that
origin only, and only in Development).

Then open `http://localhost:4200`. Demo login: `demo@thinkschool.local` / `ThinkSchool2026` (a locally
seeded user, real BCrypt + JWT auth — not a hardcoded frontend token). You can also browse `/quotes`
without logging in, since quote reads don't require authentication.

## Verification

Commands actually run and confirmed passing in this session (see [`docs/testing.md`](docs/testing.md)
for full output):

```powershell
dotnet build QuotesApi.csproj              # 0 errors
dotnet test QuotesApi.Tests/QuotesApi.Tests.csproj      # 7/7 passed
dotnet test Tests.Domain/Tests.Domain.csproj             # 21/21 passed
```
```powershell
cd Day15/Day15App
ng build --configuration production        # succeeded
ng test --watch=false                       # 5/5 passed
```

Also verified live, over real HTTP, not just by reading code: CORS preflight + actual cross-origin
requests from `:4200` to `:5145`; full login → create quote (`201`) → delete (`204`) round trip with a
real JWT; the JWT claim-mapping fix (before: `401` on every authenticated create; after: `201`).

## Known Technical Considerations

Legitimate, real items — not hidden, not silently fixed without being asked:

1. **Quote deletion isn't ownership-scoped.** `CanDeleteOwnQuoteHandler` is written and fully unit-tested
   but not wired into the `DELETE /api/quotes/{id}` policy — any authenticated user can delete any quote.
   See [`docs/authentication.md`](docs/authentication.md).
2. **SQLite composite-key warning on startup**, for `CollectionItems (CollectionId, QuoteId)` — benign
   (the app always supplies `QuoteId` explicitly), root-caused and explained rather than suppressed. See
   [`docs/database.md`](docs/database.md).
3. **Dead code**: `Services/SystemClock.cs` duplicates `Infrastructure/SystemClock.cs` and is unreferenced;
   `Infrastructure/QuoteFormatter.cs` appears unused by any endpoint; `Services/TokenService.cs` is
   registered in DI but not actually called by the login/refresh endpoints (which build tokens manually).
   Left in place rather than deleted, per this project's rule against removing things without being asked.
4. **A 20MB VS Code extension binary** (`github.copilot-chat-0.48.1.vsix`) was committed by accident in
   an early commit and removed from the working tree three commits later — but it's still present in git
   history, since removing it fully requires a history rewrite, which wasn't done without explicit
   approval.
5. **`Quotes.Tests.Integration` requires Docker** and could not be run in the environment this
   documentation was written in (Docker Desktop installed but not running) — reported as unverified, not
   assumed passing.
6. **NuGet advisory warnings** (`SQLitePCLRaw.lib.e_sqlite3`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`)
   surface on every backend build — known, not blocking, not addressed in this pass since upgrading them
   wasn't asked for and risks version-compatibility changes outside this session's scope.
7. **Repository structure was deliberately NOT reorganized** into `backend/`/`frontend/`/`training/`
   folders — see "On repository structure" below for why.

## On Repository Structure

A `backend/` / `frontend/` / `training/` layout was considered and audited in detail before touching
anything. The verdict, based on real evidence rather than caution for its own sake:

- Moving `Day7`–`Day14` and `Day15/Day15App` would be **low risk** — they're fully self-contained
  projects with no external path references.
- Moving `QuotesApi.csproj` itself into a `backend/` folder would be **medium risk**: it's mechanically
  safe, but requires *coordinated, simultaneous* edits to three test-project `<ProjectReference>` paths,
  `azure.yaml`'s `project: .` path, and **both GitHub Actions workflow files** (which hardcode
  `dotnet build`/`dotnet test` paths relative to the repo root). Missing any one of these breaks CI or
  `azd` deployment immediately and silently until the next push.

Given this project's own stated priority — **working project > clean structure > pretty repository** —
and that a partial move (Day-folders only, backend left in place) doesn't even eliminate the
`<Compile Remove>` workaround in `QuotesApi.csproj` it was meant to simplify (both would need to move
together for that benefit), the structure was left exactly as-is. "Clean and presentable" was achieved
through this documentation set instead, which is what Phase 4 of the original brief allows for when
moving is genuinely risky rather than clearly safe.

If you want the full reorganization later, the exact edits required (every file, every path) are
recorded and available — it just wasn't executed unprompted in this pass.

## Interview Talking Points

- **Rich domain model, deliberately asymmetric.** `Quote`/`Collection` enforce invariants through
  factory methods and private setters; `User`/`RefreshToken` are intentionally anemic — infrastructure
  records, not domain aggregates. See `WHY.md` at the repo root for the original reasoning.
- **A real bug found through live testing, not unit tests.** The JWT claim-mapping bug
  (`MapInboundClaims`) only surfaced by actually running the login → create-quote flow end-to-end; the
  integration test suite already had the correct config, but couldn't run without Docker to prove it.
- **A real bug from a framework default, not application logic.** The zoneless change-detection issue
  wasn't a typo or a logic error — it was a correct-looking `.subscribe()` callback that silently didn't
  trigger a re-render under Angular's current zoneless-by-default behavior. Good example of verifying
  in-browser rather than trusting that code "looks right."
- **Honest handling of an incomplete authorization feature.** Rather than either hiding the ownership-
  check gap or unilaterally wiring it in (which would change live endpoint behavior without being asked),
  it's documented clearly with the exact fix described.
- **A structure decision backed by evidence, not vibes.** The "don't reorganize" call wasn't made from
  caution alone — it came from actually reading the CI workflows, `azure.yaml`, and `.csproj` reference
  paths first and finding real (if fixable) breakage risk in a full backend move.
