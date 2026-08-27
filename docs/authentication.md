# Authentication & Authorization

**Authentication** answers "who are you?" — proving identity, typically via a signed token.
**Authorization** answers "what are you allowed to do?" — a decision made *after* identity is known,
based on claims/policies. This project implements both, but not uniformly everywhere — see the
"Known gap" section below for a real, honest example of where the two diverge.

## Backend: two authentication schemes behind a policy scheme

`Program.cs` registers **three** authentication schemes:

1. **`InternalJwt`** — validates tokens issued by this API itself (`AuthEndpointExtensions`).
   Signing key comes from configuration (`Jwt:Key`, stored in `dotnet user-secrets`, never committed).
   Validates issuer (`Jwt:Issuer`), audience (`Jwt:Audience`), lifetime, and the signature
   (`SymmetricSecurityKey` + `HmacSha256`). `ClockSkew = TimeSpan.Zero` — no grace period on expiry.
2. **`EntraJwt`** — validates tokens issued by Microsoft Entra ID, configured against a real tenant
   (`Entra:TenantId`, `Entra:Audience` in `appsettings.json`). Authority is derived as
   `https://login.microsoftonline.com/{tenantId}/v2.0`.
3. **`"Smart"` policy scheme** — the actual `DefaultAuthenticateScheme`/`DefaultChallengeScheme`. It
   doesn't validate anything itself; it inspects the raw bearer token's `iss` claim
   (`handler.ReadJwtToken(token).Issuer`) and forwards to `EntraJwt` if the issuer contains
   `login.microsoftonline.com`, otherwise forwards to `InternalJwt`. This is what lets the same API
   accept either a self-issued token or a real Entra ID token on the same endpoints without the caller
   needing to specify which scheme to use.

**Bug found and fixed this session:** the `InternalJwt` scheme was missing `MapInboundClaims = false`.
Without it, ASP.NET Core silently remaps short claim types (like `sub`) to long CLR/XML claim URIs
during validation, so `user.FindFirst(JwtRegisteredClaimNames.Sub)` — used by the `POST /api/quotes`
handler to resolve the caller's user ID — returned `null` even for a perfectly valid, correctly-scoped
token. The result: every authenticated create request returned `401 Unauthorized` for a reason that had
nothing to do with the token being invalid. The integration test factory
(`Quotes.Tests.Integration/CustomWebApplicationFactory.cs`) already had this flag set correctly, which
is exactly why the test suite (when it can run — it needs Docker) never caught it: the fix existed in
the test harness but not in the actual `Program.cs`. Fixed by adding the same flag to production
startup. This is a good, concrete "found a real bug through live end-to-end testing, not just passing
tests" story for an interview.

## Password storage and login flow

`POST /api/auth/login` — looks up the user by email, verifies the password with **BCrypt**
(`BCrypt.Net.BCrypt.Verify`, work factor from the library default). On success, issues:
- An **access token** (JWT, 15-minute expiry, claims: `sub` = user id, `email`, `scope` = `"quotes.write"`).
- A **refresh token** — a random 64-byte value, returned to the client in plaintext but stored
  **hashed** (`SHA-256`) in `RefreshTokens.Token`. The plaintext value is never persisted.

## Refresh token rotation and reuse detection

`POST /api/auth/refresh`:
- Looks up the refresh token by its SHA-256 hash.
- If the token has already been revoked (`RevokedAt is not null`) — this is a **reuse-detection**
  signal (someone is presenting a token that was already rotated away, which can indicate token theft)
  — the entire token family is revoked via `RefreshTokenService.RevokeTokenFamily`, which walks the
  `ReplacedByToken` chain forward and revokes every descendant. The request is rejected with `401`.
- Otherwise, a new refresh token is issued, the old one is marked revoked with
  `ReplacedByToken` pointing at the new one (building the chain the reuse-detection logic walks), and
  a new 15-minute access token is issued.

`POST /api/auth/logout` — revokes the given refresh token if it exists and isn't already revoked.
Idempotent — always returns `204` regardless.

## Authorization policies actually in use

Only **one** policy is registered: `"can-edit-quotes"`, which requires the claim `scope = "quotes.write"`.
Every login issues that scope unconditionally, so in practice any authenticated user via `InternalJwt`
satisfies it — there's no tiered permission model beyond "authenticated or not" today.

- `POST /api/quotes` → `.RequireAuthorization("can-edit-quotes")`
- `DELETE /api/quotes/{id}` → `.RequireAuthorization()` (any authenticated principal, no policy)
- `GET /api/quotes`, `GET /api/quotes/{id}` → no authorization requirement at all (public reads)

## Known gap: quote ownership is tested but not enforced

`Authorization/CanDeleteOwnQuoteHandler.cs` and `CanDeleteOwnQuoteRequirement.cs` implement a real,
resource-based authorization handler: given a quote ID, it loads the quote and succeeds only if the
caller's user ID matches `quote.UserId`. It's registered in DI
(`AddScoped<IAuthorizationHandler, CanDeleteOwnQuoteHandler>()`) and has **5 passing unit tests**
covering the success path, the `sub`-claim fallback, missing user ID, a nonexistent quote, and a quote
owned by someone else.

**It is not wired into any policy or endpoint.** `DELETE /api/quotes/{id}` uses a bare
`.RequireAuthorization()` — any authenticated user can soft-delete *any* quote, not just their own.
This is documented here rather than silently fixed, per this project's rule against changing working
endpoint behavior without it being asked for — but it's a legitimate, well-tested piece of logic sitting
unused, and a good interview talking point: "I wrote and tested the ownership check, but hadn't yet
wired it into the endpoint's authorization policy — here's exactly what that would look like:
`.RequireAuthorization(policy => policy.Requirements.Add(new CanDeleteOwnQuoteRequirement()))` with the
quote ID supplied as the authorization resource."

## Frontend: what the Angular app actually does

- **`auth.interceptor.ts`** — reads `access_token` from `localStorage`; if present, attaches it as
  `Authorization: Bearer <token>` to every outgoing request. It does not validate or refresh the token.
- **`auth.guard.ts`** (`CanActivateFn`, gates `/quotes/:id`) — checks only whether a value exists at
  `localStorage['access_token']`. It does **not** verify the token is well-formed, unexpired, or
  actually valid — it's a client-side UX gate ("don't show this page if you're obviously logged out"),
  not a security boundary. The real security boundary is the server validating the JWT on every
  request; a user could put an arbitrary string in `localStorage` and pass the guard, but any
  protected endpoint would still reject it.
- **`login.component.ts`** — as of this session, performs **real authentication**: it posts credentials
  to `POST /api/auth/login` and stores the genuine JWT/refresh token pair returned by the server via
  `AuthService.setSession()`. Earlier in this project's history, this page set a hardcoded string
  (`'day16-demo-token'`) into `localStorage` on button click — that was never real authentication, just
  a placeholder that satisfied the route guard for UI development purposes. It's called out here
  explicitly so the distinction is never misrepresented: a token existing in `localStorage` is not the
  same claim as "the user authenticated."
- **`auth.service.ts`** — a small signal-based wrapper around `localStorage` reads/writes, so the header
  nav can reactively show "Log in" vs. "Log out" without duplicating guard/interceptor logic.
