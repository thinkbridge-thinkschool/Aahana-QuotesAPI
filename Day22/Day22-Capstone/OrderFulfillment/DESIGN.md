# Order Fulfillment — Capstone Design (Day 22)

A slice of an order-fulfillment platform, built as a **modular monolith**: one deployable process
(`OrderFulfillment.Api`), five independently-owned modules, each laid out as Clean/Onion
architecture (Domain → Application → Infrastructure), talking to each other only through
integration events over a transactional outbox. Never through a direct method call across a
module boundary, and never through a shared database table.

## Bounded contexts

| Module | Role | Owns |
|---|---|---|
| **Ordering** | Core domain — the reason this product exists | `Order` aggregate, order lifecycle |
| **Inventory** | Supporting — stock accuracy | `Stock` aggregate, reservations |
| **Payments** | Supporting — an anti-corruption layer over an external gateway | `Payment` aggregate |
| **Shipping** | Supporting — dispatch | `Shipment` aggregate |
| **Notifications** | Generic — purely reactive, no invariants of its own | delivery/idempotency log |

Each module is its own set of projects (`{Module}.Domain/Application/Infrastructure`) with the
Clean Architecture dependency rule enforced by project references: Domain depends on nothing;
Application depends only on Domain; Infrastructure depends on Application and Domain. Nothing
outside a module ever references its Domain or Application projects — the only thing another
module can see is the integration events it publishes (`BuildingBlocks.Application.IntegrationEvents`)
and, at the composition root, its `Add{Module}Module()` extension method.

## The core aggregate: `Order`

`Order` (`Modules/Ordering/Ordering.Domain/Order.cs`) is the aggregate the whole slice is
organized around. It owns the entire lifecycle — `Pending → Confirmed → PaymentReceived →
Shipped`, or `→ Cancelled` from any pre-Shipped state — and is the *only* thing allowed to change
it. Inventory, Payments, and Shipping never write to an Order directly; they publish facts about
their own aggregates (`StockReserved`, `PaymentCaptured`, `ShipmentDispatched`, …), and Ordering's
Application layer is what reacts and calls the one aggregate method that applies.

`OrderLine` and `Money` are value-shaped children local to the aggregate. Inventory's `Stock` is
a *separate* aggregate/consistency boundary, referenced from `Order` only by SKU string — never by
object reference — because "don't oversell a SKU" and "an order's lifecycle" are different
invariants with no reason to share a transaction.

## Async flows

Nothing outside Ordering calls back into it synchronously. Every cross-module reaction goes
through the same mechanism: **write the event to an outbox row in the same DB transaction as the
aggregate change → a background `OutboxProcessor` polls and publishes it → the subscribing
module's handler reacts, in its own unit of work.** This is what makes "the order was placed" and
"Inventory will eventually find out" atomic with respect to a crash, without needing a distributed
transaction.

```
POST /api/orders
   -> Order.Place()                (Pending)          --outbox--> OrderPlaced
        Inventory reserves stock                                  --outbox--> StockReserved | StockReservationFailed
   -> Order.Confirm() | Cancel()   (Confirmed)         --outbox--> OrderConfirmed | OrderCancelled
        Payments captures charge                                   --outbox--> PaymentCaptured | PaymentFailed
   -> Order.MarkPaymentReceived() | Cancel() (PaymentReceived)     --outbox--> OrderPaymentReceived | OrderCancelled
        Shipping dispatches                                        --outbox--> ShipmentDispatched
   -> Order.MarkShipped()          (Shipped)
```

Notifications subscribes to `OrderPlaced`, `OrderConfirmed`, `OrderCancelled`, `PaymentFailed`, and
`ShipmentDispatched` in parallel with everyone else — it's a leaf, publishes nothing further, and
its handlers are idempotent against redelivery via a `NotificationRecord` keyed on the source
event's id (the outbox gives *at-least-once* delivery, not exactly-once).

A message that fails past `MaxAttempts` (5) is moved to that module's dead-letter table instead of
retried forever — a human resolves it, `OutboxProcessor` doesn't.

## What's built vs. scaffolded

`Ordering` is built to full depth as the reference implementation: real EF Core/SQLite
persistence, an owned-type mapping for `Order`/`OrderLine`/`Money`, and a working outbox table.
`Inventory`, `Payments`, `Shipping` follow the identical Domain/Application/Infrastructure shape
but use in-memory repositories and an in-memory outbox (`BuildingBlocks.Infrastructure.InMemoryOutboxStore`)
instead of EF Core — swapping one for the other is a change confined entirely to a module's own
`Infrastructure` project. `Notifications` has no persistence beyond an in-memory idempotency log.
No EF Core migrations exist yet (`Database.EnsureCreated()` stands in); that's explicitly next,
not done here.

**Verified, not just written:** the whole solution builds clean (0 warnings), the 7 domain unit
tests pass, and the full flow was run live against the actual API — a placed order cascades
through Confirmed → PaymentReceived → Shipped with a notification logged at every step; an
order for more stock than exists correctly cancels instead of confirming; an empty order is
rejected with 400, not 500.

## A DI lesson learned the hard way

The first working version of this had a real bug, caught by actually running it rather than by
reading the code: `IUnitOfWork`, `IOutboxWriter`, and `IIntegrationEventPublisher` are shared
interfaces (defined once in `BuildingBlocks.Application`/`.Infrastructure`) that every module
registers its *own* implementation of. Registering them the normal way
(`services.AddScoped<IUnitOfWork, UnitOfWork>()` in one module,
`services.AddScoped<IUnitOfWork, NoOpUnitOfWork>()` in the next) doesn't give each module its own
implementation — .NET's container resolves a single (non-`IEnumerable`) dependency to whichever
registration was added *last*, globally, across the whole container. In practice every module's
handlers all silently got the last-registered module's `NoOpUnitOfWork`, so `Order.Place()` never
actually persisted, and integration events all landed in the wrong module's outbox. It only
surfaced by placing a real order and reading `Order {id} not found` a few hops downstream.

The fix (see `OrderingModule.cs`, `InventoryModule.cs`, etc.) is .NET's keyed DI services:
`AddKeyedScoped<IUnitOfWork, UnitOfWork>("Ordering")`, resolved back out with
`GetRequiredKeyedService<IUnitOfWork>("Ordering")` inside each module's own handler factory
registrations — so the key, not registration order, decides which module's instance a handler
gets. `IOutboxStore` deliberately stays *unkeyed*, because `OutboxProcessor` wants every module's
store via `IEnumerable<IOutboxStore>`, and that form of resolution (unlike a single dependency)
really does aggregate every unkeyed registration rather than picking the last one.

## Known gaps (kickoff scope, not hidden)

- No EF Core migrations for `Ordering` yet — `EnsureCreated()` stands in.
- `Inventory`/`Payments`/`Shipping` are in-memory, not durable — by design, to keep the kickoff
  scoped to proving the module shape and the async flow, not four EF Core contexts.
- No idempotency key on `POST /api/orders` itself — a retried request places a second order.
- `OutboxProcessor` polls a fixed 5s interval rather than a push-based trigger; fine at kickoff
  scale, worth revisiting under load.
