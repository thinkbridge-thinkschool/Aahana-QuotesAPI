# Day 25 — Identity end-to-end (OrderFulfillment capstone)

Closes two "known gaps" Day 23/24 documented but didn't fix (Ordering still targeting Sqlite, no
real Service Bus wiring), adds Entra ID app auth, and proves — by grepping the actual compiled
template, not by assertion — that zero secrets exist anywhere in this app's configuration.

## What changed, and where

```
src/BuildingBlocks/BuildingBlocks.Infrastructure/
  AzureServiceBusMessageBus.cs          NEW — real IMessageBus, sends via managed identity
  ServiceBusSubscriptionProcessor.cs    NEW — real consumer, one per module subscription
src/Modules/Ordering/Ordering.Infrastructure/
  OrderingModule.cs                     CHANGED — UseSqlServer vs UseSqlite, decided at runtime
src/Host/OrderFulfillment.Api/
  Program.cs                            CHANGED — Entra ID JWT auth, real Service Bus wiring
infra/
  main.bicep                            CHANGED — entraTenantId/entraAudience params (not secrets)
  sql/grant-managed-identity.sql        NEW — the one step Bicep genuinely cannot do (see below)
```

## API → SQL: Managed Identity, made real

`modules/sql.bicep` (Day 23) already provisioned Azure SQL with `azureADOnlyAuthentication: true`
and an output connection string using `Authentication=Active Directory Default` — but
`OrderingModule.cs` still called `UseSqlite(...)` unconditionally, so that connection string was
never actually used by anything. Fixed:

```csharp
var connectionString = configuration.GetConnectionString("OrderFulfillment");
var isAzureSql = connectionString?.Contains("Server=tcp:", StringComparison.OrdinalIgnoreCase) == true;

services.AddDbContext<OrderingDbContext>(options =>
{
    if (isAzureSql) options.UseSqlServer(connectionString);
    else options.UseSqlite(connectionString);
});
```

Same setting (`ConnectionStrings:OrderFulfillment`) decides the provider from its own shape — no
extra "which environment am I in" flag needed. Local dev's default
(`appsettings.json`: `"Data Source=orderfulfillment.db"`) still works untouched.
`Microsoft.EntityFrameworkCore.SqlServer` resolves `Authentication=Active Directory Default`
through `Microsoft.Data.SqlClient`'s own `DefaultAzureCredential` integration — the connection
string carries no password because there is no password, not because one was stripped out.

**The one piece Bicep genuinely cannot do:** `azureADOnlyAuthentication` gets the identity past
the server's front door, but a database user still has to exist *inside* `orderfulfillmentdb` for
that identity to touch any table — `CREATE USER ... FROM EXTERNAL PROVIDER` is T-SQL, not an ARM
resource. `infra/sql/grant-managed-identity.sql` is that step, parameterized so the same script
serves dev and prod, run once per environment by the Entra ID admin
(`sqlcmd -G` — Entra ID auth, no password here either). Documented as a manual/pipeline step
rather than automated via a `deploymentScript` Bicep resource: a `deploymentScript` spins up a
Container Instance to run it, which is real extra cost and complexity for a one-line-of-real-work
step — the honest trade-off here is a documented manual step, not a hidden one.

## API → Service Bus: Managed Identity, made real

Before Day 25, `IMessageBus`'s only implementation was `InProcessMessageBus` — real, but entirely
in-memory; nothing ever touched the Service Bus namespace Day 23/24 provisioned. Two new classes
close that gap, both authenticating the same way:

```csharp
builder.Services.AddSingleton(_ => new ServiceBusClient(serviceBusNamespace, new DefaultAzureCredential()));
```

`ServiceBusClient` takes a fully-qualified namespace and a `TokenCredential` — there is no
overload used here that takes a connection string or a `SharedAccessKey`. `DefaultAzureCredential`
resolves to the Container App's own managed identity when deployed, and to whatever's already
signed in (`az login`, Visual Studio, VS Code) when run locally — same code, same class, either
way. This is exactly the identity `infra/modules/servicebus-access.bicep` granted
`Azure Service Bus Data Owner` to; without that RBAC grant this would authenticate fine and then
fail on every send/receive with a 403.

- **`AzureServiceBusMessageBus`** (the producer) — stamps `EventType` (short name, e.g.
  `"OrderPlaced"`) on every message's `ApplicationProperties`, which is exactly the property
  `modules/servicebus.bicep`'s per-subscription SQL filters match against. Without this property
  every message would arrive unmatched at every subscription's filter and never be delivered —
  this was flagged as a known gap in Day 24's DESIGN.md; it's closed now.
