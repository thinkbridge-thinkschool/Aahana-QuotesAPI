using 'main.bicep'

param environment = 'dev'
param location = 'centralindia'
param resourceGroupName = 'thinkschool-rg-dev'
param containerAppEnvName = 'thinkschool-env-dev'
param containerAppName = 'quotes-api-dev'
param containerImageTag = readEnvironmentVariable('CONTAINER_IMAGE_TAG', 'dev-latest')

// Secrets and per-person identity never live in source control, dev or prod — pulled from the
// deployer's shell/CI environment instead. See DESIGN.md, "Why nothing here is a literal".
param jwtSigningKey = readEnvironmentVariable('JWT_SIGNING_KEY')
param aadAdminLogin = readEnvironmentVariable('AAD_ADMIN_LOGIN')
param aadAdminObjectId = readEnvironmentVariable('AAD_ADMIN_OBJECT_ID')

param corsAllowedOrigins = [
  'http://localhost:4200'
]

// Dev sizing: cheapest tiers, scale-to-zero when idle — this environment doesn't need to be
// always warm, and nobody's paged if it cold-starts.
param containerAppCpu = '0.25'
param containerAppMemory = '0.5Gi'
param minReplicas = 0
param maxReplicas = 1
param registrySku = 'Basic'

param sqlSkuName = 'Basic'
param sqlSkuTier = 'Basic'

param serviceBusSkuName = 'Basic'
param serviceBusMaxDeliveryCount = 5
