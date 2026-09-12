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
