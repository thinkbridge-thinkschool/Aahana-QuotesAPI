using 'main.bicep'

param environment = 'prod'
param location = 'centralindia'
param resourceGroupName = 'orderfulfillment-rg-prod'
// Not a new environment — this subscription allows only one Container Apps environment per
// region, shared with QuotesApi and capstone-dev alike. See main.bicep's useExistingEnvironment
// comment. Isolation from dev/QuotesApi is by Container App name, not environment boundary.
param containerAppEnvName = 'thinkschool-env'
param useExistingEnvironment = true
param existingEnvironmentResourceGroup = 'thinkschool-rg'
param containerAppName = 'orderfulfillment-api'
param containerImageTag = readEnvironmentVariable('CONTAINER_IMAGE_TAG')

// Per-person identity never lives in source control, dev or prod — pulled from the deployer's
// shell/CI environment instead. See DESIGN.md, "Why nothing here is a literal".
param aadAdminLogin = readEnvironmentVariable('AAD_ADMIN_LOGIN')
param aadAdminObjectId = readEnvironmentVariable('AAD_ADMIN_OBJECT_ID')

// Prod sizing: always at least 2 replicas (no cold start, survives one replica recycling),
// headroom to scale under real load.
param containerAppCpu = '1'
param containerAppMemory = '2Gi'
param minReplicas = 2
param maxReplicas = 10
param registrySku = 'Standard'

// Serverless General Purpose: scales compute with load instead of paying for a fixed vCore
// count around the clock.
param sqlSkuName = 'GP_S_Gen5_2'
param sqlSkuTier = 'GeneralPurpose serverless'

param serviceBusSkuName = 'Standard'
param serviceBusMaxDeliveryCount = 10
