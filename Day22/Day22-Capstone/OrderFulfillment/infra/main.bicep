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
    extraEnv: [
      {
        // Matches ConnectionStrings:OrderFulfillment in appsettings.json — Ordering.Infrastructure
        // still targets Sqlite today (see modules/sql.bicep's comment); this is provisioned ahead
        // of that code change, not yet load-bearing.
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
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output serviceBusNamespaceFqdn string = serviceBus.outputs.serviceBusNamespaceFqdn
output serviceBusSubscriptionNames array = serviceBus.outputs.subscriptionNames
