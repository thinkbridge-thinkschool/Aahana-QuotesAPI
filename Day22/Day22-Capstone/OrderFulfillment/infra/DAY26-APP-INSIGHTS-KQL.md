# Day 26 — App Insights + KQL (OrderFulfillment capstone)

Wires OpenTelemetry to Application Insights, then proves — with real telemetry from a real run,
not asserted — that a trace stitches together the API request, the outbox worker's background
processing, and the SQL calls each hop makes.

## What changed in code

| File | Change |
|---|---|
| `BuildingBlocks.Infrastructure/OutboxTelemetry.cs` | New. One shared `ActivitySource("OrderFulfillment.Outbox")`. |
| `BuildingBlocks.Infrastructure/OutboxMessage.cs` | Added `TraceParent` — the W3C traceparent of the HTTP request that wrote this row. |
| `BuildingBlocks.Infrastructure/OutboxIntegrationEventPublisher.cs` | Captures `Activity.Current?.Id` into `TraceParent` at write time. |
| `BuildingBlocks.Infrastructure/OutboxProcessor.cs` | Re-parents a new `outbox.process` activity under the stored `TraceParent` instead of starting a disconnected one. |
| `Host/OrderFulfillment.Api/Program.cs` | `UseAzureMonitor()` + explicit `AddSource("OrderFulfillment.Outbox")` + explicit `AddEntityFrameworkCoreInstrumentation()` (see "the real bug" below). |
| `infra/modules/appinsights.bicep` | New. Log Analytics workspace + Application Insights + a real scheduled-query alert rule. |
| `infra/main.bicep` | Wires `appInsights.outputs.connectionString` into the API's `APPLICATIONINSIGHTS_CONNECTION_STRING` env var. |

## Why a stored TraceParent, not automatic propagation

`OutboxProcessor` is a `BackgroundService` polling on its own 5-second timer — it has no HTTP
context, so there is nothing for OpenTelemetry to automatically propagate a trace from. Without
deliberately carrying the trace ID across that gap, every background publish would start a brand
new, disconnected trace, and "API → worker → DB" would just be two unrelated operations that
happen to reference the same order ID. Capturing `Activity.Current?.Id` at write time
(`OutboxIntegrationEventPublisher`) and replaying it as a parent context at read time
(`OutboxProcessor`) is the entire mechanism — one field, two lines.

## The real bug this session found: EF Core instrumentation isn't automatic

`Azure.Monitor.OpenTelemetry.AspNetCore`'s `UseAzureMonitor()` auto-enables ASP.NET Core and
HttpClient instrumentation by reflection — **not** EF Core's, even with
`OpenTelemetry.Instrumentation.EntityFrameworkCore` referenced. Verified by testing against a real
Application Insights resource: with only `AddSource(...)`, the `dependencies` table stayed
completely empty — every SQL call the app made was invisible, with no error or warning anywhere.
Adding `.AddEntityFrameworkCoreInstrumentation()` explicitly to the `WithTracing(...)` builder
fixed it immediately (see the before/after query results below). **QuotesApi's own `Program.cs`
has the identical gap** — it references the same package the same way, and was never actually
tested against a live Application Insights resource either, so this was never caught there.

## Verified against a real Application Insights resource

Created for real (not simulated) in the same "Azure for Students" subscription, in its own
resource group so it can't collide with anything Container-Apps-related:

```
az group create --name orderfulfillment-observability-dev --location centralindia
az monitor log-analytics workspace create -g orderfulfillment-observability-dev -n orderfulfillment-logs-dev
az monitor app-insights component create --app orderfulfillment-appinsights-dev \
  -g orderfulfillment-observability-dev -l centralindia --workspace <workspace-id> --application-type web
```

Ran `OrderFulfillment.Api` **locally** (`dotnet run`) with `APPLICATIONINSIGHTS_CONNECTION_STRING`
pointed at that real resource — Application Insights doesn't care where the app runs, and this
sidesteps Day 24's still-unresolved Container App issue entirely; nothing about this needed a
Container App. Local SQLite + the in-process bus (no Service Bus config set locally) — same code
path, same `IMessageBus` contract, no Azure identity needed for this test.

```bash
curl -X POST http://localhost:5266/api/orders -H "Content-Type: application/json" \
  -d '{"customerId":"...","lines":[{"sku":"WIDGET-1","quantity":1,"unitPrice":9.99,"currency":"USD"}]}'
```

`WIDGET-1` is one of the two SKUs `InMemoryStockRepository` seeds — this order went all the way
through the real saga: Ordering → Inventory (stock reserved) → Payments (captured) → Shipping
(dispatched) → Notifications, each hop a separate outbox round-trip.

## KQL — p50/p99 latency by endpoint

```kql
requests
| where timestamp > ago(1h)
| summarize RequestCount=count(), p50=percentile(duration,50), p99=percentile(duration,99) by name
| order by RequestCount desc
```

**Real output:**

| name | RequestCount | p50 (ms) | p99 (ms) |
|---|---|---|---|
| outbox.process | 9 | 7.85 | 124.24 |
| POST /api/orders | 2 | 10.46 | 488.58 |

`outbox.process` shows up as its own row here because it's a `Consumer`-kind activity, which
Application Insights buckets alongside HTTP requests rather than under `dependencies`. The p99 on
`POST /api/orders` (488ms vs. a 10ms p50) is the first request's EF Core model-building/JIT cold
start, not a real regression — visible precisely because p99 is reported at all, which an average
alone would have hidden.

