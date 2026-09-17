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

@description('This subscription allows only one Container Apps managed environment per region, and QuotesApi already occupies it in Central India (MaxNumberOfRegionalEnvironmentsInSubExceeded — only surfaced by a real deployment, not by what-if/validate). When true, reuse that existing environment instead of creating a new one; isolation between QuotesApi, capstone-dev, and capstone-prod then happens at the Container App level (separate apps/revisions), not the environment level.')
param useExistingEnvironment bool = false

@description('Resource group of the existing environment — only read when useExistingEnvironment is true.')
param existingEnvironmentResourceGroup string = ''

@description('Extra environment variables the composition root wires in — SQL/Service Bus connection info from the other modules. Kept generic so this module never needs to know those modules exist.')
param extraEnv array = []

@description('''
Day 29: without this, the deployed app has no ASPNETCORE_ENVIRONMENT at all, which ASP.NET Core
treats as "Production" — and Day 27's fail-closed auth check (Program.cs) then refuses to start
at all when no real Entra App Registration is configured yet (Day 25's still-open gap), which is
exactly what happened on this dev deployment's first real run. "Development" here is not a
security downgrade of that check; it is the one value the check itself already treats as the
sanctioned place to run unauthenticated (see Program.cs's own comment) — dev's bicepparam sets
this to Development for exactly that reason, prod's leaves the default Production untouched, so
Day 27's hardening stays fully in force there until a real App Registration exists.
''')
param aspnetCoreEnvironment string = 'Production'

@description('vCPU allocated to each replica. Must be one of the Container Apps-supported increments (0.25, 0.5, 0.75, 1, ...).')
param containerAppCpu string = '0.5'

param containerAppMemory string = '1Gi'
param minReplicas int = 1
param maxReplicas int = 3

@allowed(['Basic', 'Standard', 'Premium'])
@description('Basic is sufficient for dev; prod should move to Standard once image geo-replication or higher throughput is needed.')
param registrySku string = 'Basic'

@description('''
Day 29 fix for a real deadlock hit on a live deploy, not a hypothetical: declaring the ACR
registries entry unconditionally in the SAME deployment as the Container App creates a genuine
circular dependency, not just a "first revision can't pull a private image yet" inconvenience.
The Container Apps control plane validates every declared registry credential as part of
provisioning a revision — even one using a public image that never touches that registry — and
kept retrying a 401 (no AcrPull grant exists yet) for the full ~20-minute operation timeout,
so the Container App's own deployment never reached a terminal "Succeeded" state. Because
acrPullRoleAssignment below has an implicit dependency on containerApp (it reads
containerApp.identity.principalId), ARM never even attempted that role assignment once its
dependency's operation failed — so AcrPull could never be granted, which is exactly what was
causing the 401 in the first place. A real, self-reinforcing deadlock, confirmed live: granting
AcrPull to the already-created identity by hand, completely outside this template, is what
finally let the stuck Container App provision.

The fix: keep `registries` empty (and the image on the public bootstrap) until this is explicitly
true. Phase 1 (this param false, the default) — deploy with no registries declared at all, so
there is nothing for the platform to validate and the Container App reaches Succeeded, at which
point acrPullRoleAssignment finally runs. Phase 2 (this param true, after phase 1 has succeeded
and a real image has been pushed) — a second deployment adds the registries entry and swaps in
the real image, now that AcrPull genuinely exists before it's needed.
''')
param acrPullGranted bool = false

// See acrPullGranted above for why this is only used until that phase-2 redeploy happens.
var bootstrapImage = 'mcr.microsoft.com/dotnet/samples:aspnetapp'
var realImage = '${registry.properties.loginServer}/${containerAppName}:${containerImageTag}'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = if (!useExistingEnvironment) {
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

resource containerAppEnvironment 'Microsoft.App/managedEnvironments@2025-01-01' = if (!useExistingEnvironment) {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics!.properties.customerId
        sharedKey: logAnalytics!.listKeys().primarySharedKey
      }
    }
  }
}

// Cross-resource-group reference — no properties of QuotesApi's environment are read or
// written beyond its resource id, which is all a Container App needs to join it.
resource existingEnvironment 'Microsoft.App/managedEnvironments@2025-01-01' existing = if (useExistingEnvironment) {
  name: environmentName
  scope: resourceGroup(existingEnvironmentResourceGroup)
}

var environmentId = useExistingEnvironment ? existingEnvironment.id : containerAppEnvironment.id

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
    managedEnvironmentId: environmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: acrPullGranted
        ? [
            {
              server: registry.properties.loginServer
              identity: 'system'
            }
          ]
        : []
    }
    template: {
      containers: [
        {
          name: containerAppName
          image: acrPullGranted ? realImage : bootstrapImage
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
              {
                name: 'ASPNETCORE_ENVIRONMENT'
                value: aspnetCoreEnvironment
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
