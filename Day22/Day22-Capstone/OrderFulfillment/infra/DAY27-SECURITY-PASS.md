# Day 27 — Security pass (OrderFulfillment capstone)

STRIDE-lite threat model, the private-endpoint change, OpenAPI hardening, and a security-check
pass against the actual hardened API running live — in that order, because the threat model is
what the rest of this pass responds to, not a document written after the fact to match what got
built. Every finding below, including the ones that didn't go as planned, is what actually
happened this session, not a tidied-up retelling.

## STRIDE-lite threat model

Scoped to the real components this capstone actually has by Day 26: a Container App (`orderfulfillment-api-*`,
system-assigned identity), Azure SQL (Entra-ID-only), a Service Bus topic + 5 subscriptions, Entra
ID JWT auth on `/api/v1/orders`, and the outbox/worker pipeline. Each row is a concrete scenario in
*this* system, not a generic checklist entry.

| # | Category | Concrete threat here | Mitigation already in place | Residual risk / gap |
|---|---|---|---|---|
| 1 | **S**poofing | A caller forges a JWT claiming to be a legitimate Entra ID user | JWT signature validated against Entra's real signing keys (`options.Authority`), issuer + audience pinned (`ValidIssuers`/`ValidAudience` in Program.cs) | No real Entra App Registration exists yet (Day 25's known gap) — auth code is exercised in shape only, not against a live token |
| 2 | **T**ampering | A client mutates `unitPrice` to submit a near-free order | `Money.Of` rejects negative amounts; nothing stops an *inflated-quantity* variant beyond `PlaceOrderValidator.MaxLines` | Price is client-supplied at all — a real system prices server-side from a catalog, never trusts the caller's `unitPrice` |
| 3 | **T**ampering | A message on the Service Bus topic is altered in transit or by another subscriber | TLS 1.2 minimum on the namespace (`servicebus.bicep`); `disableLocalAuth` means no shared key ever leaks for a forged publish | Nothing authenticates *which module* published a given message — any identity holding Data Owner (see #6) can publish under any EventType |
| 4 | **R**epudiation | "I never placed that order" / "Inventory never actually failed that reservation" | Every `OutboxMessage`/`DeadLetterMessage` row is a durable, timestamped record; Day 26's `TraceParent` means the full causal chain is reconstructable from Application Insights | No audit log survives independently of the app's own database — an attacker with SQL-level access could edit history, not just query it |
| 5 | **I**nformation Disclosure | The `/openapi/v1.json` surface, a 500 error, or an ordinary response header leaks internal shape (stack traces, EF Core SQL, which web server is running) | `UseExceptionHandler` returns a flat `{ error }` shape, never the raw exception; `Server: Kestrel` was found leaking on *every* response (see the security-check section below) and is now suppressed | Nothing yet inspects response bodies for accidental leakage the way this pass checked headers — the same manual method below, extended, would be the next pass |
| 6 | **D**enial of Service | A client submits an order with 1,000,000 lines, or a 50MB string as a SKU | **New this pass**: `PlaceOrderValidator` caps lines at 100 and SKU shape at 40 ASCII chars (Span-based, zero-allocation check); Kestrel's `MaxRequestBodySize` capped at 64 KB; a fixed-window rate limiter (20 req/10s) on `/api/v1/orders` | The rate limiter is per-process, in-memory — multiple Container App replicas each get their own 20/10s budget, not a shared one; a distributed limiter (Redis-backed) would be needed for real horizontal scale |
| 7 | **E**levation of Privilege | The API's managed identity is scoped too broadly and a compromised container reaches more than it should | Every RBAC grant (`servicebus-access.bicep`, `api.bicep`'s AcrPull) is scoped to one specific resource, never the resource group — see Day 23/24's DESIGN.md | `Azure Service Bus Data Owner` (not the narrower Sender/Receiver split) is still broader than strictly needed — a known, previously-documented gap, unchanged this pass |

## The private-endpoint change

**New:** `infra/modules/network.bicep` — a VNet (`10.20.0.0/16`) with a dedicated
`private-endpoints` subnet (`10.20.1.0/24`, `privateEndpointNetworkPolicies: 'Disabled'`, as
private endpoints require), plus two Private DNS Zones (`privatelink.database.windows.net`,
`privatelink.servicebus.windows.net`) linked to that VNet so `*.database.windows.net`/
`*.servicebus.windows.net` names resolve to private IPs *from inside the VNet* instead of their
public ones.

