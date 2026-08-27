# Architecture

This document describes the layers that actually exist in this repository and how a request
moves through them. Nothing here is aspirational — every box below corresponds to real code.

## Request flow

```
Angular (Day15App)
  │
  ├── Router (provideRouter + withViewTransitions, lazy-loaded routes)
  ├── Guards (authGuard — checks for a token in localStorage before /quotes/:id)
  ├── HttpClient
  └── Interceptors (auth → retry → error, applied in that order)
          │
          ▼  HTTP over localhost:4200 → localhost:5145 (CORS allowed in Development)
ASP.NET Core (QuotesApi, Minimal APIs, .NET 10)
  │
  ├── Serilog request logging + correlation ID middleware
  ├── ExceptionHandlingMiddleware (DomainInvariantException → 400, else → 500, both as ProblemDetails)
  ├── CORS (Development only, http://localhost:4200)
  ├── Authentication ("Smart" policy scheme → InternalJwt or EntraJwt, chosen by inspecting the token's issuer)
  ├── Authorization (policy: "can-edit-quotes"; a registered-but-unused ownership handler — see docs/authentication.md)
  ├── Minimal API endpoint groups (/api/auth, /api/quotes, /api/collections)
  ├── Services (TokenService, RefreshTokenService)
  ├── Repositories (IQuoteRepository, ICollectionRepository)
  └── Domain model (Quote, Collection — rich entities with invariants; User, RefreshToken — anemic)
          │
          ▼
Entity Framework Core 10 (Sqlite provider)
          │
          ▼
SQLite (quotes.db, local file, gitignored)
```

## Why this shape

- **Minimal APIs, not Controllers.** The whole HTTP surface is defined in `Extensions/*EndpointExtensions.cs`
  as `MapGet`/`MapPost`/`MapDelete` groups over `IEndpointRouteBuilder`. There is no MVC controller anywhere
  in this codebase — this is a deliberate, consistent choice, not an oversight.
- **Repository pattern over EF Core.** Endpoints depend on `IQuoteRepository`/`ICollectionRepository`
  interfaces, not `DbContext` directly. This is what makes `Quotes.Tests.Integration` and the unit test
  projects able to substitute fakes/real containers without touching endpoint code.
- **Rich domain model for `Quote` and `Collection`.** Both are constructed through static/instance factory
  methods that enforce invariants (see `WHY.md` at the repo root and `docs/database.md`). `User` and
  `RefreshToken` are plain anemic models — validation for those happens at the DTO/endpoint layer instead.
  This is an intentional asymmetry, not an inconsistency: `Quote`/`Collection` are the domain's core
  aggregates; `User`/`RefreshToken` are closer to infrastructure records.
- **Dual JWT schemes via a policy scheme.** Program.cs registers two `AddJwtBearer` schemes
  (`InternalJwt`, `EntraJwt`) behind a third `AddPolicyScheme("Smart", ...)` that inspects the incoming
  bearer token's `iss` claim and forwards to whichever scheme actually applies. See
  `docs/authentication.md` for the full flow.
- **Observability is opt-in, not hardcoded.** `Program.cs` only wires Azure Monitor when
  `APPLICATIONINSIGHTS_CONNECTION_STRING` is configured; otherwise OpenTelemetry runs without an exporter
  attached. This means the app runs identically with or without Azure configured — a deliberate
  local-dev-friendly choice.

## Angular app structure (Day15App)

```
src/app/
├── app.ts / app.html          — shell: skip link, header, nav (login/logout state), <router-outlet>
├── app.routes.ts               — /, /login, /quotes, /quotes/:id (guarded), wildcard → /quotes
├── app.config.ts               — provideRouter, provideHttpClient(withInterceptors([...]))
├── guards/auth.guard.ts        — CanActivateFn, checks for an access_token in localStorage
├── interceptors/
│   ├── auth.interceptor.ts     — attaches Authorization: Bearer <token> if one exists
│   ├── retry.interceptor.ts    — retries GET requests up to twice on 5xx/network errors (exponential backoff)
│   └── error.interceptor.ts    — normalizes ProblemDetails-shaped error bodies into a typed ApiError
├── services/
│   ├── quote.service.ts        — getQuotes(page,size), getQuoteById(id)
│   └── auth.service.ts         — signal-based auth state (isAuthenticated), setSession()/logout()
├── models/
│   ├── quote.ts                — { id, userId, author, text, isDeleted } — matches the real API shape
│   └── problem-details.ts      — mirrors ASP.NET Core's ProblemDetails contract
└── pages/
    ├── login/login.component.ts        — real form, POST /api/auth/login
    ├── quotes/quotes.component.ts      — list view, signal-based state
    └── quote-detail/quote-detail.component.ts — detail view, signal-based state
```

**Zoneless change detection.** This Angular 21 app has no `zone.js` dependency (confirmed absent from
`package.json` and `angular.json`'s build options across Day13, Day14, and Day15 — this is the current
Angular CLI default, not a project-specific choice). Under zoneless change detection, only signal writes,
template-bound events, and the async pipe trigger a re-render — a plain class field mutated inside an
RxJS `.subscribe()` callback does not. All page-level state (`loading`, `error`, `quotes`, `quote`) is
therefore implemented with `signal()`, not plain fields. This was the root cause of a real bug found and
fixed during this project (see `docs/day-by-day.md`, Day 16) where the quotes list stayed stuck on
"Loading…" even though the HTTP call succeeded.

## What does not exist (so this document doesn't overstate the project)

- No API Gateway, message queue, or microservice boundary — this is a single ASP.NET Core process.
- No Redis/distributed cache.
- No production SQL Server — SQLite is used everywhere except the integration test suite, which
  deliberately runs against a real SQL Server container via Testcontainers (see `docs/testing.md`).
- No server-side rendering — the Angular app is a client-rendered SPA.
- Middleware/Abstractions/Infrastructure exists in the tree but is an empty folder with no files —
  noted here rather than silently ignored, since Phase 1's instruction was not to assume folder names
  mean anything.
