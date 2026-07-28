@description('Base name used to derive the Static Web App name.')
param baseName string

@description('Static Web Apps is only available in a subset of regions, independent of the primary deployment location — see main.bicep\'s staticWebAppLocation parameter.')
param location string

@description('Resource ID of the App Service (API) to proxy /api/* to under the Static Web App\'s own domain.')
param apiResourceId string

@description('Region the linked App Service actually lives in — required by the linkedBackends sub-resource, separate from the Static Web App\'s own location above.')
param apiLocation string

// Standard SKU is required for the linked-backend feature used below — Free
// tier only supports SWA's own built-in (Azure Functions) managed API, not
// an existing App Service. This is what makes the same-origin /api/*
// proxying in docs/devflow/05-technical-decisions.md ADR 6 possible without
// the SPA and API being on unrelated default domains.
resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: '${baseName}-web'
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    // The GitHub Actions deploy workflow pushes the built SPA directly via
    // the SWA deploy action/token rather than SWA's own repository
    // integration — no repositoryUrl/branch/buildProperties needed here.
    provider: 'None'
  }
}

resource linkedBackend 'Microsoft.Web/staticSites/linkedBackends@2023-12-01' = {
  parent: staticWebApp
  name: 'devflow-api'
  properties: {
    backendResourceId: apiResourceId
    region: apiLocation
  }
}

output name string = staticWebApp.name
output defaultHostname string = staticWebApp.properties.defaultHostname
