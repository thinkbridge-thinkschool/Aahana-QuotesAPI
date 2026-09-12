targetScope = 'subscription'

@description('Environment this stack is for — purely a naming/tagging input, dev vs prod is decided entirely by which .bicepparam file supplies these params, not by branching logic in here.')
@allowed(['dev', 'prod'])
param environment string = 'dev'

param location string = 'centralindia'

// Resource-group name deliberately does not collide with QuotesApi's own
// (thinkschool-rg-dev/prod) — this is a fully separate stack for a fully separate app, sharing
// nothing at the Azure resource level.
param resourceGroupName string = 'orderfulfillment-rg-dev'
param containerAppEnvName string = 'orderfulfillment-env-dev'
param containerAppName string = 'orderfulfillment-api'
param containerImageTag string = '0.1.0'

@description('This subscription (Azure for Students) caps Container Apps managed environments at one per region — QuotesApi already owns it in Central India. Real deployment surfaced this (what-if/validate did not); when true, the api module joins that existing environment instead of creating a second one.')
param useExistingEnvironment bool = false
param existingEnvironmentResourceGroup string = ''

// --- API sizing (differs per environment — see main.dev.bicepparam / main.prod.bicepparam) ---
param containerAppCpu string = '0.5'
param containerAppMemory string = '1Gi'
param minReplicas int = 1
param maxReplicas int = 3
@allowed(['Basic', 'Standard', 'Premium'])
param registrySku string = 'Basic'

// --- SQL (Ordering module only — see modules/sql.bicep) ---
param sqlDatabaseName string = 'orderfulfillmentdb'
param aadAdminLogin string
param aadAdminObjectId string
param sqlSkuName string = 'Basic'
@allowed(['Basic', 'Standard', 'GeneralPurpose', 'GeneralPurpose serverless', 'BusinessCritical'])
param sqlSkuTier string = 'Basic'

// --- Service Bus (topic + one subscription per subscribing module) ---
param serviceBusSkuName string = 'Standard'
param serviceBusTopicName string = 'order-fulfillment-events'
param serviceBusMaxDeliveryCount int = 5

// --- Day 25: Entra ID app auth — neither value is a secret (tenant ID and an app registration's
// audience/client ID are public identifiers), so unlike aadAdminLogin/aadAdminObjectId above,
// these have safe empty defaults: the app runs unauthenticated when they're unset (see
// Program.cs) rather than every deploy needing an app registration to exist first.
param entraTenantId string = ''
param entraAudience string = ''

// --- Day 26: OpenTelemetry -> Application Insights ---
param errorRateThreshold int = 5
param errorRateWindow string = 'PT5M'

// --- Day 27: private endpoints for the data tier ---
// Default false in both environments deliberately, not just dev: closing the public endpoint is
// the point of this pass, not a dev/prod distinction. See modules/network.bicep and
// DAY27-SECURITY-PASS.md for the one honest gap this doesn't close (the Container App's own
// environment isn't VNet-integrated, for reasons unrelated to this toggle).
param allowSqlPublicNetworkAccess bool = false
param allowServiceBusPublicNetworkAccess bool = false

resource resourceGroup 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: resourceGroupName
  location: location
  tags: {
    environment: environment
    app: 'order-fulfillment-capstone'
  }
}

// Registry/SQL server/Service Bus namespace names are all globally unique across Azure, so
// derive them from the subscription + environment instead of hardcoding a name someone else
// (or the other environment's stack) may already own.
var uniqueSuffix = uniqueString(subscription().subscriptionId, resourceGroupName, environment)
var registryName = 'orderfulfillmentacr${uniqueSuffix}'
var sqlServerName = 'orderfulfillment-sql-${environment}-${uniqueSuffix}'
var serviceBusNamespaceName = 'orderfulfillment-sb-${environment}-${uniqueSuffix}'
var logAnalyticsWorkspaceName = 'orderfulfillment-logs-${environment}'
var appInsightsName = 'orderfulfillment-appinsights-${environment}'
var vnetName = 'orderfulfillment-vnet-${environment}'

