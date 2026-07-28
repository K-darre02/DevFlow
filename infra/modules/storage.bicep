@minLength(3)
@description('Base name used to derive the storage account name (must be globally unique, lowercase alphanumeric only, max 24 chars).')
param baseName string

param location string

@description('Matches AzureBlobStorageOptions.ContainerName\'s default (src/DevFlow.Infrastructure/Storage/AzureBlobStorageOptions.cs) — pre-created here for clarity, though AzureBlobStorageService also creates it on first upload if missing.')
param containerName string = 'task-attachments'

// Storage account names allow no hyphens and must be <= 24 chars — baseName
// is stripped of hyphens here since it's shared with other modules that do
// allow them.
var storageAccountName = toLower(replace('${baseName}sa', '-', ''))

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  // take() below trips BCP334 (can't statically prove the string is long
  // enough) — a known Bicep analyzer limitation through toLower/replace
  // chains, not a real risk: baseName is always 'devflow-...' at every call
  // site in main.bicep.
  name: take(storageAccountName, 24)
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

// PublicAccess 'None' at the container level too — belt-and-suspenders on
// top of the account-level allowBlobPublicAccess: false above. Matches
// AzureBlobStorageService's own PublicAccessType.None
// (docs/devflow/05-technical-decisions.md ADR 10: object keys + short-lived
// SAS URLs only, never a public blob URL).
resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

output storageAccountName string = storageAccount.name

@secure()
@description('Composed here (where listKeys() has a direct resource reference) rather than in main.bicep, and marked @secure() so it is redacted from deployment operation history — consumed by main.bicep only to write into Key Vault (modules/keyVaultSecrets.bicep), never surfaced anywhere else.')
output connectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
