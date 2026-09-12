# Day 24 — Deployment Stacks + azd (OrderFulfillment capstone)

Deploys Day 23's `main.bicep` through the Azure Developer CLI (`azd`), with the `deployment.stacks`
alpha feature turned on so `azd provision`/`azd down` go through an `Microsoft.Resources/deploymentStacks`
resource (equivalent to `az stack sub create`/`delete`) instead of a plain `az deployment sub create`.

This is a real attempt against the real subscription this session is authenticated to, not a
simulation — including the parts that failed. Both are reported here, because the failures are
where the actual subscription-level constraints this exercise was supposed to surface came from.

## Layout added this pass

```
OrderFulfillment/
  azure.yaml                     azd project file — points at infra/, one service (orderfulfillment-api)
  infra/
    main.parameters.json         azd's parameter file (new) — separate from Day 23's
                                  main.dev.bicepparam / main.prod.bicepparam, which still work
                                  unchanged for the raw `az deployment` / what-if workflow
    modules/api.bicep            changed this pass — see "The fix" under the deploy log below
    modules/sql.bicep            changed this pass — see Attempt 1 under the deploy log below
```

Two azd environments exist locally (`azd env list`): `dev` and `prod`. Their values live in
`.azure/{dev,prod}/.env`, which is gitignored (`.azure/.gitignore` is `*`) — nothing in this
section was ever committed as a literal.

## Turning on Deployment Stacks

```
azd config set alpha.deployment.stacks on
```

Confirmed active on every `azd provision`/`azd down` run by the banner azd prints:

```
WARNING: Feature 'deployment.stacks' is in alpha stage.
```

## azd config (as asked for in the exercise)

**azure.yaml**
```yaml
name: orderfulfillment-capstone
metadata:
  template: azd-init

infra:
  path: infra
  module: main

services:
  orderfulfillment-api:
    project: ./src/Host/OrderFulfillment.Api
    language: dotnet
    host: containerapp
```

**infra/main.parameters.json** — every value that differs per environment is a `${VAR}` azd
resolves from the active environment's stored values at provision time:
```json
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "environment": { "value": "${ENVIRONMENT}" },
    "location": { "value": "${AZURE_LOCATION}" },
    "resourceGroupName": { "value": "${RESOURCE_GROUP_NAME}" },
    "containerAppEnvName": { "value": "${CONTAINER_APP_ENV_NAME}" },
    "useExistingEnvironment": { "value": true },
    "existingEnvironmentResourceGroup": { "value": "${EXISTING_ENV_RESOURCE_GROUP}" },
    "containerAppName": { "value": "${CONTAINER_APP_NAME}" },
    "containerImageTag": { "value": "${CONTAINER_IMAGE_TAG}" },
    "aadAdminLogin": { "value": "${AAD_ADMIN_LOGIN}" },
    "aadAdminObjectId": { "value": "${AAD_ADMIN_OBJECT_ID}" },
    "containerAppCpu": { "value": "${CONTAINER_APP_CPU}" },
    "containerAppMemory": { "value": "${CONTAINER_APP_MEMORY}" },
    "minReplicas": { "value": "${MIN_REPLICAS}" },
    "maxReplicas": { "value": "${MAX_REPLICAS}" },
    "registrySku": { "value": "${REGISTRY_SKU}" },
    "sqlSkuName": { "value": "${SQL_SKU_NAME}" },
    "sqlSkuTier": { "value": "${SQL_SKU_TIER}" },
    "serviceBusMaxDeliveryCount": { "value": "${SERVICE_BUS_MAX_DELIVERY_COUNT}" }
  }
}
```

**azd env values** (`azd env get-values`, subscription id and personal identity redacted):
| Key | dev | prod |
|---|---|---|
| RESOURCE_GROUP_NAME | orderfulfillment-rg-dev | orderfulfillment-rg-prod |
| CONTAINER_APP_ENV_NAME | thinkschool-env | thinkschool-env |
| EXISTING_ENV_RESOURCE_GROUP | thinkschool-rg | thinkschool-rg |
| CONTAINER_APP_NAME | orderfulfillment-api-dev | orderfulfillment-api |
| CONTAINER_APP_CPU / MEMORY | 0.25 / 0.5Gi | 1 / 2Gi |
| MIN / MAX_REPLICAS | 0 / 1 (1 during the deploy attempt — see below) | 2 / 10 |
| REGISTRY_SKU | Basic | Standard |
| SQL_SKU_NAME / TIER | Basic / Basic | GP_S_Gen5_2 / GeneralPurpose serverless |
| SERVICE_BUS_MAX_DELIVERY_COUNT | 5 | 10 |
| AZURE_LOCATION | centralindia | centralindia |

