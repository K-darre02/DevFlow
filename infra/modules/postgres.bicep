@description('Base name used to derive the PostgreSQL Flexible Server name.')
param baseName string

param location string

param administratorLogin string

@secure()
param administratorLoginPassword string

@description('Database name — matches DevFlowDbContext\'s expected database, same as the local docker-compose.yml Postgres container.')
param databaseName string = 'devflow'

// Burstable B1ms: smallest tier that supports Flexible Server's full
// feature set (matches docs/devflow/05-technical-decisions.md ADR 11's
// Postgres choice) — right-sized for a portfolio-scale deployment, not a
// production workload with real concurrent load.
resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2022-12-01' = {
  name: '${baseName}-psql'
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2022-12-01' = {
  parent: server
  name: databaseName
}

// Postgres Flexible Server is provisioned with public network access (no
// VNet integration — out of scope for a portfolio-scale deployment); this
// rule is what actually lets App Service reach it, since App Service isn't
// itself VNet-integrated here either. The special 0.0.0.0-0.0.0.0 range is
// Azure's documented "allow access from any Azure-internal IP" rule, not a
// literal open-to-the-internet rule.
resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2022-12-01' = {
  parent: server
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
