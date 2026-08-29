# Day 17 submission — SWA deployment + Managed Identity wiring

## 1. Brief

See [`day17-brief.md`](./day17-brief.md).

## 2. Agent output

| Piece | File(s) |
|---|---|
| Angular env wiring (no more hardcoded `localhost:5145`) | `Day15/Day15App/src/environments/environment.ts`, `environment.prod.ts`, `angular.json` (`fileReplacements`), `quote.service.ts`, `pages/login/login.component.ts` |
| SWA routing/security config | `Day15/Day15App/public/staticwebapp.config.json` |
| Managed-Identity backend (Azure Function, isolated worker, .NET 8) | `Day17/api/Day17Api.csproj`, `Program.cs`, `DeleteQuoteFunction.cs`, `host.json` |
| Infra: Static Web App + Function App (system-assigned identity) + linked backend + custom domain slot | `Day17/infra/swa.bicep`, `swa.parameters.json` |
| CI/CD | `.github/workflows/day17-swa-deploy.yml` |
| Week-1 API fix (see bug below) | `Program.cs` (root), `appsettings.json` (`Cors:AllowedOrigins`) |

Locally verified before writing this log:
- `dotnet build QuotesApi.csproj` — succeeds.
- `dotnet build Day17/api/Day17Api.csproj` — succeeds.
- `az bicep build --file Day17/infra/swa.bicep` — compiles with no errors.
- `npx ng build --configuration production` in `Day15/Day15App` — succeeds; output at `dist/Day15App/browser` (confirmed against the `output_location` used in the workflow); confirmed the production bundle contains the `REPLACE-WITH-QUOTES-API-URL` placeholder (substituted by CI from the `QUOTES_API_BASE_URL` repo variable) and **not** `localhost:5145`; confirmed `staticwebapp.config.json` is copied into the build output via the existing `public/` asset glob.

## 3. Verification log

> This session did not have Azure credentials (`az` was installed but not logged in) or a GitHub token, so the actual cloud provisioning and live checks below are **run by you**, not fabricated here. Fill in each `<result>` as you go — this is the checklist to work through, not a claim that it already happened.

### One-time setup (you run this)

```bash
# 1. Log in and deploy the Week-1 API if not already live
az login
az deployment sub create --location centralindia \
  --template-file infra/main.bicep --parameters infra/main.parameters.json

# Capture the live QuotesApi URL:
QUOTES_API_URL=$(az containerapp show -g thinkschool-rg -n quotes-api \
  --query "properties.configuration.ingress.fqdn" -o tsv)
echo "https://$QUOTES_API_URL"

# 2. Deploy the SWA + Function backend
az deployment group create -g thinkschool-rg \
  --template-file Day17/infra/swa.bicep \
  --parameters Day17/infra/swa.parameters.json \
  --parameters quotesApiBaseUrl="https://$QUOTES_API_URL"

# 3. Grant the Function's Managed Identity a token for the QuotesApi audience.
#    This is an Entra ID app-role step, not Azure RBAC — do it once:
FUNC_PRINCIPAL_ID=$(az deployment group show -g thinkschool-rg -n swa \
  --query "properties.outputs.functionAppPrincipalId.value" -o tsv)
# Then in the Entra app registration for api://953b5bcb-682b-47b4-a116-8936323f5bec:
#   - Add an app role (e.g. "Quotes.Delete") if one doesn't exist.
#   - Assign $FUNC_PRINCIPAL_ID to that app role (App registrations >
#     Enterprise app > Users and groups, or `az rest` against Microsoft Graph).

# 4. Wire GitHub: add repo secret AZURE_STATIC_WEB_APPS_API_TOKEN (from the
#    SWA resource's deployment token) and repo variable QUOTES_API_BASE_URL
#    (the same https://$QUOTES_API_URL — not a secret, just the public base URL).

# 5. Add the custom domain (once you have one) via
#    az staticwebapp hostname set, then verify DNS (CNAME/TXT as Azure
#    instructs) before the domain shows "Ready" in the portal.
```

### Live checks (fill in after deploying)