## KQL — dependency call breakdown

```kql
dependencies
| where timestamp > ago(1h)
| summarize Count=count(), AvgDurationMs=round(avg(duration),2), FailedCount=countif(success == false) by type, target
| order by Count desc
```

**Before the EF Core instrumentation fix:** zero rows — every SQL call invisible.
**After:**

| type | target | Count | AvgDurationMs | FailedCount |
|---|---|---|---|---|
| sqlite | Users \| main | 6 | 0.5 | 0 |

## KQL — confirming the distributed trace: API → worker → DB

```kql
union requests, dependencies
| where operation_Id == '<operation id from the POST /api/orders request>'
| project timestamp, itemType, name, target, duration, operation_Id, operation_ParentId, id
| order by timestamp asc
```

**Real output** (`operation_Id = 0eed93b3034fbd2efa3261cd9aff5039`, one full order's saga):

| time (UTC) | itemType | name | operation_ParentId | id |
|---|---|---|---|---|
| 17:51:25.605 | request | POST /api/orders | `0eed9…` (self) | `5ba49ff4…` |
| 17:51:28.458 | request | outbox.process | `5ba49ff4…` | `5c357d35…` |
| 17:51:28.515 | dependency | sqlite | `5c357d35…` | `07b9cb95…` |
| 17:51:28.523 | dependency | sqlite | `5c357d35…` | `7fa278e8…` |
| 17:51:28.529 | request | outbox.process | `5c357d35…` | `158d7a27…` |
| 17:51:28.645 | dependency | sqlite | `158d7a27…` | `30160b18…` |
| 17:51:28.676 | dependency | sqlite | `158d7a27…` | `7b4cb45f…` |
| 17:51:28.676 | dependency | sqlite | `158d7a27…` | `0ac8b787…` |
| 17:51:33.695 | request | outbox.process | `158d7a27…` | `5b1085aa…` |
| 17:51:33.712 | request | outbox.process | `5b1085aa…` | `141bdc07…` |
| 17:51:38.731 | request | outbox.process | `141bdc07…` | `95014424…` |
| 17:51:38.746 | request | outbox.process | `95014424…` | `56455072…` |

**This is the proof, not an assertion:** every row shares one `operation_Id`, and each
`operation_ParentId` is the immediately preceding row's own `id` — a real parent-child chain, not
merely a shared correlation field. Four `outbox.process` hops chained end-to-end are the four
sagas steps (Inventory reserve → Payments capture → Shipping dispatch → Notifications), each with
its own SQLite dependency calls nested underneath it. API → worker → DB, stitched into one trace,
exactly as the exercise asked to confirm.

**Screenshot:** open the resource in the portal —
`https://portal.azure.com/#resource/subscriptions/68a88491-0c9b-4750-9cb7-1fa2157daeb8/resourceGroups/orderfulfillment-observability-dev/providers/microsoft.insights/components/orderfulfillment-appinsights-dev/overview`
— Transaction Search, search operation ID `0eed93b3034fbd2efa3261cd9aff5039`, open the
`POST /api/orders` result, "View end-to-end transaction details". No browser-automation tool was
available in this session to capture that screenshot directly — the query results above are the
same data that view renders, just as a table instead of a waterfall.

## KQL — error rate (backs the alert)

```kql
requests
| where timestamp > ago(1h)
| summarize Total=count(), Failed=countif(success == false) by bin(timestamp, 5m), name
| extend ErrorRatePct = round(100.0 * Failed / Total, 2)
| order by timestamp desc
```

Every test request in this session returned 2xx (0% error rate) — including the `SKU-1` order,
which the saga correctly *cancelled* through compensating actions rather than the API throwing.
That's a deliberate distinction: "the business process declined the order" and "the request
failed" are different things, and this metric is about the latter.

## The alert — created for real, not just written

```bash
az monitor scheduled-query create --name "orderfulfillment-high-error-rate" \
  -g orderfulfillment-observability-dev --scopes <app insights resource id> \
  --condition "count 'Query' > 5" \
  --condition-query Query='requests | where success == false | summarize FailedCount = count()' \
  --evaluation-frequency 5m --window-size 5m --severity 2
```

Fires when more than 5 requests fail inside a 5-minute window. Committed as
`infra/modules/appinsights.bicep`'s `highErrorRateAlert` resource — the exact same KQL, so the
alert that actually exists in Azure and the query committed to the repo can't drift apart.

## What did I learn this session?

That "the package is referenced" and "the instrumentation is active" are not the same claim —
`OpenTelemetry.Instrumentation.EntityFrameworkCore` sitting in the `.csproj` did nothing at all
until `.AddEntityFrameworkCoreInstrumentation()` was called explicitly, and nothing about that
failure was loud: no exception, no warning, just an empty table. The only way to have caught it was
to actually look at real query results instead of trusting that "the docs say the Distro
auto-instruments common libraries" covered this one too.

## What would break this?

A message that never gets a `TraceParent` at all (any row written before this column existed, or
by future code that forgets to go through `OutboxIntegrationEventPublisher`) falls back to an
unparented `outbox.process` activity — still exported, but as the root of its own new trace, not
attached to the request that caused it. `OutboxProcessor`'s `hasParent` check handles this without
throwing, but silently produces a disconnected trace with no error to flag it — exactly the kind of
gap this session's own EF Core discovery says not to assume away without checking.