## The real dev deploy, in the order it actually happened

**Attempt 1** — `azd provision` against `centralindia`, first try. Failed on two real bugs at once:

```
(!) Warning: Resource ".../AllowAllWindowsAzureIps" contains the reserved word "WINDOWS"
...
(x) Failed: Azure SQL Server (1m29s)
ERROR: InvalidExternalAdministratorSid: Invalid or missing external administrator object id.
```

Two things Day 23's `what-if`/`validate` never caught, because both are enforced only on a real
deploy:
1. `AllowAllWindowsAzureIps` (the SQL firewall rule name, copied from the QuotesApi pattern) trips
   Azure's reserved-word check on resource names — renamed to `AllowAllAzureServicesIps`
   (`modules/sql.bicep`).
2. `aadAdminObjectId` was still the Day 23 what-if placeholder (`00000000-...`). A real deployment
   validates that this is an actual principal in the tenant — fetched the real one:
   `az ad signed-in-user show --query id` — and used it for both dev and prod.

**Attempt 2** — fixed both, retried. Resource group, Service Bus, and SQL Server all succeeded
this time; the API module then hit a subscription quota:

```
(✓) Done: Resource group ...
(✓) Done: Service Bus Namespace ...
(✓) Done: Azure SQL Server (1m15s)
ERROR: InvalidTemplateDeployment: ... 'Microsoft.App/managedEnvironments (2025-01-01)' reported
preflight validation errors ...
MaxNumberOfRegionalEnvironmentsInSubExceeded: The subscription cannot have more than 1 Container
App Environments in Central India.
```

QuotesApi's own Container Apps environment (`thinkschool-env`, in `thinkschool-rg`, confirmed via
`az containerapp env list`) already occupies the one environment this subscription allows in this
region. Tore the partial dev stack down (`azd down --force`) before investigating further.

**Region detour** — tried moving the whole stack to `southeastasia` instead of fighting the quota.
Failed immediately at validation, for a different reason:

```
RequestDisallowedByAzure: ... This policy maintains a set of best available regions where your
subscription can deploy resources. ... contact support.
```

Checked `eastus`, `eastus2`, `westus2`, `uksouth`, and `centralindia` directly with
`az deployment sub validate` (fast, no waiting) — only `centralindia` is actually usable on this
Azure for Students subscription. Reverted `AZURE_LOCATION` back to `centralindia` — the quota has
to be solved without a region change.

**The fix** — added `useExistingEnvironment`/`existingEnvironmentResourceGroup` params to
`modules/api.bicep` and `main.bicep`: when true, the module does an `existing` cross-resource-group
reference to `thinkschool-env` instead of creating a second managed environment. QuotesApi's
environment itself is only ever read (`.id`), never written to.

**Attempts 3–5** — Resource group, Service Bus, SQL Server, and Container Registry now succeed
consistently and fast (idempotent — the stack recognizes what already exists), but the Container
App itself failed identically three times in a row:

```
(✓) Done: Resource group: orderfulfillment-rg-dev
(✓) Done: Service Bus Namespace: orderfulfillment-sb-dev-wq5mb2ijh5gfu
(✓) Done: Azure SQL Server: orderfulfillment-sql-dev-wq5mb2ijh5gfu
(✓) Done: Container Registry: orderfulfillmentacrwq5mb2ijh5gfu
(x) Failed: Container App: orderfulfillment-api-dev (~20m each time)
ERROR: ContainerAppOperationError: Failed to provision revision for container app
'orderfulfillment-api-dev'. Error details: Operation expired.
```

