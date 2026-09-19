targetScope = 'resourceGroup'

@description('Azure region for the Service Bus namespace.')
param location string

@description('Globally-unique Service Bus namespace name.')
param namespaceName string

param topicName string = 'order-fulfillment-events'

@allowed(['Basic', 'Standard', 'Premium'])
@description('Topics/subscriptions require Standard or above — Basic (queue-only) does not support them, unlike QuotesApi\'s single-queue infra.')
param skuName string = 'Standard'

@description('Messages that fail this many delivery attempts land in the dead-letter sub-queue instead of retrying forever.')
param maxDeliveryCount int = 5

@description('ISO 8601 duration a message can sit unprocessed before it is considered expired and dead-lettered.')
param messageTimeToLive string = 'P14D'

@description('Day 27: false closes the namespace\'s public endpoint — only traffic through the private endpoint below can reach it.')
param allowPublicNetworkAccess bool = false

@description('Subnet to place the private endpoint NIC in — modules/network.bicep\'s dedicated private-endpoints subnet.')
param privateEndpointSubnetId string = ''

@description('Private DNS Zone for privatelink.servicebus.windows.net — modules/network.bicep\'s output.')
param privateDnsZoneId string = ''

// RBAC-only: no SAS connection string exists anywhere in this template's outputs. The API
// authenticates as itself (its managed identity) — same passwordless pattern as ACR pulls
// (modules/api.bicep) and SQL auth (modules/sql.bicep).
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
    publicNetworkAccess: allowPublicNetworkAccess ? 'Enabled' : 'Disabled'
  }
}

@description('Day 27: private connectivity for the data tier — see network.bicep for the VNet/subnet/DNS zone this attaches to.')
resource serviceBusPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = if (!empty(privateEndpointSubnetId)) {
  name: '${namespaceName}-pe'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${namespaceName}-plsc'
        properties: {
          privateLinkServiceId: serviceBusNamespace.id
          groupIds: ['namespace']
        }
      }
    ]
  }
}

resource serviceBusPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = if (!empty(privateEndpointSubnetId)) {
  parent: serviceBusPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-servicebus-windows-net'
        properties: {
          privateDnsZoneId: privateDnsZoneId
        }
      }
    ]
  }
}

// One topic, not a queue: DESIGN.md's async flow is genuine pub/sub — Ordering publishes
// OrderPlaced/OrderConfirmed/OrderCancelled, and Inventory, Payments, Shipping and
// Notifications each independently react to a different subset of that same stream. A queue
// only lets one logical consumer group drain a message; a topic + one subscription per
// module is what actually matches "every module that cares gets its own copy."
resource topic 'Microsoft.ServiceBus/namespaces/topics@2023-01-01-preview' = {
  parent: serviceBusNamespace
  name: topicName
  properties: {
    defaultMessageTimeToLive: messageTimeToLive
  }
}

// Which module gets which events — lifted straight from the IIntegrationEventHandler<T>
// registrations in each module's {Module}Module.cs (see DESIGN.md for the full flow).
var subscriptionEventMap = {
  Inventory: ['OrderPlaced', 'OrderCancelled']
  Payments: ['OrderConfirmed']
  Shipping: ['OrderPaymentReceived']
  Ordering: ['StockReserved', 'StockReservationFailed', 'PaymentCaptured', 'PaymentFailed', 'ShipmentDispatched']
  Notifications: ['OrderPlaced', 'OrderConfirmed', 'OrderCancelled', 'PaymentFailed', 'ShipmentDispatched']
}
var subscriptionNames = items(subscriptionEventMap)

@description('One subscription per subscribing module — each only ever sees the event types listed in subscriptionEventMap, not the whole topic.')
resource subscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2023-01-01-preview' = [
  for i in range(0, length(subscriptionNames)): {
    parent: topic
    name: subscriptionNames[i].key
    properties: {
      deadLetteringOnMessageExpiration: true
      deadLetteringOnFilterEvaluationExceptions: true
      maxDeliveryCount: maxDeliveryCount
      defaultMessageTimeToLive: messageTimeToLive
    }
  }
]

// Overrides each subscription's default (match-everything) rule with a SQL filter on a custom
// "EventType" message property, stamped by AzureServiceBusMessageBus.PublishAsync on every send.
// Day 32: confirmed live that this rule resource had never actually been deployed to Azure at
// all (every subscription's rule list came back empty — the successful servicebus.bicep deploy
// predates this resource being added, and no full redeploy has completed since, because the
// AcrPull deadlock blocked every azd provision attempt past this module). Also confirmed live,
// while fixing that: a rule literally named "$Default" silently fails to persist through this
// exact API path — `rule create` reports success and echoes the correct filter back, but an
// immediate `rule show` for that same name returns "Rule does not exist", every time, no error
// on the write side at all. A genuine, reproducible Service Bus/CLI quirk around that one
// reserved name, not a typo — switched to a plain, non-reserved rule name instead, which
// persists immediately and correctly. Also fixed the same session: the stamped value itself
// didn't match this filter's short names at all (BuildingBlocks.Infrastructure/
// AzureServiceBusMessageBus.cs was sending the full CLR type name, e.g.
// "OrderPlacedIntegrationEvent", against a filter checking for "OrderPlaced") — three independent
// things had to all be wrong at once for nothing to ever have worked, and all three were.
// Verified live end-to-end after all three fixes: a real order placed against the deployed API
// cascaded through the real saga (Confirmed -> PaymentReceived -> Shipped) over this exact
// namespace, for the first time in this capstone's history.
resource subscriptionFilters 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2022-01-01-preview' = [
  for i in range(0, length(subscriptionNames)): {
    parent: subscriptions[i]
    name: 'EventTypeFilter'
    properties: {
      filterType: 'SqlFilter'
      sqlFilter: {
        sqlExpression: 'EventType IN (${join(map(subscriptionNames[i].value, t => '\'${t}\''), ', ')})'
      }
    }
  }
]

output serviceBusNamespaceName string = serviceBusNamespace.name
output serviceBusNamespaceFqdn string = '${serviceBusNamespace.name}.servicebus.windows.net'
output topicName string = topic.name
output subscriptionNames array = [for entry in subscriptionNames: entry.key]
