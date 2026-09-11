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
// "EventType" message property. Known gap, called out rather than hidden: nothing publishes
// with that property set yet — OutboxIntegrationEventPublisher is still in-process
// (InProcessMessageBus), not wired to this namespace. A future Azure Service Bus-backed
// IMessageBus implementation needs to stamp ApplicationProperties["EventType"] =
// event.GetType().Name on send for this filter to actually route anything; until then this
// namespace is provisioned and validated, not yet load-bearing for the running app.
resource subscriptionFilters 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2022-01-01-preview' = [
  for i in range(0, length(subscriptionNames)): {
    parent: subscriptions[i]
    name: '$Default'
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