| Check | Command / method | Result |
|---|---|---|
| Live SWA URL loads | Open `https://<swa-hostname>` (and the custom domain once bound) | `<paste URL + screenshot/confirmation>` |
| Lighthouse ≥ 95 | `npx lighthouse https://<swa-hostname> --output=json --output-path=./lighthouse.json --only-categories=performance,accessibility,best-practices,seo --chrome-flags="--headless"` | `<paste the four category scores>` |
| Loading state | Throttle network (DevTools) and load `/quotes/{id}` — the intentional 1.5s API delay should show a visible loading indicator | `<pass/fail + note>` |
| Empty state | Query `/api/quotes?page=999&size=10` (past the last page) and confirm the UI shows an empty list, not an error | `<pass/fail + note>` |
| Error state | Stop/scale the container app to 0, reload `/quotes` — confirm the error interceptor surfaces a visible error, not a blank screen | `<pass/fail + note>` |
| 401 without a token | `curl -i https://<swa-hostname>/api/quotes/1 -X DELETE` (through the SWA `/api/*` route, no auth header) → expect the Function to still succeed in getting **its own** MI token, but a direct unauthenticated curl straight to the API should 401: `curl -i https://<quotes-api-fqdn>/api/quotes/1 -X DELETE` (no `Authorization` header) | `<paste status code, expect 401>` |
| MI token actually used, no secret anywhere | `grep -ri "clientsecret\|client_secret\|connectionstring.*key" Day17/ Day15/` (expect no matches); Function App → Identity blade shows "System assigned: On"; Function App → Configuration has **no** secret-shaped setting, only `QuotesApi__BaseUrl` and `QuotesApi__EntraAudience`; Application Insights / Function logs show the `"Deleted quote {QuoteId} via Managed Identity call"` line with a successful upstream status | `<paste grep output (should be empty) + log line>` |

## One concrete bug I caught and fixed

**The API's CORS policy only ran in `Development`.** The original `Program.cs` had:

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseCors("AngularDevClient");
}
```

`AngularDevClient` was also hardcoded to `http://localhost:4200` only. Deployed as-is, the browser (SPA calling the API directly for login/reads) would have hit CORS failures in production — the live SWA origin was never in the allow-list, and CORS was never even applied outside Development. I changed this to a config-driven `Cors:AllowedOrigins` (default `[]`, override via the Container App's environment variables) merged with the local dev origin, and now call `app.UseCors("AngularClient")` unconditionally (`Program.cs`, `appsettings.json`). Without this fix, the deployment would have "worked" (built, deployed, Lighthouse would even pass since Lighthouse doesn't exercise cross-origin XHR the same way a real login does) while every login attempt from the live SWA URL silently failed in the browser console with a CORS error — the kind of bug that only shows up when a real user tries to log in, not in any of the build/deploy checks above.

## What breaks if the API's auth or a key endpoint changes

- **If `Entra:TenantId` or `Entra:Audience` in `appsettings.json` changes** (e.g. the API's app registration is recreated), the Function keeps requesting a token for the *old* audience — `DefaultAzureCredential.GetTokenAsync` will still succeed (Azure AD doesn't know the audience is "wrong"), but every call will then get a `401` from the API because the issuer/audience no longer match `ValidIssuers`/`ValidAudience`. This fails silently until someone checks the Function's logs or the API's `401` responses — `QuotesApi:EntraAudience` in the Function's app settings must be updated in lockstep with the API's config.
- **If `DELETE /api/quotes/{id}` ever gets a `RequireAuthorization("can-edit-quotes")` scope check** (matching the `POST` endpoint), the Managed Identity path breaks outright: app-only Entra tokens carry an app role in `roles`, not a `scope` claim, so `RequireClaim("scope", "quotes.write")` would never match, and the Function would start getting `403`s with no code change on its side — the fix would need either an app-role-based policy on the API or a claim-mapping change, not just a Function-side fix.
- **If the API's Entra app registration's app roles change** (the one the Function's Managed Identity is assigned to), the Function's *token acquisition* itself starts failing (`GetTokenAsync` throws), which is the `BadGateway` branch already coded in `DeleteQuoteFunction.cs` — this one at least fails loudly and is caught.

## What I learned this session

Managed Identity only exists for Azure-hosted compute — a browser SPA can never hold one. The pattern that actually satisfies "no client secret, MI end to end" is to keep interactive user auth for the SPA and put the *service-to-service* call (the one action a human isn't directly performing) behind a small MI-authenticated backend. Trying to force the whole frontend through MI would have meant fabricating an architecture that doesn't exist.

## What would break this

A network partition or misconfigured `linkedBackends` region mismatch between the Function and the SWA would make the SWA `/api/*` route 502 even though the Function itself is healthy — `region` in the `linkedBackends` resource must match where the Function actually runs.