**Changed:** `modules/sql.bicep` and `modules/servicebus.bicep` both gained a
`publicNetworkAccess`-style toggle (`allowPublicNetworkAccess`, default `false` now) and a
`Microsoft.Network/privateEndpoints` resource wired to the new subnet, with a
`privateDnsZoneGroup` pointing at the matching zone above — this is what actually makes a private
IP register itself as an A record without a manual step.

### Two real deployment failures, both real findings

`az deployment sub validate` passed cleanly first — and, exactly like Day 24's lesson, `validate`
turned out not to be the whole story. A real deployment of the network + SQL module (Container App
skipped — see below) surfaced two things validate never caught:

**1. The SQL firewall rule can't coexist with a disabled public endpoint.** `sql.bicep`'s
`allowAzureServicesRule` was still unconditionally trying to create a firewall rule even with
`publicNetworkAccess: 'Disabled'`:

```
DenyPublicEndpointEnabled: Unable to create or modify firewall rules when public network
interface for the server is disabled. To manage server or database level firewall rules,
please enable the public network interface.
```

Fixed by gating the rule on `allowAzureServices && allowPublicNetworkAccess` — a firewall rule is
meaningless once there's no public listener left for it to filter.

**2. Service Bus private endpoints require the Premium tier — Standard cannot have one at all.**

```
PrivateEndpointInvalidSku: Call to Microsoft.ServiceBus/namespaces failed. Error message:
Private endpoint connections are only supported on Premium Service Bus namespaces.
```