module network 'modules/network.bicep' = {
  name: 'orderfulfillment-network'
  scope: resourceGroup
  params: {
    location: location
    vnetName: vnetName
  }
}

module sql 'modules/sql.bicep' = {
  name: 'orderfulfillment-sql'
  scope: resourceGroup
  params: {
    location: location
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    aadAdminLogin: aadAdminLogin
    aadAdminObjectId: aadAdminObjectId
    skuName: sqlSkuName
    skuTier: sqlSkuTier
    allowPublicNetworkAccess: allowSqlPublicNetworkAccess
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneId: network.outputs.sqlPrivateDnsZoneId
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'orderfulfillment-servicebus'
  scope: resourceGroup
  params: {
    location: location
    namespaceName: serviceBusNamespaceName
    topicName: serviceBusTopicName
    skuName: serviceBusSkuName
    maxDeliveryCount: serviceBusMaxDeliveryCount
    allowPublicNetworkAccess: allowServiceBusPublicNetworkAccess
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneId: network.outputs.serviceBusPrivateDnsZoneId
  }
}

module appInsights 'modules/appinsights.bicep' = {
  name: 'orderfulfillment-appinsights'
  scope: resourceGroup
  params: {
    location: location
    logAnalyticsWorkspaceName: logAnalyticsWorkspaceName
    appInsightsName: appInsightsName
    errorRateThreshold: errorRateThreshold
    errorRateWindow: errorRateWindow
  }
}

// api depends on sql + serviceBus for their connection info (baked into container env vars at
// deploy time); it cannot also grant them RBAC on itself before it exists — see
// modules/servicebus-access.bicep for how that direction of the dependency is handled instead.
module api 'modules/api.bicep' = {
  name: 'orderfulfillment-api'
  scope: resourceGroup
  params: {
    location: location
    environmentName: containerAppEnvName
    containerAppName: containerAppName
    containerImageTag: containerImageTag
    registryName: registryName
    containerAppCpu: containerAppCpu
    containerAppMemory: containerAppMemory
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    registrySku: registrySku
    useExistingEnvironment: useExistingEnvironment
    existingEnvironmentResourceGroup: existingEnvironmentResourceGroup
    extraEnv: [
      {
        // Matches ConnectionStrings:OrderFulfillment in appsettings.json — since Day 25,
        // OrderingModule.cs actually switches to UseSqlServer when the connection string looks
        // like this one ("Server=tcp:..."), so this is load-bearing now, not just provisioned
        // ahead of the code.
        name: 'ConnectionStrings__OrderFulfillment'
        value: sql.outputs.sqlConnectionStringNoCredentials
      }
      {
        name: 'ServiceBus__Namespace'
        value: serviceBus.outputs.serviceBusNamespaceFqdn
      }
      {
        name: 'ServiceBus__Topic'
        value: serviceBus.outputs.topicName
      }
      {
        // Neither of these is a secret — see the entraTenantId/entraAudience param comments.
        name: 'Entra__TenantId'
        value: entraTenantId
      }
      {
        name: 'Entra__Audience'
        value: entraAudience
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: appInsights.outputs.connectionString
      }
    ]
  }
}

module serviceBusAccess 'modules/servicebus-access.bicep' = {
  name: 'orderfulfillment-servicebus-access'
  scope: resourceGroup
  params: {
    namespaceName: serviceBus.outputs.serviceBusNamespaceName
    principalId: api.outputs.principalId
  }
}

output containerAppFqdn string = api.outputs.containerAppFqdn
output registryLoginServer string = api.outputs.registryLoginServer
output targetImage string = api.outputs.targetImage
output appInsightsName string = appInsights.outputs.appInsightsName
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output serviceBusNamespaceFqdn string = serviceBus.outputs.serviceBusNamespaceFqdn
output serviceBusSubscriptionNames array = serviceBus.outputs.subscriptionNames
