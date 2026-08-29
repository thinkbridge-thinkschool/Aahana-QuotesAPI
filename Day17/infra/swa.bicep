// Day 17: Static Web App + Managed-Identity Function backend for the
// Angular frontend. Deploys into the existing 'thinkschool-rg' resource
// group created by ../../infra/main.bicep for the Week-1 QuotesApi.
targetScope = 'resourceGroup'

@description('Azure region for the Static Web App (SWA has a restricted region list).')
param swaLocation string = 'eastasia'

@description('Region for the Function App, storage account, and plan.')
param location string = 'centralindia'

param staticSiteName string = 'quotes-frontend'
param functionAppName string = 'quotes-frontend-api'
param storageAccountName string = 'quotesfrontendstg'
param customDomainName string = ''

@description('The Week-1 QuotesApi base URL, e.g. https://quotes-api.<suffix>.centralindia.azurecontainerapps.io')
param quotesApiBaseUrl string

@description('The QuotesApi Entra app registration audience, e.g. api://953b5bcb-682b-47b4-a116-8936323f5bec')
param quotesApiEntraAudience string

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowSharedKeyAccess: true
  }
}

resource functionPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${functionAppName}-plan'
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          name: 'QuotesApi__BaseUrl'
          value: quotesApiBaseUrl
        }
        {
          name: 'QuotesApi__EntraAudience'
          value: quotesApiEntraAudience
        }
      ]
    }
  }
}

// Grants the function's system-assigned identity access to the storage
// account it needs for the Functions runtime, again with no key/secret.
resource storageBlobDataOwnerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, 'StorageBlobDataOwner')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
    )
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource staticSite 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticSiteName
  location: swaLocation
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    stagingEnvironmentPolicy: 'Enabled'
    allowConfigFileUpdates: true
  }
}

resource linkedBackend 'Microsoft.Web/staticSites/linkedBackends@2023-12-01' = {
  parent: staticSite
  name: 'quotes-frontend-api-backend'
  properties: {
    backendResourceId: functionApp.id
    region: location
  }
}

resource customDomain 'Microsoft.Web/staticSites/customDomains@2023-12-01' =
  if (!empty(customDomainName)) {
    parent: staticSite
    name: customDomainName
  }

output staticWebAppDefaultHostname string = staticSite.properties.defaultHostname
output functionAppPrincipalId string = functionApp.identity.principalId
output functionAppName string = functionApp.name
