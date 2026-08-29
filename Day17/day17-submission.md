# Day 17 submission — SWA deployment + Managed Identity wiring

## 1. Brief

See [`day17-brief.md`](./day17-brief.md).

## 2. Agent output

| Piece | File(s) |
|---|---|
| Angular env wiring (no more hardcoded `localhost:5145`) | `Day15/Day15App/src/environments/environment.ts`, `environment.prod.ts`, `angular.json` (`fileReplacements`, `optimization.styles.inlineCritical: false`), `quote.service.ts`, `pages/login/login.component.ts` |
| SWA routing/security config | `Day15/Day15App/public/staticwebapp.config.json`, `public/robots.txt` |
| Managed-Identity backend (Azure Function, isolated worker, .NET 8) | `Day17/api/Day17Api.csproj`, `Program.cs`, `DeleteQuoteFunction.cs`, `host.json` |
| Infra: Static Web App + Function App (system-assigned identity) + linked backend + custom domain slot | `Day17/infra/swa.bicep`, `swa.parameters.json` |
| Week-1 API container infra: ACR (keyless pull) + Container Apps environment, restructured for a valid subscription/resource-group scope split | `infra/main.bicep`, `infra/resources.bicep`, `infra/main.parameters.json` |
| Week-1 API container image | `Dockerfile`, `.dockerignore` (used as a build-context reference; the actual pushed image was built via `dotnet publish -r linux-musl-x64 -p:PublishProfile=DefaultContainer`) |
| CI/CD (for future pushes; this session's live deploy was done directly via CLI — see below) | `.github/workflows/day17-swa-deploy.yml` |
| Week-1 API fixes found during live deployment (see bugs below) | `Program.cs` (root), `appsettings.json` (`Cors:AllowedOrigins`) |

## 3. Verification log — live, not simulated

Everything below was run against **actually deployed Azure resources** in an Azure for Students subscription (`68a88491-0c9b-4750-9cb7-1fa2157daeb8`, tenant "Amity University", same tenant as `Entra:TenantId`), created in this session via `az`/`dotnet publish`/direct zip-deploy (GitHub Actions secrets weren't configured, so the CI workflow above is prepared but not yet the path used for this deploy).

### Live resources

| Resource | URL |
|---|---|
| Angular frontend (Azure Static Web Apps) | **https://zealous-mud-054744c00.7.azurestaticapps.net** |
| Week-1 QuotesApi (Azure Container Apps) | **https://quotes-api.orangebeach-4969d067.centralindia.azurecontainerapps.io** |
| Managed-Identity bridge Function | **https://quotes-frontend-api.azurewebsites.net** |

Custom domain: **not set up** — no domain was available for this exercise, so this ships on the SWA/Function default hostnames. Everything else in the brief (live URL, Lighthouse ≥ 95, Managed Identity, no secret) is met on those hostnames.

### Lighthouse (live SWA URL)

Ran twice with `npx lighthouse` against the live URL (headless Chrome, `performance,accessibility,best-practices,seo`):

| Run | Performance | Accessibility | Best Practices | SEO |
|---|---|---|---|---|
| 1st (before fixes) | 98 | 100 | 92 | 91 |
| 2nd (after fixes) | **97** | **100** | **100** | **100** |

All four categories are ≥ 95 on the second run. The first run's Best Practices/SEO gaps were real, fixed issues, not noise — see bugs #3 and #4 below.

### Managed Identity proof

```
# No token at all against the real API directly — must be rejected:
curl -X DELETE https://quotes-api.orangebeach-4969d067.centralindia.azurecontainerapps.io/api/quotes/999
→ 401 Unauthorized

# Through the Managed-Identity Function, for a quote id that doesn't exist:
curl -X DELETE https://quotes-frontend-api.azurewebsites.net/api/quotes/999
→ 404 Not Found

# Same call routed through the SWA's own /api/* path (the linked backend):
curl -X DELETE https://zealous-mud-054744c00.7.azurestaticapps.net/api/quotes/999
→ 404 Not Found
```

A 404 (not a 401) proves the Function's Managed-Identity-acquired Entra token was **accepted** by the API and the request reached real business logic (`DeleteAsync` correctly reporting "not found" for a nonexistent id) — the only way to get a 404 instead of a 401 here is for the bearer token to have passed the API's `EntraJwt` validation.

No secret exists anywhere in this path:
- Function App settings: only `QuotesApi__BaseUrl` and `QuotesApi__EntraAudience` (both public identifiers, not secrets) — confirmed via `az functionapp config appsettings list`.
- The Function authenticates via `DefaultAzureCredential`, which resolves to its system-assigned Managed Identity in Azure — no client id/secret pair configured anywhere.
- The QuotesApi container's only secret (`Jwt__Key`, the *internal* user-JWT signing key, unrelated to the Entra/MI path) is stored as an encrypted Container Apps secret, generated fresh at deploy time and never written to a file, git, or chat output.
- Repo-wide grep for connection strings, client secrets, and private keys turned up nothing (see command below).

```
grep -rniE "AccountKey=|client_secret|BEGIN (RSA |EC )?PRIVATE KEY" \
  --include="*.cs" --include="*.json" --include="*.ts" --include="*.bicep" . 
→ no matches
```

### States exercised

- **Empty**: `GET /api/quotes?page=1&size=3` on the live API returns `[]` (fresh database, no seed data) — confirmed via curl. The quotes list page renders this as an empty list rather than erroring.
- **Loading**: `GET /api/quotes/{id}` carries an intentional 1.5s artificial delay from an earlier day's exercise (`QuoteEndpointExtensions.cs`) — confirmed the delay is still present in the live deployment via `curl -w "%{time_total}"`.
- **Failed-token / 401**: see the Managed Identity proof above — a direct call with no token is rejected with 401.
- **Error state (UI), visually confirmed live**: attempting the login page's own advertised demo credentials before bug #4 (below) was fixed rendered "Invalid email or password." correctly in the live UI, backed by a real `401` from `/api/auth/login` — a genuine, unplanned real-world error-state check, not a synthetic one. After the fix, the same form with the same credentials succeeds (`200`, valid `access_token`).

## Four concrete bugs I caught and fixed during the live deployment

This section reports what actually broke, in the order I hit it — not a hypothetical list.

**1. Container image built for the wrong C library.** I initially pushed the image via `dotnet publish --os linux --arch x64` (RID `linux-x64`, glibc) on top of the `aspnet:10.0-alpine` base image (musl libc). The container started but crashed on first request: `DllNotFoundException: Unable to load shared library 'e_sqlite3'... symbol not found`. Fixed by publishing with `-r linux-musl-x64` instead, which restores the musl-linked native SQLite binary that actually matches the Alpine runtime.

**2. Non-root container user couldn't write its own database file.** After fixing #1, the container started but crashed on migration: `SQLite Error 14: unable to open database file` at `quotes.db` (a relative path resolving to `/app`, the working directory). Microsoft's `aspnet` images run as a non-root user by default, and `/app` isn't writable by that user. Fixed by pointing `ConnectionStrings:QuotesDb` at `/tmp/quotes.db` via a Container Apps environment variable — a config change, no code or image change needed. (This also means the SQLite data doesn't survive a container restart, which is an accepted limitation for this exercise, not something masked.)

**3. The API's own auth-scheme router only recognized half of Entra's issuer formats — this is the one that actually blocked Managed Identity.** After fixing #1 and #2, the API was healthy, but the Function's Managed-Identity-authenticated call to `DELETE /api/quotes/{id}` came back `401` even though the token had been issued for the right tenant and audience. The QuotesApi's `"Smart"` policy scheme (`Program.cs`) picks between the internal JWT handler and the `EntraJwt` handler by checking whether the token's issuer string contains `"login.microsoftonline.com"` (the Entra **v2** issuer format). But this app registration's `requestedAccessTokenVersion` is `null` (confirmed via Microsoft Graph), which means Entra issues **v1** tokens for the client-credentials flow the Function's Managed Identity uses — issuer `https://sts.windows.net/{tenant}/`. The router never recognized that format, so every Managed-Identity token got misrouted to the internal HMAC validator and failed signature validation. `EntraJwt`'s own `ValidIssuers` list already included both formats — the bug was purely in the routing heuristic, not the validation config. Fixed by checking for `sts.windows.net` as well as `login.microsoftonline.com` in the router.

**4. The login page's own demo credentials didn't work.** After the API was fully healthy, I actually tried logging in on the live site — `demo@thinkschool.local` / `ThinkSchool2026`, exactly what the login page itself advertises as a hint — and got "Invalid email or password." Nothing had ever created that user; it only "worked" for whoever had a locally-seeded dev database. On a fresh deployment (the normal case) it's just false advertising in the UI. There's no signup endpoint either, so there was no way for a new user to self-serve around it. Fixed by seeding that exact user idempotently in `Program.cs` right after the migration step — verified live with a real `POST /api/auth/login` call returning a valid `access_token`.

I also caught and fixed, as smaller items along the way: the API's CORS policy only running in `Development` and only allow-listing `localhost:4200` (now reads `Cors:AllowedOrigins` from config and applies in every environment); the original `infra/main.bicep` mixing subscription-scope and resource-group-scope resources in a way that could never have compiled (restructured into `main.bicep` + `resources.bicep`); and `cpu: 0.5` on the Container App template, which Bicep's `int`-only number literals can't express (needs `json('0.5')`).

## What breaks if the API's auth or a key endpoint changes

- **If `Entra:TenantId` or `Entra:Audience` changes** (e.g. the API's app registration is recreated), the Function keeps requesting a token for the *old* audience — `DefaultAzureCredential.GetTokenAsync` still succeeds, but every call then gets a `401` because the issuer/audience no longer match. This fails silently until someone checks the API's logs or response codes — `QuotesApi:EntraAudience` in the Function's app settings must be updated in lockstep.
- **If `requestedAccessTokenVersion` is later set to `2`** on the app registration (a very plausible future "cleanup"), Entra would start issuing v2 tokens for the Managed Identity too — which the now-fixed router already handles, but this is a reminder that the router's dual-issuer check is a workaround for the registration's current (default) config, not something that can be safely narrowed back to one format without re-verifying against the registration's actual settings.
- **If `DELETE /api/quotes/{id}` ever gets a `RequireAuthorization("can-edit-quotes")` scope check** (matching the `POST` endpoint), the Managed Identity path breaks outright: app-only Entra tokens carry an app role in `roles`, not a `scope` claim, so `RequireClaim("scope", "quotes.write")` would never match, and the Function would start getting `403`s with no change on its own side.
- **If the SQLite file's `/tmp` path is ever "fixed" by moving it back to `/app`** without also making `/app` writable or switching to a real managed database, every write silently starts failing again the way bug #2 did.

## What I learned this session

Managed Identity only exists for Azure-hosted compute — a browser SPA can never hold one. The pattern that actually satisfies "no client secret, MI end-to-end" is to keep interactive user login for the SPA and put only the *service-to-service* action (moderation delete) behind a small MI-authenticated Function backend. The harder lesson from actually deploying, though: an MI token being technically valid isn't enough — the *receiving* API's own auth-routing logic has to recognize the token format Entra actually issues for that specific app registration's configuration, which isn't always the v2/`login.microsoftonline.com` format most examples assume.

## What would break this

Beyond the auth-version fragility above: a network/region mismatch between the Function and the SWA's `linkedBackends` region would make the SWA `/api/*` route fail even though the Function itself is healthy — the `region` in `linkedBackends` must match where the Function actually runs. Also, this Azure for Students subscription auto-enables a region-restriction policy (only `centralindia`, `indiasouthcentral`, `eastasia`, `uaenorth`, `malaysiawest` allowed) and auto-enables App Service Authentication (EasyAuth) on new Function Apps by default — both silently blocked this deployment until diagnosed, and would silently block a fresh redeploy under a different subscription with different defaults.
