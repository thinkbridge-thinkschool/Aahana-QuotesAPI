targetScope = 'resourceGroup'

@description('Azure region for the SQL server and database.')
param location string

@description('Globally-unique logical SQL server name.')
param sqlServerName string

@description('Only the Ordering module is EF Core/SQL-backed at kickoff scope — Inventory, Payments, Shipping and Notifications are in-memory (see DESIGN.md).')
param sqlDatabaseName string = 'orderfulfillmentdb'

@description('Display name of the Entra ID principal (user or group) that administers this server.')
param aadAdminLogin string

@description('Object ID of that Entra ID principal.')
param aadAdminObjectId string

@description('Basic for dev; a real tier (e.g. GP_S_Gen5_2 — General Purpose, serverless, 2 vCores) for prod.')
param skuName string = 'Basic'

@allowed(['Basic', 'Standard', 'GeneralPurpose', 'GeneralPurpose serverless', 'BusinessCritical'])
param skuTier string = 'Basic'

@description('Lets Azure-hosted resources (the Container App) reach this server without listing individual outbound IPs. Azure SQL still requires TLS + AAD auth on top of this — it only opens the firewall, not the database.')
param allowAzureServices bool = true

@description('Day 27: false closes the server\'s public endpoint entirely — only traffic through the private endpoint below can reach it. The firewall rule above becomes irrelevant (there is no public listener left for it to allow into) but is left in the template rather than deleted, since flipping this back to true for a lower environment should not also require re-adding the firewall rule by hand.')
param allowPublicNetworkAccess bool = false

@description('Subnet to place the private endpoint NIC in — modules/network.bicep\'s dedicated private-endpoints subnet.')
param privateEndpointSubnetId string = ''

@description('Private DNS Zone for privatelink.database.windows.net — modules/network.bicep\'s output.')
param privateDnsZoneId string = ''

// Entra-ID-only authentication: no SQL login/password exists on this server at all, matching
// the passwordless pattern used for the API's registry pull (see modules/api.bicep).
// Known gap, not silently glossed over: granting the Container App's managed identity a
// contained database user (CREATE USER ... FROM EXTERNAL PROVIDER) is SQL DML, not an ARM/Bicep
// resource — it isn't automated here and would need a deploymentScript or a manual/pipeline step
// after this template applies. Ordering.Infrastructure also still targets Sqlite
// (UseSqlite in OrderingModule.cs) — swapping it for UseSqlServer against this server is
// tracked as follow-up work, not done in this pass (see DESIGN.md, "Known gaps").
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      login: aadAdminLogin
      sid: aadAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: allowPublicNetworkAccess ? 'Enabled' : 'Disabled'
  }
}

@description('Day 27: private connectivity for the data tier — see network.bicep for the VNet/subnet/DNS zone this attaches to.')
resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = if (!empty(privateEndpointSubnetId)) {
  name: '${sqlServerName}-pe'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${sqlServerName}-plsc'
        properties: {
          privateLinkServiceId: sqlServer.id
          groupIds: ['sqlServer']
        }
      }
    ]
  }
}

resource sqlPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = if (!empty(privateEndpointSubnetId)) {
  parent: sqlPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-database-windows-net'
        properties: {
          privateDnsZoneId: privateDnsZoneId
        }
      }
    ]
  }
}

// Named without "Windows" — Azure rejects that as a reserved word in a resource name (only
// caught by a real deployment; az deployment sub validate/what-if did not flag it).
//
// Day 27: also gated on allowPublicNetworkAccess, not just allowAzureServices — a real deployment
// with public access disabled fails outright (DenyPublicEndpointEnabled: "Unable to create or
// modify firewall rules when public network interface for the server is disabled"). A firewall
// rule is meaningless once there is no public listener left for it to filter.
resource allowAzureServicesRule 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (allowAzureServices && allowPublicNetworkAccess) {
  parent: sqlServer
  name: 'AllowAllAzureServicesIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    zoneRedundant: false
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name

@description('An Active-Directory-Default connection string — no password, relies on the caller managed identity plus the contained-user grant described above.')
output sqlConnectionStringNoCredentials string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabase.name};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;'
