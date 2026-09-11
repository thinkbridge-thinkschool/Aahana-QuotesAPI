targetScope = 'resourceGroup'

@description('Azure region for every resource this module creates.')
param location string

@description('Name of the Container Apps managed environment.')
param environmentName string

@description('Name of the Container App running the OrderFulfillment.Api host.')
param containerAppName string

@description('Image tag to deploy once the real image has been pushed (see bootstrapImage below).')
param containerImageTag string

@description('Globally-unique name for the Azure Container Registry.')
param registryName string

@description('Extra environment variables the composition root wires in — SQL/Service Bus connection info from the other modules. Kept generic so this module never needs to know those modules exist.')
param extraEnv array = []

@description('vCPU allocated to each replica. Must be one of the Container Apps-supported increments (0.25, 0.5, 0.75, 1, ...).')
param containerAppCpu string = '0.5'

param containerAppMemory string = '1Gi'
param minReplicas int = 1
param maxReplicas int = 3

@allowed(['Basic', 'Standard', 'Premium'])
@description('Basic is sufficient for dev; prod should move to Standard once image geo-replication or higher throughput is needed.')
param registrySku string = 'Basic'

// The container app's first revision is created before the AcrPull role assignment below
// (which depends on the app's own identity) can exist or propagate, so it can't pull a
// private-registry image on its very first revision. Bootstrap with a public placeholder
// image; a follow-up `az containerapp update` swaps in the real ACR image once AcrPull is
// active. Same trick used by the QuotesApi infra this module was modeled on.
var bootstrapImage = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${environmentName}-logs'
  location: location
  properties: {
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource containerAppEnvironment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: registryName
  location: location
  sku: {
    name: registrySku
  }
  properties: {
    adminUserEnabled: false
  }
}

resource containerApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: containerAppName
          image: bootstrapImage
          resources: {
            cpu: json(containerAppCpu)
            memory: containerAppMemory
          }
          env: concat(
            [
              {
                name: 'ASPNETCORE_URLS'
                value: 'http://+:8080'
              }
            ],
            extraEnv
          )
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

// Lets the container app pull images using its own system-assigned identity — no registry
// username/password/secret is stored anywhere.
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, containerApp.id, 'AcrPull')
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '7f951dda-4ed3-4680-a7ca-43fe172d538d'
    )
    principalId: containerApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

@description('The Container App system-assigned identity — main.bicep grants this principal access to SQL/Service Bus, so neither module needs a secret handed to the app.')
output principalId string = containerApp.identity.principalId

output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output registryLoginServer string = registry.properties.loginServer
output targetImage string = '${registry.properties.loginServer}/${containerAppName}:${containerImageTag}'
