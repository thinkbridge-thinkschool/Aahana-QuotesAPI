# Infrastructure as Code — Day 23 (OrderFulfillment capstone)

Bicep for the Day 22 capstone slice (`OrderFulfillment`, the modular-monolith order-fulfillment
app — see `../DESIGN.md`), not for QuotesApi. QuotesApi's own `infra/` at the repo root already
has its own Day 23 pass and is untouched by this one — the two apps share nothing at the Azure
resource level (separate resource groups, separate names, separate subscriptions of this
template).

Three parameterized Bicep modules (API, SQL, Service Bus) behind one subscription-scope
orchestrator, with separate dev/prod `.bicepparam` files. No resource in here was ever clicked
into existence in the portal — everything below is exactly what was deployed/what-if'd.

## Layout

```
infra/
  main.bicep                 subscription scope: creates the resource group, wires the 3 modules together
  main.dev.bicepparam         dev parameter values
  main.prod.bicepparam        prod parameter values
  modules/
    api.bicep                 Container Apps environment + ACR + Container App (system-assigned identity)
    sql.bicep                 Azure SQL logical server (Entra-ID-only auth) + database + firewall rule
    servicebus.bicep          Namespace (RBAC-only) + topic + one subscription per subscribing module
    servicebus-access.bicep   grants the API's managed identity access to the Service Bus namespace
  whatif-dev-output.txt       captured output of the what-if run below
```

## Why a topic + 5 subscriptions, not a queue

DESIGN.md's async flow (`../DESIGN.md`, "Async flows") is genuine pub/sub, not point-to-point:
every module publishes integration events onto one stream, and several *different* modules each
independently react to their own subset of it. A queue only lets one logical consumer drain a
given message once; a topic lets every subscribing module get its own copy. The subscription map
is lifted directly from the `IIntegrationEventHandler<T>` registrations in each module's
`{Module}Module.cs`:

| Subscription | Reacts to |
|---|---|
| `Inventory` | `OrderPlaced`, `OrderCancelled` |
| `Payments` | `OrderConfirmed` |
| `Shipping` | `OrderPaymentReceived` |
| `Ordering` | `StockReserved`, `StockReservationFailed`, `PaymentCaptured`, `PaymentFailed`, `ShipmentDispatched` |
| `Notifications` | `OrderPlaced`, `OrderConfirmed`, `OrderCancelled`, `PaymentFailed`, `ShipmentDispatched` |

Each subscription's default (match-everything) rule is overridden with a SQL filter on a custom
`EventType` message property, so a module only ever receives the events it actually handles
instead of every event on the topic. This is also why the namespace SKU is `Standard`, not
`Basic` — Basic is queue-only and doesn't support topics/subscriptions at all.

Only the Ordering module has a real SQL database (`sql.bicep`) — Inventory, Payments, Shipping
and Notifications are in-memory per `../DESIGN.md`'s "What's built vs. scaffolded" section, so
they need no database of their own at this scope.

## Passwordless everywhere

- **ACR**: the Container App pulls images via its own system-assigned identity (`AcrPull` role),
  not a registry username/password.
- **SQL**: `azureADOnlyAuthentication: true` on the server — no SQL login exists at all. The
  output connection string uses `Authentication=Active Directory Default`, not a password.
- **Service Bus**: `disableLocalAuth: true` on the namespace — no SAS connection string is ever
  produced as an output. The API is granted `Azure Service Bus Data Owner` on the namespace via
  its managed identity instead (`modules/servicebus-access.bicep`).

There is no JWT signing key or other application secret in this stack — unlike QuotesApi,
OrderFulfillment.Api has no auth layer yet at kickoff scope, so there is nothing to mark
`@secure()` here.

## Provisioned ahead of the code (known gaps, not hidden)

This infra describes where OrderFulfillment *would* run in Azure; the app itself hasn't been
wired up to any of it yet:

- `Ordering.Infrastructure`'s `OrderingModule.cs` still calls `UseSqlite(...)`, not
  `UseSqlServer(...)` against the server this template provisions. Swapping that, plus the
  contained-database-user grant SQL's Entra-only auth needs
  (`CREATE USER ... FROM EXTERNAL PROVIDER` — SQL DML, not an ARM/Bicep resource, so not
  automated here), is follow-up work.
