@description('Base name used to derive the Key Vault name (max 24 chars, alphanumeric + hyphens).')
param baseName string

param location string

@description('Azure AD tenant ID the vault belongs to.')
param tenantId string

// Vault names are globally unique across all of Azure — baseName alone
// (which already includes a uniqueString suffix, see main.bicep) keeps this
// short enough to stay under the 24-char limit.
var vaultName = '${baseName}-kv'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenantId
    // RBAC over the legacy access-policy model — secret access is granted
    // via a "Key Vault Secrets User" role assignment on App Service's
    // managed identity (see main.bicep), consistent with how every other
    // permission in this system is modeled (role-based, not per-resource
    // ad hoc grants).
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
  }
}

output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
output vaultResourceId string = vault.id
