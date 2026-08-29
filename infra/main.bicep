targetScope = 'subscription'

param location string = 'centralindia'
param resourceGroupName string = 'thinkschool-rg'
param environmentName string = 'thinkschool-env'
param containerAppName string = 'quotes-api'
param containerImageTag string = '0.1.0'

@secure()
param jwtSigningKey string

param corsAllowedOrigins array = []

resource resourceGroup 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: resourceGroupName
  location: location
}

// ACR names are globally unique across Azure, so derive one from the
// subscription instead of hardcoding a name someone else may already own.
var registryName = 'quotesacr${uniqueString(subscription().subscriptionId, resourceGroupName)}'

module resources 'resources.bicep' = {
  name: 'quotes-api-resources'
  scope: resourceGroup
  params: {
    location: location
    environmentName: environmentName
    containerAppName: containerAppName
    containerImageTag: containerImageTag
    registryName: registryName
    jwtSigningKey: jwtSigningKey
    corsAllowedOrigins: corsAllowedOrigins
  }
}

output containerAppFqdn string = resources.outputs.containerAppFqdn
output registryLoginServer string = resources.outputs.registryLoginServer
output targetImage string = resources.outputs.targetImage
