# API Reference

Base URL (local dev): `http://localhost:5145`. All endpoints below are read directly from
`Extensions/AuthEndpointExtensions.cs` and `Extensions/QuoteEndpointExtensions.cs` — nothing here is
inferred or planned; every route listed is live in the current codebase.

## Auth endpoints (`/api/auth`)

### `POST /api/auth/login`
- **Auth required:** none.
- **Request body:** `{ "email": string, "password": string }`
- **Success (200):** `{ "access_token": string, "refresh_token": string, "expires_in": 900 }`
- **Failure (401):** invalid email or password (no body).
- **DB operations:** `SELECT` on `Users` by email; `BCrypt.Verify` against `PasswordHash`; `INSERT` into
  `RefreshTokens`.

### `POST /api/auth/refresh`
- **Auth required:** none (the refresh token itself is the credential).
- **Request body:** `{ "refreshToken": string }`
- **Success (200):** same shape as login — new `access_token`, new `refresh_token`, `expires_in: 900`.
- **Failure (401):** token not found, already revoked (triggers reuse detection — revokes the whole
  token family), or expired.
- **DB operations:** `SELECT` on `RefreshTokens` (with `Include(User)`) by hashed token; on success,
  marks the old token revoked, inserts a new one, `SaveChanges`.

### `POST /api/auth/logout`
- **Auth required:** none.
- **Request body:** `{ "refreshToken": string }`
- **Success (204):** always, whether or not the token existed (idempotent).
- **DB operations:** `SELECT` + conditional `UPDATE` (`RevokedAt`) on `RefreshTokens`.

## Quote endpoints (`/api/quotes`)

### `GET /api/quotes?page={page}&size={size}`
- **Auth required:** none — public.
- **Query params:** `page` (defaults to 1 if `<1`), `size` (defaults to 10 if `<1` or `>100`).
- **Success (200):** `Quote[]` — `{ id, userId, author, text, isDeleted }[]`.
- **DB operation:** `SELECT` from `Quotes` where `!IsDeleted`, ordered by `Id`, paged via `Skip`/`Take`,
  `AsNoTracking()`.

### `GET /api/quotes/{id}`
- **Auth required:** none — public.
- **Success (200):** a single `Quote` object, same shape as above.
- **Failure (404):** quote not found or soft-deleted.
- **Deliberate behavior:** this handler contains `await Task.Delay(1500)` with the code comment
  *"Day 5: Intentionally slow endpoint for observability exercise"* — this is a real, intentional
  artifact of the Day 5 curriculum work (see `docs/day-by-day.md`), not a performance bug. It exists to
  give something worth observing/tracing.
- **DB operation:** `SELECT` by `Id` where `!IsDeleted`, `AsNoTracking()`.

### `POST /api/quotes/`
- **Auth required:** yes — policy `"can-edit-quotes"` (claim `scope = "quotes.write"`).
- **Request body:** `{ "author": string (1-200 chars), "text": string (1-1000 chars) }`
- **User ID:** taken from the caller's JWT `sub` claim, **not** from the request body.
- **Success (201):** the created `Quote`, `Location: /api/quotes/{id}`.
- **Failure (400):** DataAnnotations validation failure on the DTO, or a domain invariant violation
  from `Quote.Create` (e.g. author/text length) — both return RFC 7807 `ValidationProblemDetails`.
- **Failure (401):** missing/invalid token, or a token whose `sub` claim can't be parsed as an int.
- **DB operation:** `INSERT` into `Quotes`.

### `DELETE /api/quotes/{id}`
- **Auth required:** yes — any authenticated caller (`.RequireAuthorization()`, no specific policy).
  See `docs/authentication.md` for the known gap: this does **not** check that the caller owns the
  quote, despite tested logic existing for exactly that check.
- **Success (204):** no body. This is a **soft delete** — `IsDeleted` is set true; the row is not removed.
- **Failure (404):** quote not found (or already soft-deleted).
- **DB operation:** `SELECT` by `Id` (tracked), `Quote.SoftDelete()`, `SaveChanges`.

## Collection endpoints (`/api/collections`)

### `POST /api/collections/`
- **Auth required:** none configured on this route (no `.RequireAuthorization()` call).
- **Request body:** `{ "name": string (3-80 chars), "ownerId": int }`
- **Success (201):** the created `Collection`, `Location: /api/collections/{id}`.
- **Failure (400):** DataAnnotations validation failure → `ValidationProblemDetails`.
- **DB operation:** `INSERT` into `Collections`.

### `POST /api/collections/{id}/items?quoteId={quoteId}`
- **Auth required:** none configured.
- **Success (204):** no body.
- **Failure (404):** collection or quote not found.
- **Domain rule enforced:** `Collection.AddItem` throws (→ unhandled exception → 500 via
  `ExceptionHandlingMiddleware`, since this isn't caught as a `DomainInvariantException` at the endpoint
  level the way quote creation is) if the collection already has 50 items or already contains this
  quote. Worth noting as an inconsistency with `POST /api/quotes`, which does convert its domain
  exception into a clean `400`.
- **DB operation:** `SELECT` collection (with `Include(Items)`) and quote by id; `AddItem`; `SaveChanges`.

### `DELETE /api/collections/{id}/items/{quoteId}`
- **Auth required:** none configured.
- **Success (204):** no body.
- **Failure (404):** collection not found.
- **DB operation:** `SELECT` collection (with `Include(Items)`) by id; `RemoveItem`; `SaveChanges`.

## Error format

All errors (validation and unhandled) are returned as RFC 7807 `application/problem+json` via
`AddProblemDetails()` and `ExceptionHandlingMiddleware`. Validation failures use
`Results.ValidationProblem(errors)` (a `Dictionary<string, string[]>` under `errors`); unexpected
exceptions produce a generic `ProblemDetails` with title `"An unexpected error occurred."` and are
logged at `Error` level with the full exception via Serilog.

## CORS

Enabled only when `app.Environment.IsDevelopment()` is true, scoped to `http://localhost:4200`
(`AllowAnyHeader`, `AllowAnyMethod`). Added specifically to let the local Angular dev server call this
API — there is no CORS policy for any other origin.