This is a genuine, previously-undocumented system-design trade-off, not a bug to fix: this
namespace is `Standard` specifically because Basic can't do topics/subscriptions at all (Day 23),
and Premium (dedicated messaging units, priced per-unit rather than per-operation — roughly two
orders of magnitude more than Standard at this namespace's volume) is a large, deliberate cost the
capstone isn't paying just to privately connect to a namespace nothing is using at production
volume yet. The Bicep for Service Bus private connectivity is written and correct (it's the exact
same pattern as SQL's), gated behind the same `allowPublicNetworkAccess` toggle, and simply isn't
exercised in dev — upgrading to Premium is the one-parameter change a real production rollout would
make, at the point it's actually worth the cost.

**What was verified for real** (network + SQL only — Container App skipped for the reason below):

```bash
az deployment sub create --template-file _day27-pe-verify.bicep \
  --parameters aadAdminLogin=... aadAdminObjectId=...
# -> Succeeded, after the firewall-rule fix above. VNet + subnet + both DNS zones + SQL private
#    endpoint + DNS zone group all created for real, in this subscription.
```

**The actual proof, queried after deployment — this is what "private endpoint" means, concretely:**

```bash
az network private-endpoint list -g orderfulfillment-rg-dev \
  --query "[].{name:name, connectionState:privateLinkServiceConnections[0].privateLinkServiceConnectionState.status}"
# name                                        connectionState
# orderfulfillment-sql-dev-wq5mb2ijh5gfu-pe   Approved

az network private-dns record-set a list -g orderfulfillment-rg-dev -z privatelink.database.windows.net \
  --query "[].{name:name, ip:aRecords[0].ipv4Address}"
# name                                     ip
# orderfulfillment-sql-dev-wq5mb2ijh5gfu   10.20.1.4
```

`orderfulfillment-sql-dev-wq5mb2ijh5gfu.database.windows.net` — the exact FQDN
`sqlConnectionStringNoCredentials` builds — now resolves to `10.20.1.4`, an address inside this
VNet's own `10.20.1.0/24` private-endpoints subnet, for anything resolving it from inside that
VNet. That's the real mechanism this pass set out to prove, independent of whether the Container
App itself can reach it yet.

**The gap, stated plainly, not hidden:** proving a Container App can *reach* SQL through this
private endpoint requires that Container App's own environment to be VNet-injected — and this
capstone's Container App runs in `thinkschool-env` (QuotesApi's environment, shared because of
Day 24's one-environment-per-region subscription cap), which is **not** VNet-integrated and was
deliberately never modified to become so, to keep this pass from touching QuotesApi's resources.
Everything above that one line is real, deployed infrastructure, verified against the live
subscription; that line is an honest, load-bearing limitation this session didn't have the room to
lift without violating the "don't touch QuotesApi's resources" constraint every prior day in this
capstone has also respected.

## OpenAPI hardening

| Change | File | Why |
|---|---|---|
| `/api/v1/orders`, not `/api/orders` | `Program.cs` | URL-segment versioning — one route-group prefix is enough for one version, no consumer yet; a full versioning package's content-negotiation machinery isn't needed at this stage |
| `PlaceOrderValidator` (line count, SKU shape) | `Ordering.Application/PlaceOrderValidator.cs` | Resource-consumption limits domain invariants deliberately don't cover (STRIDE row 6) |
| Kestrel `MaxRequestBodySize = 64 KB` | `Program.cs` | A ceiling under the app-level validator, so an oversized body never even reaches JSON deserialization |
| Fixed-window rate limiter, 20 req/10s | `Program.cs` | Same DoS category, different layer — caps request *rate*, not just request *shape* |
| Fail-closed auth outside Development | `Program.cs` | Closes the fail-open gap Day 25 explicitly flagged and left open — a misconfigured non-dev deploy now refuses to start instead of silently serving every endpoint unauthenticated |
| `X-Content-Type-Options`, `Referrer-Policy`, `Content-Security-Policy` response headers | `Program.cs` | Found missing and fixed via a real header inspection against the live app — see the security-check section below |
| `AddServerHeader = false` | `Program.cs` | Same inspection: every response was disclosing `Server: Kestrel` for free |
| `/openapi/v1.json` published (`Microsoft.AspNetCore.OpenApi`) | `OrderFulfillment.Api.csproj`, `Program.cs` | There has to be a real OpenAPI surface before "harden the OpenAPI surface" means anything concrete |
| `Microsoft.OpenApi` pinned to 2.7.5 | `OrderFulfillment.Api.csproj` | `dotnet build` itself flagged NU1903: the OpenAPI package this pass just added transitively pulled a version with a real, named CVE — see below |
| `OutboxProcessor` takes `IServiceScopeFactory`, not `IEnumerable<IOutboxStore>` | `BuildingBlocks.Infrastructure/OutboxProcessor.cs` | A real, pre-existing DI-lifetime bug this pass's own testing surfaced — see below |

### The vulnerable dependency this pass introduced and then fixed

Adding `Microsoft.AspNetCore.OpenApi` 10.0.10 (needed for the line above) transitively pulled
`Microsoft.OpenApi` 2.0.0, which `dotnet build`'s own NU1903 advisory immediately flagged:
**CVE-2026-49451 / GHSA-v5pm-xwqc-g5wc**, CVSS 7.5 — a circular OpenAPI schema reference can crash
the process via stack overflow (a denial-of-service, squarely STRIDE row 6). Fixed in 2.7.5+.
Pinned `Microsoft.OpenApi` to `2.7.5` directly in `OrderFulfillment.Api.csproj` to override the
transitive version. Found by the build's own tooling, not by ZAP — worth noting precisely because
it means a security pass isn't just "run a scanner once," it's also "read what your own build
already tells you."

## The OWASP ZAP attempt — and why it became a manual check instead

Docker (`docker ps` confirmed the daemon was running) pulled `ghcr.io/zaproxy/zaproxy:stable` —
several hundred MB into the pull, the dev machine's disk (already tight from Days 22–26's real
Azure work) hit **zero bytes free**, and the pull failed mid-write:

```
docker: failed to copy: failed to send write: ... read-only file system
```

Worse: Docker's own WSL2 backend had its metadata database corrupted by the out-of-space write and
came back reporting every command as `500 Internal Server Error` until `wsl --shutdown` + a full
Docker Desktop restart recovered it. A second attempt (the much smaller `zaproxy:bare` image, ~500
MB) pulled successfully — but `bare` turned out not to ship `zap-baseline.py` at all (it's a
minimal base image meant for building custom images from, not for running the baseline scan
directly). Removing `bare` to make room for the full `stable` image made things *worse*, not
better: Docker's WSL virtual disk (`docker_data.vhdx`) had grown to 3.2 GB and, being a dynamically-
expanding VHD, does not shrink when data inside it is deleted — host disk stayed critically low
regardless of what was removed from inside the image store. Recovered ~1.2 GB by shutting down
WSL and running `diskpart`'s `compact vdisk` against the file directly — a real fix, but not one
worth risking a second time by re-attempting the `stable` pull on the same fragile disk.

**Decision made here, out loud:** stop trying to run ZAP itself, and instead perform ZAP baseline's
own core passive checks manually, directly against the same real, running, hardened instance —
which is what a baseline scan actually *is*: passive inspection of real HTTP responses, no active
attack payloads. This is a genuine substitute for the specific checks below, not a substitute for
everything a full crawl-plus-passive-scan would eventually find.

```bash
curl -sI http://localhost:5266/nonexistent        # baseline check: response headers
curl -si -X POST http://localhost:5266/api/v1/orders -H "Content-Type: application/json" -d '...'
```

**Findings and what was fixed — all against the real running instance, before and after:**

| Check (this is what ZAP's baseline passively inspects) | Before | After |
|---|---|---|
| `X-Content-Type-Options` present | Missing | `nosniff` |
| `Content-Security-Policy` present | Missing | `default-src 'none'; frame-ancestors 'none'` |
| `Referrer-Policy` present | Missing | `no-referrer` |
| `Server` header discloses the web server | `Server: Kestrel` on every response | Header absent (`AddServerHeader = false`) |
| Rate limiting on repeated identical requests | N/A — added this pass, not found missing | Verified for real: 25 rapid `POST /api/v1/orders` calls → 20× `201`, then 5× `429` |

## The real bug this pass's own testing surfaced (not a ZAP finding — a DI-lifetime bug)

Day 27's fail-closed auth change (above) meant testing locally now requires
`ASPNETCORE_ENVIRONMENT=Development` to bypass it without a real Entra App Registration — and
Development is the one environment where ASP.NET Core validates DI scopes at container-build time
by default. The app failed to start at all:

```
Cannot consume scoped service 'BuildingBlocks.Infrastructure.IOutboxStore' from singleton
'Microsoft.Extensions.Hosting.IHostedService'.
```

`OutboxProcessor` (a singleton, registered via `AddHostedService`) had been constructor-injecting
`IEnumerable<IOutboxStore>` directly since Day 22 — and every `IOutboxStore` implementation (e.g.
`OrderingOutboxStore`) is Scoped, because it holds a `DbContext`. This had been silently wrong the
entire time; it only ever "worked" because Production mode (the effective default whenever
`ASPNETCORE_ENVIRONMENT` isn't explicitly set) doesn't validate scopes, so nothing ever complained
— but the *actual runtime behavior* was a single `DbContext` instance implicitly shared for the
process's entire lifetime across every poll cycle, which is not what EF Core's `DbContext` is
designed to tolerate. Fixed by injecting `IServiceScopeFactory` instead and creating a fresh scope
once per poll cycle (`OutboxProcessor.cs`) — `IMessageBus` and `ILogger` stay constructor-injected,
since both are Singleton-safe.

## What did I learn this session?

Two things, both about *not trusting a tool's absence of complaint as a sign of correctness*: first,
that `Microsoft.OpenApi`'s CVE wasn't hunted for — it was surfaced by `dotnet build`'s own NU1903
warning the moment the OpenAPI package was added for an unrelated reason. Second, and more
pointedly, that the `OutboxProcessor` DI bug had been sitting in this codebase since Day 22, through
five days of real deployments and real testing, because nothing had ever run it in the one mode
that actually validates DI scopes. Neither finding came from the tool this pass set out to run
(ZAP) — both came from paying attention to what the compiler and the runtime were already saying.

## What would break this?

The rate limiter is per-process: with `minReplicas` above 1 (as prod's own `bicepparam` already
sets), each replica enforces its own 20-req/10s budget independently — an attacker distributing
requests across replicas gets a multiple of the intended limit, not the limit itself. And the ZAP
substitution above is real but partial: it covers the specific passive header checks this pass
happened to reason through, not the full set of rules an actual `zap-baseline.py` crawl would apply
(cookie security attributes, CORS misconfiguration probing, TLS-specific checks that don't even
apply over this local HTTP-only test) — the next session with a less disk-constrained machine
should still run the real tool.
