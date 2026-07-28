@description('Name of an already-provisioned Key Vault (see modules/keyVault.bicep) to write secrets into.')
param vaultName string

@secure()
param jwtSigningKey string

@secure()
param databaseConnectionString string

@secure()
param storageConnectionString string

@secure()
param appInsightsConnectionString string

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: vaultName
}

// Secret names use "--" where application configuration would use ":" —
// the Azure Key Vault configuration provider (Azure.Extensions.AspNetCore.Configuration.Secrets,
// wired up in src/DevFlow.Api/Program.cs) translates "--" to ":"
// automatically, since ":" itself isn't a legal Key Vault secret name
// character.
resource jwtSigningKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'Jwt--SigningKey'
  properties: {
    value: jwtSigningKey
  }
}

resource databaseConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'ConnectionStrings--DevFlowDatabase'
  properties: {
    value: databaseConnectionString
  }
}

resource storageConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'Storage--Azure--ConnectionString'
  properties: {
    value: storageConnectionString
  }
}

resource appInsightsConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'ApplicationInsights--ConnectionString'
  properties: {
    value: appInsightsConnectionString
  }
}
