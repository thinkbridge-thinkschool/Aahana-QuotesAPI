# Infrastructure as Code — Day 23

The Quotes API's infra, restructured into three parameterized Bicep modules (API, SQL, Service
Bus) behind one subscription-scope orchestrator, with separate dev/prod `.bicepparam` files. No
resource in here was ever clicked into existence in the portal — everything below is exactly what
was deployed/what-if'd.

## Layout

```
infra/
  main.bicep                 subscription scope: creates the resource group, wires the 3 modules together
  main.dev.bicepparam         dev parameter values
  main.prod.bicepparam        prod parameter values
  modules/
    api.bicep                 Container Apps environment + ACR + Container App (system-assigned identity)
    sql.bicep                 Azure SQL logical server (Entra-ID-only auth) + database + firewall rule
    servicebus.bicep          Service Bus namespace (RBAC-only, disableLocalAuth) + queue + DLQ settings
    servicebus-access.bicep   grants the API's managed identity access to the Service Bus namespace
  whatif-dev-output.txt       captured output of the what-if run below
```

## Why a 4th module (`servicebus-access.bicep`) instead of 3

The API needs SQL's and Service Bus's connection info as container env vars, so `api.bicep` is
deployed *after* `sql.bicep`/`servicebus.bicep`. But granting the API's managed identity access to
the Service Bus namespace needs the API's `principalId`, which only exists once `api.bicep` has
deployed — so that RBAC assignment needs to happen *after* `api.bicep`. Two modules that each need
the other's output is a cycle Bicep's dependency graph can't resolve. Splitting the RBAC grant
into its own tiny module (an `existing` reference to the already-deployed namespace + one
`roleAssignments` resource) breaks the cycle: `main.bicep`'s real order is `sql` + `serviceBus` →
`api` → `serviceBusAccess`.

## Passwordless everywhere

- **ACR**: the Container App pulls images via its own system-assigned identity (`AcrPull` role),
  not a registry username/password — this part already existed before Day 23.
- **SQL**: `azureADOnlyAuthentication: true` on the server — no SQL login exists at all. The
  output connection string uses `Authentication=Active Directory Default`, not a password.
- **Service Bus**: `disableLocalAuth: true` on the namespace — no SAS connection string is ever
  produced as an output. The API is granted `Azure Service Bus Data Owner` on the namespace via
  its managed identity instead.

The one real secret, the JWT signing key, is the only `@secure()` param in the whole stack — and
even that never sits in a `.bicepparam` file (see below).

## Dev vs. prod: what actually differs

| | dev | prod |
|---|---|---|
| Container App CPU/memory | 0.25 vCPU / 0.5Gi | 1 vCPU / 2Gi |
| Container App replicas | 0–1 (scales to zero) | 2–10 |
| ACR SKU | Basic | Standard |
| SQL SKU | Basic | `GP_S_Gen5_2` (General Purpose, serverless) |
| Service Bus SKU | Basic | Standard |
| Max delivery count | 5 | 10 |

Same modules, same `main.bicep` — only the two `.bicepparam` files differ. That's the point of
parameterizing instead of hand-editing resource blocks per environment.

## Why nothing here is a literal

`jwtSigningKey`, `aadAdminLogin`, and `aadAdminObjectId` are all pulled with
`readEnvironmentVariable(...)` inside the `.bicepparam` files, not written as literal values —
confirmed by actually running `bicep build-params` without those variables set first, which fails
loudly (`BCP427: Environment variable "..." does not exist`) instead of silently deploying with an
empty secret. A committed placeholder GUID/string would *look* safe and quietly become a copy-paste
trap the first time someone reused this file for a real environment.

## Verified, not just written

Ran against the real Azure subscription this session is authenticated to (`az account show`),
with synthetic placeholder values in the shell environment — never committed:

```
export JWT_SIGNING_KEY="placeholder-not-a-real-secret-for-whatif-only-..."
export AAD_ADMIN_LOGIN="sql-admins-dev-placeholder"
export AAD_ADMIN_OBJECT_ID="00000000-0000-0000-0000-000000000000"

az deployment sub what-if --name quotes-day23-whatif --location centralindia \
  --template-file main.bicep --parameters main.dev.bicepparam
```

Result: **6 resources to create**, correctly named/sku'd per the dev param file (`thinkschool-rg-dev`,
`quotes-sql-dev-...` on the `Basic` SQL SKU, `quotes-sb-dev-...` on the `Basic` Service Bus SKU) —
full output in `whatif-dev-output.txt`.

What-if only shows 6 of the ~9 resources: it short-circuits the `api` and `servicebus-access`
modules with a `NestedDeploymentShortCircuited` diagnostic, because their parameters include
runtime `reference()`-style values (each other's outputs) that what-if's static diff can't
evaluate ahead of a real deploy — a documented Azure limitation, not a template defect. To confirm
those two modules are actually valid (not just silently skipped), `az deployment sub validate` was
run separately against both `main.dev.bicepparam` and `main.prod.bicepparam` — both returned
`"provisioningState": "Succeeded"`, `"error": null`, covering every module including the two
what-if couldn't diff.

## Known gaps (not hidden)

- SQL's Entra-only auth means the API's managed identity still needs a contained database user
  (`CREATE USER ... FROM EXTERNAL PROVIDER`) before it can actually connect — that's T-SQL, not an
  ARM/Bicep resource, and isn't automated here. Would need a `deploymentScript` resource or a
  pipeline step after this template applies.
- The SQL firewall rule (`AllowAllWindowsAzureIps`, i.e. any Azure-hosted resource) is broader than
  a VNet-scoped private endpoint would be — acceptable for this kickoff, worth tightening before
  real data goes in the database.
- No actual `az deployment sub create` was run — only `what-if` and `validate`, both read-only
  against Azure. Provisioning the real dev/prod resource groups is a separate, deliberate step.
