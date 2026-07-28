// Provisions the production environment described in
// docs/devflow/01-architecture.md §1-2 and 05-technical-decisions.md: App
// Service (API, self-hosted SignalR — see appService.bicep's comment),
// Static Web App with a linked backend (SPA, same-origin per ADR 6),
// PostgreSQL Flexible Server, Blob Storage, Key Vault, and workspace-based
// Application Insights. Deployed at resource-group scope:
//
//   az deployment group create \
//     --resource-group <rg> \
//     --template-file infra/main.bicep \
//     --parameters infra/main.parameters.json \
//     --parameters postgresAdminPassword=<secret> jwtSigningKey=<secret>
//
// See README.md's Deployment section for the full provisioning walkthrough
// and the GitHub Actions secrets this deployment's outputs feed into.

@description('Logical environment name — used to derive resource names and to seed the uniqueness token below.')
param environmentName string = 'production'

@description('Primary Azure region for App Service, PostgreSQL, Storage, Key Vault, and Application Insights.')
param location string = resourceGroup().location

@description('Azure Static Web Apps is only available in a subset of regions, independent of the primary location above.')
param staticWebAppLocation string = 'eastus2'

@description('PostgreSQL Flexible Server administrator username.')
param postgresAdminUsername string = 'devflowadmin'

@secure()
@description('PostgreSQL Flexible Server administrator password — supply at deploy time, never committed (see .github/workflows/deploy.yml, secret AZURE_POSTGRES_ADMIN_PASSWORD).')
param postgresAdminPassword string

@secure()
@description('JWT signing key written into Key Vault as Jwt--SigningKey — supply at deploy time, never committed (see .github/workflows/deploy.yml, secret JWT_SIGNING_KEY).')
param jwtSigningKey string

// Deterministic per resource-group + environment, so re-running this
// deployment against the same resource group always targets the same
// resources instead of creating new ones each time. Seeding with
// environmentName (not just resourceGroup().id) lets multiple environments
// share one resource group without name collisions, if that's ever needed.
var resourceToken = uniqueString(resourceGroup().id, environmentName)

// The descriptive, human-readable base used for resources with generous
// name-length limits (App Service, Postgres, Application Insights, Log
// Analytics, Static Web Apps — all well under their respective 60-63 char
// caps even with the full environment name spelled out).
var baseName = 'devflow-${environmentName}-${resourceToken}'

// Storage accounts (24 chars, no hyphens) and Key Vaults (24 chars) have
// much tighter limits — a shorter, token-only base avoids truncating away
// the part of the name that actually guarantees uniqueness.
var shortBaseName = 'devflow-${take(resourceToken, 10)}'

module logAnalytics 'modules/logAnalytics.bicep' = {
  name: 'logAnalytics'
  params: {
    baseName: baseName
    location: location
  }
}

module appInsights 'modules/appInsights.bicep' = {
  name: 'appInsights'
  params: {
    baseName: baseName
    location: location
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
  }
}

module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault'
  params: {
    baseName: shortBaseName
    location: location
    tenantId: subscription().tenantId
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    baseName: baseName
    location: location
    administratorLogin: postgresAdminUsername
    administratorLoginPassword: postgresAdminPassword
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    baseName: shortBaseName
    location: location
  }
}

// The URI's form (https://{vaultName}.vault.azure.net/) is deterministic
// from the vault name alone, so this can be computed here and handed to
// appService immediately — avoiding a circular dependency where appService
// would otherwise need keyVault's output, and keyVault would need
// appService's managed-identity principalId (for keyVaultAccess below) at
// the same time.
var keyVaultUri = 'https://${shortBaseName}-kv${environment().suffixes.keyvaultDns}/'

module appService 'modules/appService.bicep' = {
  name: 'appService'
  params: {
    baseName: baseName
    location: location
    keyVaultUri: keyVaultUri
  }
}

module keyVaultAccess 'modules/keyVaultAccess.bicep' = {
  name: 'keyVaultAccess'
  params: {
    vaultName: keyVault.outputs.vaultName
    principalId: appService.outputs.principalId
  }
}

module keyVaultSecrets 'modules/keyVaultSecrets.bicep' = {
  name: 'keyVaultSecrets'
  params: {
    vaultName: keyVault.outputs.vaultName
    jwtSigningKey: jwtSigningKey
    // Azure Database for PostgreSQL Flexible Server requires TLS by default;
    // Trust Server Certificate=true is Npgsql's documented setting for
    // connecting against Azure's managed certificate without pinning a
    // custom root CA (Azure rotates it without any migration/setting change
    // needed on the app's side).
    databaseConnectionString: 'Host=${postgres.outputs.serverFqdn};Port=5432;Database=${postgres.outputs.databaseName};Username=${postgresAdminUsername};Password=${postgresAdminPassword};SSL Mode=Require;Trust Server Certificate=true'
    storageConnectionString: storage.outputs.connectionString
    appInsightsConnectionString: appInsights.outputs.connectionString
  }
}

module staticWebApp 'modules/staticWebApp.bicep' = {
  name: 'staticWebApp'
  params: {
    baseName: baseName
    location: staticWebAppLocation
    // Referencing appService.outputs.name here is what makes Bicep infer
    // the dependency on the appService module automatically — no explicit
    // dependsOn needed.
    apiResourceId: resourceId('Microsoft.Web/sites', appService.outputs.name)
    apiLocation: location
  }
}

output apiName string = appService.outputs.name
output apiDefaultHostName string = appService.outputs.defaultHostName
output staticWebAppName string = staticWebApp.outputs.name
output staticWebAppDefaultHostname string = staticWebApp.outputs.defaultHostname
output keyVaultName string = keyVault.outputs.vaultName
output postgresServerFqdn string = postgres.outputs.serverFqdn
output storageAccountName string = storage.outputs.storageAccountName
output appInsightsName string = '${baseName}-appinsights'
