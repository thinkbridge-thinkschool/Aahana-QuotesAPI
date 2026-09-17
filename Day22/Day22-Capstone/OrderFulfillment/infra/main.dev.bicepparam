using 'main.bicep'

param environment = 'dev'
param location = 'centralindia'
param resourceGroupName = 'orderfulfillment-rg-dev'
// Not a new environment — this subscription allows only one Container Apps environment per
// region, and QuotesApi already owns it. See main.bicep's useExistingEnvironment comment.
param containerAppEnvName = 'thinkschool-env'
param useExistingEnvironment = true
param existingEnvironmentResourceGroup = 'thinkschool-rg'
param containerAppName = 'orderfulfillment-api-dev'
param containerImageTag = readEnvironmentVariable('CONTAINER_IMAGE_TAG', 'dev-latest')

// Day 29: no real Entra App Registration exists yet (Day 25's open gap). Confirmed live: without
// some form of this, the container crashes on startup with "Entra:TenantId and Entra:Audience
// must be configured...". Day 30 review: originally ASPNETCORE_ENVIRONMENT=Development (too broad
// a lever — see main.bicep's allowUnauthenticated comment); this is the narrower, auth-only fix.
param allowUnauthenticated = true

// Day 30 review: acrPullGranted lost its default in main.bicep specifically so this can't be
// silently omitted. false here is deliberate — it's what a *fresh* dev environment needs for its
// first deploy to succeed at all (see modules/api.bicep's deadlock comment). Promoting an
// already-bootstrapped environment to the real image is a one-time manual
// `az deployment sub create ... --parameters acrPullGranted=true` an operator runs once, not
// something this baseline file should flip permanently (that would recreate the same deadlock the
// next time this environment is torn down and recreated from scratch).
param acrPullGranted = false

// Per-person identity never lives in source control, dev or prod — pulled from the deployer's
// shell/CI environment instead. See DESIGN.md, "Why nothing here is a literal".
param aadAdminLogin = readEnvironmentVariable('AAD_ADMIN_LOGIN')
param aadAdminObjectId = readEnvironmentVariable('AAD_ADMIN_OBJECT_ID')

// Dev sizing: cheapest tiers, scale-to-zero when idle — this environment doesn't need to be
// always warm, and nobody's paged if it cold-starts.
param containerAppCpu = '0.25'
param containerAppMemory = '0.5Gi'
param minReplicas = 0
param maxReplicas = 1
param registrySku = 'Basic'

param sqlSkuName = 'Basic'
param sqlSkuTier = 'Basic'

// Standard, not Basic: topics/subscriptions (the pub/sub shape this Service Bus needs) aren't
// available on the Basic tier at all, in dev or prod.
param serviceBusSkuName = 'Standard'
param serviceBusMaxDeliveryCount = 5
