using 'main.bicep'

param environment = 'prod'
param location = 'centralindia'
param resourceGroupName = 'thinkschool-rg-prod'
param containerAppEnvName = 'thinkschool-env-prod'
param containerAppName = 'quotes-api'
param containerImageTag = readEnvironmentVariable('CONTAINER_IMAGE_TAG')

// Secrets and per-person identity never live in source control, dev or prod — pulled from the
// deployer's shell/CI environment instead. See DESIGN.md, "Why nothing here is a literal".
param jwtSigningKey = readEnvironmentVariable('JWT_SIGNING_KEY')
param aadAdminLogin = readEnvironmentVariable('AAD_ADMIN_LOGIN')
param aadAdminObjectId = readEnvironmentVariable('AAD_ADMIN_OBJECT_ID')

param corsAllowedOrigins = [
  'https://quotes.thinkschool.dev'
]

// Prod sizing: always at least 2 replicas (no cold start, survives one replica recycling),
// headroom to scale under real load.
param containerAppCpu = '1'
param containerAppMemory = '2Gi'
param minReplicas = 2
param maxReplicas = 10
param registrySku = 'Standard'

// Serverless General Purpose: scales compute with load instead of paying for a fixed vCore
// count around the clock, without prod's data going on the same Basic tier as dev's.
param sqlSkuName = 'GP_S_Gen5_2'
param sqlSkuTier = 'GeneralPurpose serverless'

param serviceBusSkuName = 'Standard'
param serviceBusMaxDeliveryCount = 10
