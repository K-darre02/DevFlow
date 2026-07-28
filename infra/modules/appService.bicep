@description('Base name used to derive the App Service plan and site names.')
param baseName string

param location string

@description('Key Vault URI — set as the KeyVault:Uri app setting Program.cs checks for at startup (see src/DevFlow.Api/Program.cs).')
param keyVaultUri string

// B1: smallest Linux tier that supports "Always On" (Free/Shared don't) —
// load-bearing here because the in-process background worker
// (docs/devflow/01-architecture.md §6) depends on the process staying warm
// between requests, not just autoscale/perf headroom.
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${baseName}-plan'
  location: location
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: '${baseName}-api'
  location: location
  identity: {
    // No client secret to manage/rotate — Program.cs's DefaultAzureCredential
    // resolves to this identity automatically when running in Azure.
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'KeyVault__Uri'
          value: keyVaultUri
        }
        {
          // Zip-deploy package is mounted read-only rather than extracted —
          // the standard, recommended App Service deployment mode for the
          // GitHub Actions publish step (see .github/workflows/deploy.yml).
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
      ]
    }
  }
}

output name string = site.name
output defaultHostName string = site.properties.defaultHostName
output principalId string = site.identity.principalId
