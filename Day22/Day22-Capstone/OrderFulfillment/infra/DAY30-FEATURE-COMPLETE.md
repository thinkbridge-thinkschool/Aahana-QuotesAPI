# Day 30 — Build day 2: feature completeness (OrderFulfillment capstone)

Picked up Day 29's one open item first — the downstream saga not completing against real Service
Bus — before declaring anything feature-complete. It's still open, honestly, but for a more
specific reason than Day 29 could pin down, and closing it is now a precise, actionable next step
rather than an open question.

## Continuing the Day 29 investigation

Added targeted diagnostics to `ServiceBusSubscriptionProcessor.OnMessageAsync` — a log line at the
top of every message receipt, unconditional, stating the resolved event type, the message id, and
exactly how many handlers were found for it. Built, pushed (`:v3`), deployed live, then placed
three more real orders against the live API while watching for that line.

**It never appeared once**, across `az containerapp logs show`, a `--follow` live tail, and a
direct Log Analytics KQL query against `ContainerAppConsoleLogs_CL`. That rules out the Day 29
lead cleanly: this isn't "the handler list is empty and the message completes silently" — the
message-receipt callback itself is never firing, for any of the five `ServiceBusSubscriptionProcessor`
instances, despite the app being up, healthy, and successfully *sending* to the topic (confirmed
again this session — a fresh order's `OrderPlaced` outbox row shows `ProcessedOn` set with zero
errors, same as Day 29).

**Also ruled out today, not just asserted:** a stray "Application is shutting down" log line
briefly suggested the whole scale-to-zero (`minReplicas: 0` in `main.dev.bicepparam`) killing the
background receivers between requests — a real, structurally plausible explanation, since a
`BackgroundService`-based outbox/saga pattern fundamentally needs the process to stay warm between
HTTP requests. Checked directly: the live Container App's actual `scale` config right now is
`minReplicas: 1` (drifted from the committed param file during earlier live debugging, not
redeployed since), and `az containerapp replica list` showed the same replica pod continuously
alive, still answering requests, minutes after that log line — so the process was not, in fact,
recycled. A real dead end, reported as one rather than quietly dropped.

**What's left as the concrete next lead:** either the `ServiceBusClient`/processor construction
inside the `if (!string.IsNullOrEmpty(serviceBusNamespace) && ...)` branch of `Program.cs` isn't
actually reaching `StartProcessingAsync` for some reason not yet surfaced (no exception logged
anywhere, including `OnErrorAsync`, which would itself be surprising), or Log Analytics'
ingestion lag (confirmed directly today — a 3-minute-old query window returned zero rows for an
app that was actively running) is hiding a genuine but delayed signal that a longer wait would
have caught. Both are precise enough to attack directly next session, unlike Day 29's "the whole
saga just doesn't work."

## Feature completeness — what that means here, stated plainly

The capstone's originally scoped feature set (Day 22's DESIGN.md: place an order, the async saga
across Inventory/Payments/Shipping/Notifications, real Azure infra, identity, observability, a
security pass) is code-complete and locally verified end to end — every module, every handler,
every domain invariant, all 11 unit tests passing. What is **not** complete is the live,
deployed, real-Service-Bus version of that same saga — confirmed working for the first hop
(placing an order, persisting to real Azure SQL) as of Day 29, still not confirmed past it. That
distinction — code-complete vs. deployed-and-proven — is the actual state, not rounded up or down.

## PR opened for review

See the PR linked in this session's submission. Days 28 and 29's cumulative work (the design
review, the ADR, the AcrPull/ASPNETCORE_ENVIRONMENT/SQL-grant fixes, the Dockerfile, and today's
diagnostic addition) is up for review against the trunk this capstone has been building on since
Day 18. Review comments and responses are in the PR's own thread, not duplicated here.

## What did I learn this session?

That "no error anywhere" stopped being reassuring the moment it applied to a background receiver
that also produces zero success signal — silence from a system that's supposed to be doing
something is not evidence it's idle, it's just evidence you haven't found where its output is
going yet. Ruling out the scale-to-zero hypothesis mattered as much as the diagnostic logging
itself: a plausible-sounding explanation that isn't checked against the actual live resource state
is exactly the kind of thing this capstone's whole practice (verify, don't assert) exists to catch.
