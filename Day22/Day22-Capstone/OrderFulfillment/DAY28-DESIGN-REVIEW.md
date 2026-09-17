# Day 28 — Design review + ADR (OrderFulfillment capstone)

The ADR for the decision everything else in this capstone traces back to lives at
[`docs/adr/0001-modular-monolith-with-async-outbox-integration.md`](docs/adr/0001-modular-monolith-with-async-outbox-integration.md) —
written now, with six days of real consequences on the table, not as a Day-1 prediction. This
document is the review that produced it: an independent critique, and the build plan that critique
changed.

## The review

No live mentor/peer was available this session, so the critique was gotten the way the rest of
this capstone gets verified — for real, not simulated: a fresh reviewer agent with no prior context
on this project, pointed only at the six existing design docs and the actual source for the core
mechanism (`Order.cs`, `OutboxProcessor.cs`, the message bus implementations, every module's DI
registration, `Program.cs`), asked to find real problems the way a demanding staff engineer would
in an actual design-review meeting — not to restate what the docs already say about themselves.

The review ranked four real findings. In order:

1. **(Blocking) Ordering's own reaction handlers aren't idempotent against outbox redelivery —
   ordinary at-least-once delivery permanently dead-letters legitimate orders.** `Order.Confirm()`,
   `MarkPaymentReceived()`, `MarkShipped()`, and `Cancel()` all threw `DomainInvariantException` if
   called when the order wasn't in the exact expected prior state. `Notifications` was built with
   an explicit idempotency log (`NotificationRecord`) for exactly this reason — but `Ordering`'s own
   handlers, reacting to `StockReserved`/`PaymentCaptured`/`ShipmentDispatched`, had no equivalent
   guard. DESIGN.md itself says outbox delivery is "at-least-once, not exactly-once" — so this isn't
   a rare edge case, it's guaranteed under ordinary Service Bus semantics (a consumer that crashes
   after processing but before acknowledging causes redelivery of the same message). When it
   happens, the second delivery hits a status check that no longer matches, throws, retries five
   times, and dead-letters a perfectly valid order — permanently stalling it with no operator alarm
   beyond a DLQ row.
2. In-memory `Inventory`/`Payments`/`Shipping` state is incompatible with prod's own
   `minReplicas: 2`/`maxReplicas: 10` — two orders for the same SKU routed to different replicas can
   both see full stock and both reserve it, silently defeating the one invariant `Stock` exists to
   protect, and a replica recycle during ordinary autoscaling discards any unpublished in-memory
   outbox rows with zero durable trace.
