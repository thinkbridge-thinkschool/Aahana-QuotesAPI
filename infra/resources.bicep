targetScope = 'resourceGroup'

param location string
param environmentName string
param containerAppName string
param containerImageTag string
param registryName string

// The container app's first revision is created before the AcrPull role
// assignment below (which depends on the app's own identity) can exist or
// propagate, so it can't pull a private-registry image on its very first
// revision. Bootstrap with a public placeholder image; a follow-up
// `az containerapp update` swaps in the real ACR image once AcrPull is
// active.
var bootstrapImage = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@secure()
param jwtSigningKey string

param corsAllowedOrigins array = []

var corsEnv = [
  for (origin, i) in corsAllowedOrigins: {
    name: 'Cors__AllowedOrigins__${i}'
    value: origin
  }
]

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
    name: 'Basic'
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
      secrets: [
        {
          name: 'jwt-signing-key'
          value: jwtSigningKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: containerAppName
          image: bootstrapImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat(
            [
              {
                name: 'ASPNETCORE_URLS'
                value: 'http://+:8080'
              }
              {
                name: 'Jwt__Key'
                secretRef: 'jwt-signing-key'
              }
            ],
            corsEnv
          )
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

// Lets the container app pull images using its own system-assigned
// identity - no registry username/password/secret is stored anywhere.
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

output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output registryLoginServer string = registry.properties.loginServer
output targetImage string = '${registry.properties.loginServer}/${containerAppName}:${containerImageTag}'
