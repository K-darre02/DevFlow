# Infrastructure

Bicep templates provisioning the production environment described in
[`docs/devflow/01-architecture.md`](../docs/devflow/01-architecture.md) §1–2
and [`05-technical-decisions.md`](../docs/devflow/05-technical-decisions.md):
App Service (API), a Static Web App with a linked backend (SPA), PostgreSQL
Flexible Server, Blob Storage, Key Vault, and workspace-based Application
Insights.

This directory contains infrastructure-as-code and is meant to be applied
deliberately, by a human running the command below (or by the `deploy`
GitHub Actions workflow once its secrets are configured — see the repo
root [README.md](../README.md#deployment)). Nothing in this directory runs
on its own.

## One-time provisioning

```bash
az group create --name devflow-production --location eastus

az deployment group create \
  --resource-group devflow-production \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json \
  --parameters postgresAdminPassword="<generate a strong password>" \
  --parameters jwtSigningKey="<generate at least 32 random bytes, base64-encoded>"
```

Both secret parameters are deploy-time-only — they're written into Key
Vault by `modules/keyVaultSecrets.bicep` and never stored in the Bicep
files or parameters.json themselves. Re-running this command against the
same resource group updates the existing resources in place rather than
creating duplicates (`resourceToken` in `main.bicep` is deterministic per
resource group + environment name).

After provisioning, capture the deployment outputs (`apiName`,
`staticWebAppName`, `keyVaultName`, `postgresServerFqdn`,
`storageAccountName`) — the GitHub Actions secrets listed in the root
README's Deployment section are built from these.

## Module layout

| Module | Provisions |
|---|---|
| `logAnalytics.bicep` | Log Analytics workspace backing Application Insights |
| `appInsights.bicep` | Workspace-based Application Insights |
| `keyVault.bicep` | Key Vault, RBAC authorization mode |
| `postgres.bicep` | PostgreSQL Flexible Server (Burstable B1ms) + `devflow` database |
| `storage.bicep` | Storage account + private `task-attachments` container |
| `appService.bicep` | Linux App Service (B1, Always On, system-assigned managed identity) |
| `keyVaultAccess.bicep` | Grants the API's managed identity the "Key Vault Secrets User" role |
| `keyVaultSecrets.bicep` | Writes the four secrets the API reads at startup (below) |
| `staticWebApp.bicep` | Static Web App (Standard SKU) with a linked backend to the API |

No separate Azure SignalR Service resource: this deployment runs
ASP.NET Core's self-hosted SignalR directly on the single API App Service
instance, matching what `src/DevFlow.Api/DependencyInjection.cs` actually
implements (see its comment) rather than the originally-drafted Azure
SignalR Service ADR — a documented single-instance simplification, same
category as the in-process background worker described in
[Architecture §6](../docs/devflow/01-architecture.md#6-background-processing).

## Secrets handling

The API never receives raw secrets as committed configuration. At startup
(`src/DevFlow.Api/Program.cs`), if `KeyVault:Uri` is set (an App Service
app setting written by `appService.bicep`, not a secret itself — just an
endpoint URI), the app adds Key Vault as a configuration source using
`DefaultAzureCredential`, which resolves to the App Service's
system-assigned managed identity in Azure. No client secret exists to
leak, rotate, or accidentally commit.

Key Vault secret names use `--` where configuration uses `:` (`:` isn't a
legal Key Vault secret-name character; the Key Vault configuration
provider translates automatically):

| Key Vault secret | Configuration key | Consumed by |
|---|---|---|
| `Jwt--SigningKey` | `Jwt:SigningKey` | JWT issuing/validation |
| `ConnectionStrings--DevFlowDatabase` | `ConnectionStrings:DevFlowDatabase` | `AddInfrastructureServices` (Npgsql) |
| `Storage--Azure--ConnectionString` | `Storage:Azure:ConnectionString` | `AzureBlobStorageService` |
| `ApplicationInsights--ConnectionString` | `ApplicationInsights:ConnectionString` | `UseAzureMonitor` |

Locally, none of this exists — `dotnet user-secrets` fills the same
configuration keys directly (see the root README's Local Development
section), and `KeyVault:Uri` is simply never set, so the Key Vault
configuration source is never added at all.

## Migrations strategy

Migrations are **not** applied automatically on API startup. Auto-migrating
on every process start is a bad fit for an App Service plan that can spin
up more than one instance (concurrent `EnsureCreated`/`Migrate` calls
racing against the same database) and it couples a schema change to a
code deploy with no way to gate one on the other succeeding first.

Instead, the existing EF Core migration (`src/DevFlow.Infrastructure/Persistence/Migrations/`,
currently just `InitialCreate`) is applied as its own explicit step in the
`deploy` GitHub Actions workflow, **before** the API deployment step, using
a self-contained [EF Core migrations bundle](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying?tabs=dotnet-core-cli#bundles):

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations bundle \
  --project src/DevFlow.Infrastructure/DevFlow.Infrastructure.csproj \
  --startup-project src/DevFlow.Api/DevFlow.Api.csproj \
  --self-contained -r linux-x64 \
  --output efbundle

./efbundle --connection "$DATABASE_CONNECTION_STRING"
```

The bundle is a standalone executable (no `dotnet-ef` or .NET SDK needed
where it runs) that applies whatever migrations the target database is
missing — safe to re-run on every deploy, since an already-up-to-date
database is a no-op. Schema changes land before the new code that expects
them goes live, and a failed migration fails the workflow before any new
API code is deployed, rather than surfacing as a runtime error against a
half-migrated schema.

Verified in this environment (no Docker/PostgreSQL available here — see
the root README's Known Limitations) by building the bundle successfully
and confirming it attempts a real Postgres connection with the exact
connection-string shape used above, failing only on "connection refused"
against a nonexistent local server — i.e. everything short of an actual
target database to migrate.

Adding a future migration is unchanged from any other EF Core project:

```bash
dotnet ef migrations add <Name> \
  --project src/DevFlow.Infrastructure/DevFlow.Infrastructure.csproj \
  --startup-project src/DevFlow.Api/DevFlow.Api.csproj
```
