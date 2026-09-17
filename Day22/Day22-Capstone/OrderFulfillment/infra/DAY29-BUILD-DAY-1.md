# Day 29 — Build day 1: foundation + happy path (OrderFulfillment capstone)

Per the Day 28 build plan, Day 29's goal was: get a real Container App running, root-causing Day
24's unresolved provisioning failure, and get the happy path working against real infra by EOD.
Both happened — plus two more real bugs the first two exposed, all fixed live against the real
subscription, not simulated.

## The real blocker, finally root-caused

Day 24 left the Container App stuck in `Failed` provisioning with a generic `Operation expired`
and a hypothesis it never got to test. Querying the Container Apps environment's own Log
Analytics workspace (`ContainerAppSystemLogs_CL` — not the generic ARM activity log Day 24 used)
found the real, specific error, repeating in a loop since the app was first deployed:

```
Failed to construct registry secret for registry 'orderfulfillmentacr...azurecr.io' for
ContainerApp 'orderfulfillment-api-dev'. ACR token exchange endpoint returned error status: 401.
```

`az role assignment list` against the ACR confirmed it: **zero role assignments existed anywhere
in the resource group** — `acrPullRoleAssignment` in `modules/api.bicep` had never actually run.
The real mechanism: that resource has an implicit dependency on `containerApp` (it reads
`containerApp.identity.principalId`), and ARM won't attempt a dependent resource until its
dependency's own deployment operation reaches a terminal state. But the Container App's
provisioning was stuck retrying that exact registry-credential check for the full ~20-minute
timeout — because AcrPull didn't exist yet — so it never reached `Succeeded`, so
`acrPullRoleAssignment` was never even attempted, so AcrPull could never be granted. A genuine,
self-reinforcing deadlock, not a flaky timeout.

**The fix** (`modules/api.bicep`, `main.bicep`): a new `acrPullGranted` parameter, default
`false`. When false, the Container App declares **no registries at all** and runs the public
bootstrap image — nothing for the platform to validate, so the deployment actually reaches
`Succeeded` and `acrPullRoleAssignment` finally runs. Once that first deploy has succeeded and a
real image has been pushed, a second deployment with `acrPullGranted: true` adds the registry
entry and swaps in the real image. Confirmed live: granting AcrPull to the already-created
identity by hand (`az role assignment create`, bypassing the template entirely) is what
immediately unstuck the year-old-feeling stuck revision — proving the diagnosis before writing
the permanent fix.

**Operational note, not hidden:** `acrPullGranted` is a one-time promotion flag for a fresh
environment's *first* deploy, not something to bake into `main.dev.bicepparam` permanently —
setting it `true` by default would recreate the exact same deadlock the very first time someone
tears down and recreates the resource group from scratch. The committed default stays `false`;
flipping it to `true` for an already-bootstrapped environment is a one-off
`az deployment sub create ... --parameters acrPullGranted=true` a human runs once, documented here
rather than encoded as a silent trap in a param file.

## Two more real bugs the fix immediately exposed

Getting past the AcrPull deadlock meant the real app container could finally start for the first
time in this capstone's history — which meant it could finally hit the code paths nothing had
ever actually exercised live:

**1. Alpine has no ICU by default; `Authentication=Active Directory Default` needs it.** First
real-image startup crashed immediately: `System.NotSupportedException: Globalization Invariant
Mode is not supported`, thrown from `Microsoft.Data.SqlClient`'s very first connection attempt.
`mcr.microsoft.com/dotnet/aspnet:10.0-alpine` ships without ICU to stay small; the AAD
authentication path inside `Microsoft.Data.SqlClient` needs full globalization support internally.
Fixed in the `Dockerfile`'s runtime stage: `apk add --no-cache icu-libs` +
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`. QuotesApi's own Dockerfile never hits this because
it only ever uses SQLite/local auth in its deployed container, never AAD-based SqlClient auth.

**2. No `ASPNETCORE_ENVIRONMENT` was ever set on the deployed container.** Day 27's fail-closed
auth change refuses to start unauthenticated outside `Development` — correct behavior, but nothing
in `modules/api.bicep` ever set that variable, so the deployed app defaulted to `Production` and
crashed on every start with the exact exception Day 27 wrote on purpose:
`Entra:TenantId and Entra:Audience must be configured outside Development`. Fixed properly, not
patched around: a new `aspnetCoreEnvironment` parameter, threaded through `main.bicep` the same
way every other dev/prod difference in this stack already is. `main.dev.bicepparam` sets it to
`Development` — the one value Program.cs's own check already treats as sanctioned to run
unauthenticated, since no real Entra App Registration exists yet (Day 25's still-open gap).
`main.prod.bicepparam` leaves the default `Production` untouched, so Day 27's hardening stays
fully in force there.

## A third and fourth: gaps Day 24's stuck deployment had been silently hiding

Because the Container App never succeeded before today, **two other modules' dependent resources
never ran either** — the exact same class of bug as the AcrPull deadlock, just not yet discovered
because nothing had gotten far enough to find them:

**3. SQL's public network access was `Disabled` (Day 27's own default) with no VNet-integrated
path to reach it.** Day 27's own DESIGN.md flagged this precisely: the private endpoint is real
and provisioned, but `thinkschool-env` (the shared Container Apps environment) isn't
VNet-integrated, so nothing can reach SQL through it. Confirmed live today for the first time —
the container's first real connection attempt failed with `Deny Public Network Access is set to
Yes`. Applied Day 27's own documented escape hatch for dev: re-enabled public network access and
added an Azure-services firewall rule on the **dev** SQL server only — prod's default stays
`Disabled`, so this is a dev-only, reversible relaxation, not a rollback of the hardening pass.

**4. `servicebus-access.bicep`'s RBAC grant had the identical deadlock as AcrPull.** It depends on
`api.outputs.principalId`, which only exists once the `api` module succeeds — which it never had.
`az role assignment list` against the Service Bus namespace confirmed zero assignments existed.
Granted `Azure Service Bus Data Owner` by hand the same way as the AcrPull fix, for the same
reason: this deploy is the first time `api` has ever actually reached `Succeeded`, so this is the
first time anything downstream of it has had the chance to run at all.

## A fifth: a real bug in a script that had never actually been run

`infra/sql/grant-managed-identity.sql` (written Day 25, explicitly flagged there as "not run
against a live database this session") failed the first time it was actually executed:
`Incorrect syntax near '+'`. The `db_datareader`/`db_datawriter` grants inlined a string
concatenation (`N'...' + @managedIdentityName + N'...'`) directly as `EXEC sp_executesql`'s first
argument — `EXEC`'s argument-list grammar doesn't accept a bare expression there the way a normal
expression context would, unlike the `CREATE USER` block just above it, which happened to assign
the same kind of concatenation to a variable first. Fixed by doing the same thing for both grants.
Run for real afterward via a raw `Microsoft.Data.SqlClient` connection authenticated with an
`az account get-access-token` access token (PowerShell) — sidesteps a separate, real local-tooling
gap: this machine's classic ODBC `sqlcmd` lacks the ADAL library needed for `-G` AAD auth at all,
and `Authentication=Active Directory Default` run locally hits `DefaultAzureCredential`'s
`ManagedIdentityCredential` probe timing out (no IMDS endpoint exists on a dev laptop) in a way
that, in this SDK version, aborts the whole credential chain instead of falling through to
`AzureCliCredential`. An access token obtained directly from the already-authenticated `az` CLI
and handed to `SqlConnection.AccessToken` bypasses both problems entirely.

## The happy path, verified live

```bash
curl -s "https://orderfulfillment-api-dev.orangebeach-4969d067.centralindia.azurecontainerapps.io/api/v1/orders" \
  -X POST -H "Content-Type: application/json" \
  -d '{"customerId":"11111111-1111-1111-1111-111111111111","lines":[{"sku":"WIDGET-1","quantity":2,"unitPrice":9.99,"currency":"USD"}]}'
