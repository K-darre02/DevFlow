@description('Base name used to derive the workspace name.')
param baseName string

param location string

resource workspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${baseName}-logs'
  location: location
  properties: {
    sku: {
      // Free-tier-equivalent: pay-as-you-go ingestion, no capacity reservation
      // commitment — appropriate for a portfolio-scale deployment's log volume.
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

output workspaceId string = workspace.id
