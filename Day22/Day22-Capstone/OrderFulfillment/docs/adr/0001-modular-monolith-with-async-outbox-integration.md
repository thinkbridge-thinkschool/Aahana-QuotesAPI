# ADR 0001: Modular monolith, integrated only through async events over a transactional outbox

## Status

Accepted (Day 22), revisited with six days of real consequences on the table (Day 28).

## Context

`OrderFulfillment` needs five things to cooperate on one business process — placing an order,
reserving stock, capturing payment, shipping, notifying the customer — without any of them being
allowed to corrupt another's invariants. Two axes had to be decided together, because they
reinforce each other:

1. **Deployment topology.** One process, or several independently-deployable services?
2. **Communication style.** Do modules call each other directly (in-process method calls, or
   network calls if separate services), or only react to facts published about each other's state?

This is a training capstone with a two-person effective team (one engineer, one reviewing
session), a subscription with real constraints already discovered the hard way (Day 24: one
Container Apps environment per region, full stop), and a deadline measured in days, not quarters.

## Decision

**Modular monolith, integrated only through asynchronous integration events written to a
transactional outbox.** One deployable process (`OrderFulfillment.Api`). Five modules
(`Ordering`, `Inventory`, `Payments`, `Shipping`, `Notifications`), each internally Clean/Onion
layered (Domain → Application → Infrastructure) with the dependency rule enforced by project
references. No module's Domain or Application project is ever referenced from outside that
module. The *only* thing one module can do to another is publish a fact — `OrderPlaced`,
`StockReserved`, `PaymentCaptured`, … — into its own outbox table in the same DB transaction as
the state change that caused it. A background `OutboxProcessor` (in-process bus locally, a real
Azure Service Bus topic with one filtered subscription per module when deployed) is what
eventually gets that fact to whoever reacts to it. No module ever calls another module's
Application or Domain code directly, in-process or otherwise.

## Alternatives considered

**A — Microservices from day one.** Five separate deployable services, five separate databases, a
real message broker between physically distinct processes from the start. Rejected: Day 24 proved
this subscription tier allows exactly **one** Container Apps environment per region — a
constraint that would have blocked even *two* independently-deployed services, let alone five,
without first solving a hosting problem that has nothing to do with the actual domain. Paying the
network-partition/independent-deployment tax before the domain boundaries have been proven right
even once would have been solving the wrong problem first.

**B — A single unstructured monolith.** One project, no module boundaries, direct method calls or
a shared `DbContext` across what should be separate concerns. Rejected outright — this is the
"distributed monolith" failure mode in reverse: not distributed, but also not actually modular,
where `Order.Place()` could reach into `Stock` directly and nothing would stop `Inventory`'s
invariants from being violated by code that has no business touching them.

**C — Modular monolith with *synchronous* in-process calls between modules.** Keep the module
boundaries, but let `Ordering.Application` call `IStockRepository` (or an `IInventoryService`)
directly instead of going through an event. This was the closest real alternative, and the one
most likely to have been reached for by default — it's less code, no outbox, no polling, no
eventual consistency to reason about. Rejected because it silently reintroduces the coupling the
module boundaries exist to prevent: a synchronous call from `Ordering` into `Inventory` makes
`Ordering`'s transaction depend on `Inventory`'s success, which is exactly the kind of
cross-module transaction the aggregate-boundary design (`Order` and `Stock` as separate
consistency boundaries — see DESIGN.md) was chosen to avoid. It would also have made a later
extraction into real services a rewrite instead of an infrastructure swap.

## Consequences

**What this bought, proven, not asserted:**
- Every cross-module reaction survives a crash between "the DB transaction committed" and "the
  rest of the system found out," because the outbox row commits atomically with the aggregate
  change (Day 22's DESIGN.md; exercised live — a placed order cascades through Confirm → Payment →
  Ship, and a failed stock reservation correctly compensates with a cancel, not a stuck order).
- The extraction seam to real services already exists and was exercised for real: swapping
  `InProcessMessageBus` for `AzureServiceBusMessageBus` (Day 25) required zero changes to any
  module's Domain or Application code — only a new Infrastructure-layer class and composition-root
  wiring. That is the concrete payoff of Alternative C's rejection.
- Deployability stayed simple enough to actually attempt (Day 24) without first having to solve a
  five-service hosting problem — even though what it did surface was a different, real hosting
  constraint (see below).

**What this cost, also proven, not hypothetical:**
- **A real DI bug, Day 22.** Shared cross-cutting interfaces (`IUnitOfWork`, `IOutboxWriter`,
  `IIntegrationEventPublisher`), each module registering its own implementation against the same
  interface type, collided under .NET's "last registration wins" resolution — every module
  silently got the last-registered module's implementation, and `Order.Place()` never actually
  persisted. This is a cost specific to *this* topology: five modules sharing one DI container is
  what created the collision surface; five separate processes each with their own container
  couldn't have had this bug.
- **Distributed-trace stitching became deliberate work, not automatic, Day 26.**
  `OutboxProcessor` runs on its own timer with no HTTP context, so a trace has to be carried across
  that gap by hand (`TraceParent` stored on the outbox row, replayed as the parent context when
  processed) or every async hop starts a disconnected trace. A synchronous call graph gets this
  for free from the framework; this design paid for it in application code.
- **A new authentication gap the design itself introduced, Day 27 (STRIDE row 3).** Nothing on the
  Service Bus topic authenticates *which module* published a given event — any identity holding
  the (already-too-broad) `Data Owner` grant can publish under any `EventType`, and every
  subscription's filter trusts that property unconditionally. A synchronous call has an implicit
  caller identity for free; a shared pub/sub topic does not, and this design hasn't paid for one
  yet (see the Day 28 build plan).
- **Eventual consistency is a real, user-visible property now**, not a detail: an order sits in
  `Pending` for up to one `OutboxProcessor` poll interval (5s) before `Inventory` even sees it, and
  every downstream module's reaction lags by up to another poll interval — multiplied across four
  hops end-to-end. A synchronous design would have made the whole saga complete inside one HTTP
  request.
- **At-least-once delivery means every reaction handler must be idempotent — and, until Day 28,
  most of them weren't.** An independent design review (see `../../DAY28-DESIGN-REVIEW.md`) found
  that `Order`'s own state-transition methods threw on redelivery of an already-applied event
  instead of no-op'ing, which is guaranteed to eventually dead-letter valid orders under ordinary
  Service Bus semantics. This is the sharpest, least optional cost of choosing at-least-once async
  delivery over a synchronous call: a synchronous call either fully happens or the whole request
  fails — it never gets "redelivered" days later against state that has since moved on. Fixed the
  same day it was found; see the review doc for the fix and the tests that pin it down.

None of these costs are hypothetical or discovered by inspection — every one of them is a bug or a
gap this capstone actually hit and then had to name, root-cause, and fix or document, across Days
22, 26, 27, and 28. That track record is what makes this ADR worth writing *now*, with the evidence
in hand, rather than as a Day-1 prediction.
