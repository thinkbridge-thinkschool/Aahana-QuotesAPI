# Day 32 — Ship + demo + postmortem (OrderFulfillment capstone)

## Live

`https://orderfulfillment-api-dev.orangebeach-4969d067.centralindia.azurecontainerapps.io`

Real Container App, real Azure SQL (managed identity, no password anywhere), real Azure Service
Bus topic + 5 subscriptions (managed identity, no connection string anywhere). Not a staging
mock — this is the same "dev" environment every prior day in this capstone has been building
toward, now doing the one thing it was always supposed to do.

## Demo — the saga, live, start to finish

```bash
curl -X POST https://orderfulfillment-api-dev.orangebeach-4969d067.centralindia.azurecontainerapps.io/api/v1/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"99999999-9999-9999-9999-999999999999","lines":[{"sku":"WIDGET-1","quantity":1,"unitPrice":9.99,"currency":"USD"}]}'
```
```
{"orderId":"47594cc2-5b66-442a-b1a0-199147c00f54"}
```

Queried directly against the live database afterward (independent of what the API claims):

| t+0s | t+6s | t+12s | t+18s |
|---|---|---|---|
| `Pending` | `Confirmed` | `PaymentReceived` | `Shipped` |

The compensating path, same session: an order for 999 units of a SKU seeded with 5 in stock
returns `201` and reaches `Cancelled` within one poll cycle — the saga declines the order instead
of the API rejecting the request, exactly as designed back on Day 22.

**This is the first time, across eleven days of building this capstone, that the saga has
completed against real deployed infrastructure rather than only in-process locally.** Getting
here today meant finding and fixing three separate, independent, compounding bugs — described
honestly below, not smoothed over, because the fix is the actual demo.

## The hardest bug, and what it taught me

Three things had to be wrong at once for the saga to never complete, and none of them threw an
error anywhere:

1. **`AzureServiceBusMessageBus`** stamped `EventType` with the *full CLR type name*
   (`"OrderPlacedIntegrationEvent"`), not the short name Bicep's subscription filters actually
   check for (`"OrderPlaced"`). A message matching zero filters isn't an error in Service Bus —
   it's just never delivered, silently.
2. **The subscription filter rules had never actually been deployed to Azure at all.** The
   successful `servicebus.bicep` deploy predates this resource being added to the template, and
   no full redeploy has completed since (the AcrPull deadlock from Day 24/29 blocked every
   attempt past that point). Real infra drift, not a code bug — the committed template and the
   live resources had quietly diverged.
3. **A rule literally named `"$Default"` silently fails to persist** through the exact
   `az servicebus topic subscription rule create` API path used to patch this live — the command
   reports success and echoes back the correct filter, and an immediate `rule show` for that same
   name returns "Rule does not exist." A genuine, reproducible platform quirk around that one
   reserved name, confirmed by switching to a plain name and watching it persist immediately.

Any ONE of these alone would have been findable by looking at logs. All three together produced
total silence: no exception anywhere, in the app, in Application Insights, or in Service Bus's
own diagnostics — just an order stuck at `Pending` forever, and every previous session's search
for "the error" came up empty because there wasn't one to find. What finally broke it open was
abandoning log-reading entirely and creating a temporary catch-all subscription with no filter at
all, to answer one binary question directly: is the message reaching the topic, yes or no? Once
that was a confirmed "yes," the problem space shrank from "the whole async pipeline" to "the
filters, specifically" — and from there each bug fell in about ten minutes.

**What that taught me:** when a system fails silently, stop trying to read more logs out of it
and instead ask it a smaller, more falsifiable question — one with a binary answer you can get in
one command. A month of "read the logs, add more logging, read the logs again" hadn't moved this
forward; one throwaway subscription with a match-everything filter did, in under half an hour.

## What I'd do differently

Build the catch-all-subscription trick — or something like it — on Day 25, the day the real
Service Bus wiring went in, as a standing diagnostic tool rather than discovering it as a last
resort on Day 32. More generally: this capstone's whole practice was "verify live, don't assert"
— but verification stopped at "no error was thrown" too many times in a row (Days 29, 30, 31 all
ran real live checks and all came back clean-looking). The lesson isn't "verify more," it's
"verify the *positive* claim, not just the absence of a negative one" — "zero active messages" was
treated as possible evidence of success for three sessions when it was actually just as
consistent with total failure, and nothing forced that ambiguity to resolve until today.

## What I'm proudest of

Not the fix itself — the fact that Day 31's local end-to-end tests had already proven the saga
*logic* was correct, in isolation, before any of today's debugging started. That meant today's
entire search space was narrowed to "the Service Bus wiring, specifically" from the first
minute, instead of re-litigating whether `Order.Confirm()` or `OnStockReserved` might be broken.
A capstone built the way this one was — real infra, real bugs found and fixed live, every claim
checked instead of assumed — is the reason a three-session mystery took thirty minutes to close
once the right question got asked.

## What would break this

Everything already flagged honestly across Days 25–31 is still true: no real Entra App
Registration exists (auth runs open on this dev slot by design, not by accident); the Container
App's environment isn't VNet-integrated, so the private endpoints Day 27 built are provisioned but
unreachable; `Inventory`/`Payments`/`Shipping` are still in-memory, so a replica recycle loses
their state; and today's fix was applied by hand to the live namespace before being committed to
Bicep — a from-scratch `azd provision` of a fresh environment hasn't been re-verified against the
corrected template yet, only reasoned through. The one honestly new risk from today: the
`"$Default"`-rule quirk means anyone who ever "fixes" `modules/servicebus.bicep` by renaming the
rule back to `$Default` — a very natural-looking refactor — will silently break this again, with
the exact same symptom, and the exact same total silence.
