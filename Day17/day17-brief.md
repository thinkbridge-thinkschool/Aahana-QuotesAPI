# Day 17 brief — deploy the Angular frontend to Azure Static Web Apps

## Target

- **Frontend to deploy:** `Day15/Day15App` (Angular 21, standalone components, real login + quotes list/detail).
- **Target SWA URL:** the SWA-issued default hostname (`https://quotes-frontend.<random>.azurestaticapps.net`) plus a custom domain once DNS is delegated — no domain is fabricated here; whichever domain I actually attach gets recorded in the verification log below at deploy time.
- **Week-1 API (real, already running code):** `QuotesApi.csproj` at the repo root, currently deployable as a container (`infra/main.bicep`, resource group `thinkschool-rg`, container app `quotes-api`, region `centralindia`).

## Endpoints and fields the frontend must hit

| Method | Path | Auth | Used for |
|---|---|---|---|
| `POST` | `/api/auth/login` | none (issues token) | Login form → `{ email, password }` → `{ access_token, refresh_token, expires_in }` |
| `POST` | `/api/auth/refresh` | none (refresh token in body) | Silent token refresh |
| `GET` | `/api/quotes?page=&size=` | none required | Quotes list page |
| `GET` | `/api/quotes/{id}` | none required (has an intentional 1.5s delay from an earlier day's exercise) | Quote detail page |
| `POST` | `/api/quotes` | `can-edit-quotes` policy (`scope=quotes.write` claim) | Create quote |
| `DELETE` | `/api/quotes/{id}` | any authenticated principal (`RequireAuthorization()`, no scope claim) | Moderation delete — **this is the Managed-Identity path** |

## Auth requirement

The Week-1 API already validates two JWT schemes side by side (`Program.cs`, the `"Smart"` policy scheme): an internal HMAC-signed user JWT, and genuine Microsoft Entra ID tokens (tenant `8d46a076-d093-416d-a57b-8692cde13bf8`, audience `api://953b5bcb-682b-47b4-a116-8936323f5bec`). A browser SPA cannot itself hold a Managed Identity, so:

- End users still log in interactively and get the internal JWT (unchanged) for reads and for creating quotes.
- The **moderation delete** path (`DELETE /api/quotes/{id}`) is called by a small Azure Function linked as the SWA's backend. That Function has a **system-assigned Managed Identity**, acquires an Entra token for `api://953b5bcb-682b-47b4-a116-8936323f5bec/.default` via `DefaultAzureCredential`, and forwards it as the `Authorization: Bearer` header. **No client secret, connection string, or key is stored in the repo, GitHub secrets, or app settings** — only the API's public base URL and its (non-secret) Entra audience identifier.

## Non-negotiables

- Live SWA URL must load the real app (not a placeholder page).
- Lighthouse ≥ 95.
- The MI-authenticated call must be provably token-based (I will capture the Function's own log line plus a `curl` showing 401 without a token vs. success with the MI-acquired one) — not "trust me."