- `IMessageBus`'s only implementation today is `InProcessMessageBus` (see
  `BuildingBlocks.Infrastructure/IMessageBus.cs`, which explicitly calls out Azure Service Bus as
  "exactly the seam" for this). Nothing publishes to this namespace yet, and nothing stamps the
  `EventType` application property the subscription filters above key off — until an
  Azure-Service-Bus-backed `IMessageBus` exists, this namespace is provisioned and validated, not
  load-bearing.
- The SQL firewall rule (`AllowAllWindowsAzureIps`, i.e. any Azure-hosted resource) is broader
  than a VNet-scoped private endpoint would be — acceptable for this kickoff, worth tightening
  before real data goes in the database (see Day 27, "Security pass").

## Dev vs. prod: what actually differs

| | dev | prod |
|---|---|---|
| Container App CPU/memory | 0.25 vCPU / 0.5Gi | 1 vCPU / 2Gi |
| Container App replicas | 0–1 (scales to zero) | 2–10 |
| ACR SKU | Basic | Standard |
| SQL SKU | Basic | `GP_S_Gen5_2` (General Purpose, serverless) |
| Service Bus SKU | Standard (floor — topics need it) | Standard |
| Max delivery count | 5 | 10 |

Same modules, same `main.bicep` — only the two `.bicepparam` files differ.

## Why nothing here is a literal

`aadAdminLogin` and `aadAdminObjectId` are pulled with `readEnvironmentVariable(...)` inside the
`.bicepparam` files, not written as literal values — confirmed by actually running
`bicep build-params` without those variables set first, which fails loudly (`BCP427: Environment
variable "..." does not exist`) instead of silently deploying with an empty admin identity. A
committed placeholder GUID/string would *look* safe and quietly become a copy-paste trap the
first time someone reused this file for a real environment.

## Verified, not just written

Ran against the real Azure subscription this session is authenticated to (`az account show`),
with synthetic placeholder values in the shell environment — never committed:

```
export AAD_ADMIN_LOGIN="sql-admins-dev-placeholder"
export AAD_ADMIN_OBJECT_ID="00000000-0000-0000-0000-000000000000"
export CONTAINER_IMAGE_TAG="dev-latest"

az deployment sub what-if --name orderfulfillment-day23-whatif --location centralindia \
  --template-file main.bicep --parameters main.dev.bicepparam
```

Result: **16 resources to create**, correctly named/sku'd per the dev param file
(`orderfulfillment-rg-dev`, `orderfulfillment-sql-dev-...` on the `Basic` SQL SKU,
`orderfulfillment-sb-dev-...` with 5 subscriptions and their `$Default` rules showing the correct
per-module `EventType IN (...)` SQL filter — e.g. `Payments`'s rule is
`EventType IN ('OrderConfirmed')`) — full output in `whatif-dev-output.txt`.

What-if only shows most resources: it short-circuits the `api` and `servicebus-access` modules
with a `NestedDeploymentShortCircuited` diagnostic, because their parameters include
runtime `reference()`-style values (each other's outputs) that what-if's static diff can't
evaluate ahead of a real deploy — a documented Azure limitation, not a template defect. To confirm
those two modules are actually valid (not just silently skipped), `az deployment sub validate`
was run separately against both `main.dev.bicepparam` and `main.prod.bicepparam` — both returned
`"provisioningState": "Succeeded"`, `"error": null`, covering every module including the two
what-if couldn't diff.

## Known gaps (kickoff scope, not hidden)

- No actual `az deployment sub create` was run — only `what-if` and `validate`, both read-only
  against Azure. Provisioning the real dev/prod resource groups is a separate, deliberate step.
- Subscription filters assume a future publisher stamps `EventType` on send (see "Provisioned
  ahead of the code" above) — until then they'd pass zero messages, not the wrong ones.
- No Dockerfile exists yet for `OrderFulfillment.Api` — `modules/api.bicep` bootstraps the
  Container App with a public placeholder image (same trick QuotesApi's `api.bicep` uses) so the
  template doesn't need a real image to exist first; building and pushing the real image is
  Day 24 ("Deploy the full stack"), not this pass.
