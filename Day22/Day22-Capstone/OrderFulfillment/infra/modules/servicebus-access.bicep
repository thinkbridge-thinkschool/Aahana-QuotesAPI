targetScope = 'resourceGroup'

@description('Name of an already-deployed Service Bus namespace (see modules/servicebus.bicep).')
param namespaceName string

@description('Principal to grant send/receive/manage access to — the API container app managed identity.')
param principalId string

// Deliberately a separate module from servicebus.bicep: this assignment needs the API's
// principalId, which only exists after the api module deploys, but the api module needs
// servicebus.bicep's namespace/topic outputs to even start (they're baked into its container
// env vars). Two modules needing each other's outputs is a cycle Bicep's dependency graph can't
// resolve — splitting the RBAC grant out into its own module, run after both, avoids it:
// main.bicep deploys sql + serviceBus -> api (using their outputs) -> this module (using api's
// output). Same shape as QuotesApi's infra/modules/servicebus-access.bicep.
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2023-01-01-preview' existing = {
  name: namespaceName
}

// Azure Service Bus Data Owner — send + receive + manage, on the whole namespace. A production
// hardening pass would split this into narrower Data Sender/Data Receiver assignments scoped
// per-topic (see Day 27, "Security pass"); one Owner grant is the pragmatic kickoff-scope
// choice, called out rather than silently left broad.
var serviceBusDataOwnerRoleId = '090c5cfd-751d-490a-894a-3ce6f1109419'

resource dataOwnerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespace.id, principalId, 'ServiceBusDataOwner')
  scope: serviceBusNamespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataOwnerRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
