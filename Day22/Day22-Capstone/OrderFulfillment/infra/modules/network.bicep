targetScope = 'resourceGroup'

@description('Azure region for the VNet, subnet, and Private DNS zone links.')
param location string

@description('VNet name.')
param vnetName string

@description('Address space for the VNet — private, unrelated to anything QuotesApi uses, since this VNet lives entirely in the capstone\'s own resource group.')
param vnetAddressPrefix string = '10.20.0.0/16'

@description('Subnet dedicated to private endpoints. privateEndpointNetworkPolicies must be disabled on this subnet — Azure enforces that as a hard requirement for any subnet hosting a private endpoint.')
param privateEndpointSubnetPrefix string = '10.20.1.0/24'

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [vnetAddressPrefix]
    }
    subnets: [
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

// One Private DNS Zone per service that gets a private endpoint below — the exact zone names
// Azure documents for each service (privatelink.<service-fqdn-suffix>) are what make a private
// endpoint's IP resolve automatically instead of requiring a manual hosts-file-style override.
// These are fixed, Azure-documented zone names for Public Cloud, not an environment-specific URL
// that should route through environment() — the linter's no-hardcoded-env-urls rule doesn't have
// a case for "this literal string is supposed to be identical everywhere", hence the suppression.
resource sqlPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  #disable-next-line no-hardcoded-env-urls
  name: 'privatelink.database.windows.net'
  location: 'global'
}

resource serviceBusPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  #disable-next-line no-hardcoded-env-urls
  name: 'privatelink.servicebus.windows.net'
  location: 'global'
}

resource sqlDnsZoneVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: sqlPrivateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}

resource serviceBusDnsZoneVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: serviceBusPrivateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}

output vnetId string = vnet.id
output privateEndpointSubnetId string = vnet.properties.subnets[0].id
output sqlPrivateDnsZoneId string = sqlPrivateDnsZone.id
output serviceBusPrivateDnsZoneId string = serviceBusPrivateDnsZone.id