3. `OrderingOutboxStore.GetUnprocessedAsync` has no claim/lock semantics (no `UPDLOCK`, no "claimed
   by" column) — once Ordering's outbox is shared Azure SQL across 2+ replicas, two
   `OutboxProcessor` instances polling concurrently will fetch overlapping batches and both publish
   the same message, directly feeding finding #1.
4. No optimistic concurrency token on `Order` — combined with #3, two processors racing on the same
   `Order` row produce a silent last-write-wins overwrite via EF Core rather than a
   `DbUpdateConcurrencyException`, so a state transition can be lost with nothing to dead-letter and
   nothing logged.

**Verdict, in the reviewer's own words:** "#1 is what I'd block on. It's not a scaling edge case —
it's the designed at-least-once contract meeting handlers that were never made idempotent,
guaranteed to eventually quarantine valid orders in production with no alarm firing." None of these
were already stated anywhere in the six existing design docs — #1 in particular directly
contradicts DESIGN.md's own "What's built vs. scaffolded" section, which describes `Notifications`'
idempotency handling as if it were the *general* pattern this capstone follows, when it was in fact
the *only* module that got it.

## How the top critique changed the design

Fixed today, not filed for later — this is a small, well-scoped correctness fix, not a redesign,
and every other real bug this capstone has found (Day 22's DI collision, Day 27's DI-lifetime bug)
got fixed the same session it was found. `Order.Confirm()`, `MarkPaymentReceived()`, `MarkShipped()`,
and `Cancel()` (`src/Modules/Ordering/Ordering.Domain/Order.cs`) now treat "the target state is
already reached, or the order has already moved past it" as a no-op success instead of a thrown
exception — redelivery of the *same* event becomes idempotent, while a genuine conflict (e.g.
`Confirm()` called on an order some other event already `Cancel()`led) still throws and still
dead-letters, exactly as before. `OrderTests.cs` gained five new/changed tests reproducing the
precise redelivery scenario the review described — including
`Confirm_WhenAlreadyConfirmed_IsIdempotentNoOp`, which used to assert the *old, buggy* behavior
(`Confirm_WhenAlreadyConfirmed_Throws`) and now asserts the fix. All 11 tests pass; the full
solution still builds with 0 warnings.

Findings #2–#4 are real but not same-day fixable without either standing up real Azure SQL for
every module (findings #2, #3) or picking a concurrency strategy that needs to be designed, not
patched (finding #4) — both real infrastructure/design decisions, not a five-line change. They're
folded into the build plan below (Day 33) rather than deferred silently.

## Day-by-day build plan

Not a retrospective — everything below is genuinely still open. It's the "Known gaps (not hidden)"
sections scattered across all six existing design docs, pulled into one prioritized sequence,
ordered so each day's exit criterion is something the next day can build on rather than re-derive.
Every day's goal is a live, verified fact against the real subscription — matching how every prior
day in this capstone was actually done — not a code change alone.

| Day | Goal | Closes | Exit criterion |
|---|---|---|---|
| **29** | Get a real Container App running | Day 24's unresolved provisioning failure; no Dockerfile exists yet anywhere in this repo | Root-cause the cross-resource-group environment-join failure (test the one hypothesis Day 24 explicitly left untested), write the Dockerfile, build + push a real image, get one successful live revision. `curl` against a real FQDN returns `200`. |
| **30** | Real identity, not code review | Day 25's two "verified by code review, not a live round-trip" gaps | Register a real Entra ID App Registration; get a real token and confirm `RequireAuthorization()` actually rejects/accepts correctly. Run `grant-managed-identity.sql` against the live SQL server and place a real order that lands in Azure SQL, not SQLite. |
| **31** | Real Service Bus round-trip | Day 25's `ServiceBusSubscriptionProcessor` — never exercised live | Place an order against the deployed API and confirm, from Application Insights (Day 26's trace-stitching machinery already exists for this), that all five subscriptions received exactly the events their `EventType` filter says they should — over the real topic, not `InProcessMessageBus`. |
| **32** | Close the publisher-spoofing gap | ADR 0001's own worst cost: Day 27 STRIDE row 3 — nothing authenticates which module published a given event | Split the single `Azure Service Bus Data Owner` grant into per-module Sender/Receiver roles (`servicebus-access.bicep`), so a compromised module's identity can no longer publish under another module's `EventType` or read a subscription it doesn't own. Verify by attempting a cross-module publish and confirming it's now denied. |
| **33** | Make it survive real prod load — and real multi-replica outbox contention | Day 27's rate limiter gap; `Inventory`/`Payments`/`Shipping` still in-memory (Day 28 review finding #2); no outbox claim/lock semantics (finding #3); no optimistic concurrency on `Order` (finding #4) — all four are the same underlying fact, that prod's own `bicepparam` sets `minReplicas: 2`, restated four different ways | Swap the rate limiter for a shared store; give the three in-memory modules real EF Core persistence; add claim/lock semantics to `GetUnprocessedAsync` (or a "claimed by/until" column) so two `OutboxProcessor` instances can't double-publish the same row; add a `RowVersion` to `Order` so a lost concurrent update throws instead of silently vanishing. Verify by actually running two API replicas against shared Azure SQL and confirming no order is ever double-processed or silently overwritten. |
| **34** | Close the data-integrity gaps | EF Core migrations still `EnsureCreated()`; no idempotency key on `POST /api/orders`; client-supplied `unitPrice` (Day 27 STRIDE row 2) | Add a real migration; add an idempotency key so a retried request can't double-place an order; move pricing server-side from a catalog lookup instead of trusting the caller. |
| **35** | Finish the security pass Day 27 couldn't | The real OWASP ZAP baseline scan (blocked by disk space, substituted with manual checks); the private endpoints built and proven in Day 27 but unreachable because `thinkschool-env` isn't VNet-integrated | Run the actual `zap-baseline.py` crawl on a machine with headroom. VNet-integrate the Container Apps environment so Day 27's already-deployed SQL/Service Bus private endpoints stop being provisioned-but-inert. |

## What did I learn this session?

That an ADR written *after* the decision has produced six days of real, named consequences is a
fundamentally different document than one written before — every "consequence" section above cites
an actual bug (Day 22's DI collision), an actual gap (Day 26's manual trace-stitching), or an
actual STRIDE finding (Day 27's publisher-spoofing gap) instead of a guess about what might happen.
The review agent's top finding (above) is exactly the kind of thing that's easy to miss from
inside the six days that built the thing — a fresh, uninvested reader of the same docs found it
faster than another pass by the person who wrote them would have.

## What would break this?

The build plan above is itself sequenced on an assumption Day 24 never got to test: that Day 29's
root-cause fix for the Container App provisioning failure actually has one. If the real constraint
turns out to be "one environment, one resource group, forever" (Day 24's own closing line), Day 29
doesn't reduce to a bug fix — it forces a real topology decision (share QuotesApi's resource group,
or accept a second Container Apps environment isn't available on this subscription tier at all),
and every day after it in this plan is downstream of which way that goes.
