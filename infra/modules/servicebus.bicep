targetScope = 'resourceGroup'

@description('Azure region for the Service Bus namespace.')
param location string

@description('Globally-unique Service Bus namespace name.')
param namespaceName string

param queueName string = 'quote-events'

@allowed(['Basic', 'Standard', 'Premium'])
@description('Basic covers a single queue + dead-lettering, which is all this slice needs; Standard adds topics/subscriptions if fan-out notifications are added later.')
param skuName string = 'Basic'

@description('Messages that fail this many delivery attempts land in the dead-letter sub-queue instead of retrying forever.')
param maxDeliveryCount int = 5

@description('ISO 8601 duration a message can sit unprocessed before it is considered expired and dead-lettered.')
param messageTimeToLive string = 'P14D'

// RBAC-only: no SAS connection string exists anywhere in this template's outputs. The API
// authenticates as itself (its managed identity), the same passwordless pattern used for ACR
// pulls (modules/api.bicep) and SQL auth (modules/sql.bicep).
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2023-01-01-preview' = {
  name: namespaceName
  location: location
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2023-01-01-preview' = {
  parent: serviceBusNamespace
  name: queueName
  properties: {
    deadLetteringOnMessageExpiration: true
    maxDeliveryCount: maxDeliveryCount
    defaultMessageTimeToLive: messageTimeToLive
  }
}

// Granting a consumer access (e.g. the API's managed identity) is deliberately not done in this
// module — see modules/servicebus-access.bicep and the comment in main.bicep for why: this
// module's own output (the namespace name/FQDN) is what the API needs to even start, so an RBAC
// assignment in here that took the API's principalId as input would make the two modules depend
// on each other's outputs in both directions, which Bicep's module graph can't resolve.
output serviceBusNamespaceName string = serviceBusNamespace.name
output serviceBusNamespaceFqdn string = '${serviceBusNamespace.name}.servicebus.windows.net'
output queueName string = queue.name
