@description('Base name used to derive the Application Insights resource name.')
param baseName string

param location string

@description('Resource ID of the Log Analytics workspace this workspace-based Application Insights resource is backed by.')
param logAnalyticsWorkspaceId string

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${baseName}-appinsights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspaceId
    IngestionMode: 'LogAnalytics'
  }
}

@description('Consumed by Program.cs (UseAzureMonitor) via the ApplicationInsights--ConnectionString Key Vault secret — see infra/modules/keyVaultSecrets.bicep.')
output connectionString string = appInsights.properties.ConnectionString
