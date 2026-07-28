@description('Name of an already-provisioned Key Vault to grant read access on.')
param vaultName string

@description('principalId of the identity to grant access to (App Service\'s system-assigned managed identity).')
param principalId string

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: vaultName
}

// "Key Vault Secrets User" (4633458b-17de-408a-b874-0445c86b69e6) is a
// built-in Azure role scoped to read-only secret access — the App Service
// identity never needs to create/list/delete secrets, only read the four
// this system writes (see modules/keyVaultSecrets.bicep), so this is the
// least-privileged built-in role that covers it.
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, principalId, keyVaultSecretsUserRoleId)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