- **`ServiceBusSubscriptionProcessor`** (the consumer) — one instance per subscription
  (Ordering, Inventory, Payments, Shipping, Notifications), registered as five separate
  `IHostedService`s so the host actually starts all five (same unkeyed/collection-registration
  reasoning as `IOutboxStore` — a single dependency resolves to "last registered", a collection
  resolves to "all of them"). Deserializes each message back to its exact concrete
  `IntegrationEvent` subtype using a second stamped property, `EventClrType`
  (`Type.AssemblyQualifiedName`), then dispatches to every registered
  `IIntegrationEventHandler<T>` — the same reflection-based dispatch `InProcessMessageBus` already
  used, so a module's handler code is identical whether the bus underneath is in-process or real
  Service Bus.

Both are wired in `Program.cs` only when `ServiceBus:Namespace`/`ServiceBus:Topic` are configured
(i.e. only when actually deployed with Day 23/24's infra); local dev with nothing set still gets
`InProcessMessageBus`, unchanged.

## Entra ID for app auth

```csharp
var entraTenantId = builder.Configuration["Entra:TenantId"];
var entraAudience = builder.Configuration["Entra:Audience"];

if (!string.IsNullOrEmpty(entraTenantId) && !string.IsNullOrEmpty(entraAudience))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options => { /* Authority + ValidIssuers/ValidAudience */ });
    builder.Services.AddAuthorization();
}
```

One scheme, not the dual `InternalJwt`/`EntraJwt` "Smart" policy scheme QuotesApi's `Program.cs`
uses — OrderFulfillment never had its own token issuance to begin with, so there's no second
scheme to arbitrate between. `POST /api/orders` calls `.RequireAuthorization()` when Entra config
is present.

**Neither `entraTenantId` nor `entraAudience` is a secret.** A tenant ID and an app registration's
audience (its Application ID URI / client ID) are both public identifiers — anyone can see them in
a token's own claims. They're plain Bicep params (`main.bicep`), plain container env vars
(`Entra__TenantId`, `Entra__Audience`), no `@secure()`, no Key Vault reference, because there is
nothing here that needs one.

**Known trade-off, not hidden:** both default to empty strings, and the app runs unauthenticated
when they're unset rather than refusing to start. Convenient for local dev and for this capstone
(no real Entra ID app registration was created this session — doing so is a real Azure AD write
action, out of scope without deciding to actually stand up the auth flow end-to-end). A production
hardening pass would make these required outside `Development`, failing closed instead of open.

## Why there's no Key Vault (a deliberate absence, not an oversight)

The exercise asks for "Key Vault references for any remaining config" — the operative word is
*remaining*. Checked what's left after Managed Identity covers SQL and Service Bus, and Entra ID
needs no secret to just validate incoming tokens:

- SQL: passwordless (Entra-ID-only auth + managed identity + the contained-user grant above).
- Service Bus: passwordless (`disableLocalAuth` + managed identity + RBAC).
- Entra ID app auth: the API only *validates* bearer tokens here — that needs a tenant ID and an
  audience, both public, never a client secret. A client secret would only enter the picture if
  this API also needed to *call out* as a confidential client (e.g. calling another API on a
  user's behalf), which nothing in this capstone does.
- `Payments.Infrastructure`'s only implementation is `FakePaymentGateway` — no real external
  gateway is configured, so there is no real API key to protect yet.

Genuinely nothing remains. Standing up an empty Key Vault "for the pattern" would be resume
padding, not engineering — it's simpler and more honest to state the reasoning and note where a
Key Vault reference would go the moment a real secret exists: `FakePaymentGateway`'s replacement
would read a real gateway's API key from a Container App secret in the shape
`@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/PaymentGatewayApiKey)`,
granted to the API's managed identity via a `Key Vault Secrets User` role assignment — the exact
same RBAC-grant module shape `servicebus-access.bicep` already establishes for Service Bus, reused
rather than reinvented.

## Proof: zero secrets, checked against the actual compiled template

```bash
grep -rn "@secure()" *.bicep modules/*.bicep         # no matches
grep -n "secrets" modules/api.bicep                  # no matches
az bicep build --file main.bicep --stdout | grep -c '"secrets"'   # 0
grep -rn "Password=\|pwd=\|AccountKey=\|SharedAccessKey" *.bicep modules/*.bicep   # no matches
```

All four came back empty/zero. This is a static, repeatable check against the actual compiled ARM
JSON — not a claim resting on "the code looks right." No live Container App exists to run
`az containerapp show --query properties.configuration.secrets` against right now (Day 24's
Container App never finished provisioning against the shared environment — see
`DAY24-DEPLOYMENT-STACKS.md`); this static proof is what's available without spending another real
deploy attempt against the same unresolved constraint.

## Known gaps (not hidden)

- No real Entra ID App Registration exists — `entraTenantId`/`entraAudience` are wired and ready,
  but auth stays off (fails open) until real values are supplied. Registering one is a real Azure
  AD write action, deliberately not done without deciding to exercise the full auth flow.
- `infra/sql/grant-managed-identity.sql` has not been run against a live database this session —
  Day 24's Container App issue means no live environment currently exists to run it against.
- `ServiceBusSubscriptionProcessor`'s dispatch has not been exercised against a live Service Bus
  namespace for the same reason — verified by code review and a clean build, not a live message
  round-trip.