Investigated with `az containerapp show` (the Container App resource itself creates fine —
`environmentId` correctly resolves to `thinkschool-env` across the resource-group boundary) and
`az containerapp revision list` (empty every time — no revision ever starts). Ruled out
`minReplicas: 0` as the cause by retrying with `minReplicas: 1` on the third attempt — identical
failure, identical ~20-minute timing. The one structural difference left between this app and
QuotesApi's working `quotes-api` (same environment, same registries-before-AcrPull-grant ordering,
same bootstrap-image pattern) is that `quotes-api` lives in the *same* resource group as
`thinkschool-env`, and this app deliberately does not — keeping QuotesApi's and the capstone's
resources from touching, on request, meant not testing that same-resource-group hypothesis.

Stopped after the third identical failure rather than keep re-spending real deploy time (each
attempt costs ~20 real minutes) chasing a hypothesis this session couldn't test without violating
that constraint.

## Clean teardown — demonstrated twice, including once over a failed resource

```
azd down --force --no-prompt
...
Deleting subscription deployment stack azd-stack-dev
  (✓) Done: Deleted subscription deployment stack azd-stack-dev
SUCCESS: Your application was removed from Azure in 6 minutes 52 seconds.
```

Ran this twice: once after attempt 2 (clearing the quota-blocked partial stack before the region
detour), and again after attempt 5 (the stopping point) — that second time the stack still
contained the Container App stuck in `Failed` provisioning state, and `azd down` removed it along
with the resource group, Service Bus namespace, SQL server, and registry in one call anyway, in
9 minutes 34 seconds. Verified nothing was left behind both times:
`az group exists --name orderfulfillment-rg-dev` → `false`, `az stack sub list` → empty.

## What actually got verified end-to-end (real Azure, this subscription)

- Resource group, Service Bus namespace (topic + all 5 subscriptions with the correct per-module
  `EventType IN (...)` SQL filters), SQL Server (real Entra-ID-only admin, not a placeholder), and
  Container Registry — created successfully through `azd provision` with Deployment Stacks active.
- The stack's teardown — one command, twice, including tearing down a stack holding a failed
  resource, with zero manual cleanup and zero orphaned resources afterward.
- Prod's template + parameters, `az deployment sub validate` (`Succeeded`) — a real provision
  wasn't attempted for prod, since it would hit the identical unresolved Container App issue.

## Known gaps (not hidden)

- The Container App's first revision does not successfully provision when its managed environment
  lives in a different resource group under this subscription — real, reproducible, root cause
  narrowed to "cross-resource-group environment join" but not confirmed, because confirming it
  would mean testing the app in QuotesApi's resource group, which was explicitly out of scope here.
- No teardown-triggered drift check was run (nothing was manually edited out-of-band to provoke
  one) — the mechanism (see below) is described, not demonstrated live, since the resources no
  longer exist to check.

## What Deployment Stacks give you over plain deployments

A plain `az deployment sub create` gives you a deployment *record* — a log of what happened once.
A Deployment Stack gives you a *live-managed resource set*: `az stack sub delete` (what `azd down`
calls under the hood) deletes every resource the stack still owns in one call, in the right order,
even when one of those resources never finished provisioning — proven twice here, the second time
tearing down a stack that had a `Failed` Container App sitting in it, with no manual hunting for
what to delete or in what order. Plain deployments leave you doing that by hand. The same
managed-set bookkeeping is also what makes drift detectable: the stack knows the exact resource
list it's responsible for, so a resource edited or deleted outside the stack shows up as a
difference the next time the stack is inspected or reconciled — a plain deployment has no ongoing
record to compare against after it finishes.

## What did I learn this session?

`what-if`/`validate` are static template checks — they do not run the platform-side preflight and
quota checks a real deployment does. Four separate things (a reserved-word resource name, a
placeholder AAD object id, a regional subscription policy, and a per-region environment count
quota) all passed Day 23's validation cleanly and only surfaced once real resources were actually
being created. "Validated" and "deployable" are not the same claim.

## What would break this?

Deploying a second Container App into `thinkschool-env` from *its own* resource group — this
session never tested whether that specific combination (new app, new resource group, existing
environment, existing environment's own resource group) succeeds, because doing so was explicitly
out of scope. If it also fails, the real constraint is "one environment, one resource group,
forever" on this subscription tier — which would mean any second app can only ever join an
existing environment by living in that environment's own resource group, undermining the
per-app resource-group isolation this whole capstone was built around.
