targetScope = 'resourceGroup'

@description('Azure region for the SQL server and database.')
param location string

@description('Globally-unique logical SQL server name.')
param sqlServerName string

param sqlDatabaseName string = 'quotesdb'

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

// Entra-ID-only authentication: no SQL login/password exists on this server at all, matching
// the passwordless pattern already used for the API's registry pull (see modules/api.bicep).
// Known gap, not silently glossed over: granting the Container App's managed identity a
// contained database user (CREATE USER ... FROM EXTERNAL PROVIDER) is SQL DML, not an ARM/Bicep
// resource — it isn't automated here and would need a deploymentScript or a manual/pipeline step
// after this template applies.
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
    publicNetworkAccess: 'Enabled'
  }
}

resource allowAzureServicesRule 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (allowAzureServices) {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
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
