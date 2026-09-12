targetScope = 'resourceGroup'

@description('Azure region for the Log Analytics workspace, Application Insights component, and alert rule.')
param location string

@description('Name of the Log Analytics workspace backing Application Insights.')
param logAnalyticsWorkspaceName string

@description('Name of the Application Insights component.')
param appInsightsName string

@description('Failed requests within errorRateWindow before the alert fires.')
param errorRateThreshold int = 5

@description('ISO 8601 duration both the alert\'s evaluation frequency and lookback window use.')
param errorRateWindow string = 'PT5M'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

// Not a secret in the same sense as a DB password or a Service Bus key: this connection string
// only lets a caller SEND telemetry to this one Application Insights resource, not read it back or
// reach anything else in the subscription — Microsoft's own guidance treats it as low-sensitivity
// configuration, which is why it's wired as a plain container env var below (main.bicep) rather
// than something requiring @secure()/Key Vault.
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

@description('Fires when the API logs more than errorRateThreshold failed requests inside errorRateWindow — the KQL is the same query used interactively (see DAY26-APP-INSIGHTS-KQL.md).')
resource highErrorRateAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${appInsightsName}-high-error-rate'
  location: location
  properties: {
    displayName: 'High error rate — OrderFulfillment API'
    description: 'Fires when the API logs more than ${errorRateThreshold} failed requests in ${errorRateWindow}.'
    severity: 2
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: errorRateWindow
    windowSize: errorRateWindow
    criteria: {
      allOf: [
        {
          query: 'requests | where success == false | summarize FailedCount = count()'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: errorRateThreshold
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
  }
}

output connectionString string = appInsights.properties.ConnectionString
output appInsightsName string = appInsights.name