```

```
{"orderId":"27e35407-1044-48bb-bd29-45268621687e"}
HTTP 201
```

Not just an HTTP response — queried directly against the live Azure SQL database afterward
(same access-token technique as above), independent of what the API claims:

```
Order: 27e35407-1044-48bb-bd29-45268621687e | Customer: 11111111-1111-1111-1111-111111111111 | Status: Pending
```

The order genuinely exists, in the real Azure SQL database this capstone provisioned back in Day
23, written there by the real deployed Container App authenticating as its own managed identity —
the entire chain (Container App → managed identity → Azure SQL, passwordless end to end) working
live for the first time.

The `OrderPlaced` integration event was also confirmed to reach the real Service Bus topic: its
outbox row shows `ProcessedOn` set with zero errors on the first attempt, once the Data Owner
grant above was in place.

## What's confirmed vs. what's still open (not glossed over)

**Confirmed live, today:** the Container App runs the real image; API → Azure SQL (managed
identity, schema created, contained user granted) works end to end for placing an order;
API → Service Bus (managed identity, RBAC granted) successfully **sends** the resulting
`OrderPlaced` event.

**Not yet confirmed:** the downstream saga — Inventory reserving stock and the order actually
reaching `Confirmed`/`PaymentReceived`/`Shipped` — did not complete for any of the three test
orders placed today; all three remained `Pending`. Every Service Bus subscription showed zero
active and zero dead-lettered messages afterward (i.e. something completed the messages without
visible error), and Application Insights returned no exceptions and no telemetry at all for this
window — genuinely inconclusive with the time available today, not something papered over. This
is precisely Day 25's still-open "`ServiceBusSubscriptionProcessor` never exercised against a live
namespace" gap, now underway rather than theoretical, and squarely what Day 28's build plan
already scheduled as Day 31 ("Real Service Bus round-trip"). Today's session ran out of runway to
fully root-cause it; the concrete lead for next time is `ServiceBusSubscriptionProcessor.OnMessageAsync`
completing a message even when `GetServices(handlerType)` returns zero handlers, which would
explain messages disappearing with no error and no effect.

## Commit log for the day

See the branch's own commits — grant-managed-identity.sql fix, Dockerfile + ICU fix, the
AcrPull-deadlock fix, and the ASPNETCORE_ENVIRONMENT fix are each their own small commit rather
than one large one, so each stands on its own and matches exactly one of the findings above.

## What did I learn this session?

That a stuck deployment doesn't just block the one resource that's visibly `Failed` — it silently
prevents everything *downstream* of it from ever being attempted at all. Both AcrPull and the
Service Bus RBAC grant were victims of the exact same shape of bug, and the second one was
invisible until the first was fixed, because nothing had ever gotten far enough to reach it. The
concrete lesson: when a deployment has been stuck for a long time, check what *else* in the
template depends on the stuck resource's output before assuming fixing the one obvious failure is
the whole fix.

## What would break this?

Everything in "not yet confirmed" above. And more specifically: if the eventual root cause of the
stalled saga turns out to be `ServiceBusSubscriptionProcessor` silently completing messages it
never actually handled (the lead above), then every integration event this capstone has sent to a
real Service Bus topic today may have been silently dropped rather than processed — which would
mean the "zero errors" seen in Application Insights and the container logs is not evidence of
success, it's an artifact of a bug that swallows failures without a trace. That distinction is
exactly what Day 31 needs to resolve before this saga can be trusted against real infrastructure.
