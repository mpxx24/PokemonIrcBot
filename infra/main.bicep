// ============================================================
// PokemonIrcBot — Azure Infrastructure
// Resources: App Service Plan (B1), App Service, Storage,
//            Application Insights, Log Analytics, Key Vault
// Auth: System-assigned Managed Identity + RBAC (no secrets in config)
// ============================================================

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('App Service name — becomes <name>.azurewebsites.net')
param appName string

@description('Storage Account name for season stats (3-24 chars, lowercase alphanumeric, globally unique)')
param storageAccountName string

@description('Object ID of user running the deploy — gets Key Vault Secrets Officer role')
param deployerObjectId string = ''

@description('IRC channel the bot joins')
param ircChannel string

@description('Season identifier — used as the blob folder name')
param seasonId string = 'season-1'

@description('Season display name')
param seasonName string = 'Season 1'

var kvName            = 'kv${take(uniqueString(resourceGroup().id, appName), 21)}'
var appServicePlanName = 'plan-${appName}'

// ------------------------------------------------------------
// Log Analytics Workspace
// Free tier: 5 GB/month ingestion
// ------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'log-${appName}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ------------------------------------------------------------
// Application Insights (workspace-based)
// ------------------------------------------------------------
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${appName}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ------------------------------------------------------------
// App Service Plan — B1 Basic (Linux)
// ~€11/month — minimum tier that supports Always On for Worker Services
// ------------------------------------------------------------
resource appServicePlan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true // Linux
  }
}

// ------------------------------------------------------------
// App Service
// ------------------------------------------------------------
resource appService 'Microsoft.Web/sites@2022-09-01' = {
  name: appName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          // Blob endpoint — app authenticates via Managed Identity (DefaultAzureCredential)
          name: 'Storage__BlobEndpoint'
          value: storageAccount.properties.primaryEndpoints.blob
        }
        {
          name: 'Irc__Channel'
          value: ircChannel
        }
        {
          name: 'Season__Id'
          value: seasonId
        }
        {
          name: 'Season__Name'
          value: seasonName
        }
        {
          // Array config — ASP.NET Core binds Season__Generations__0 → Generations[0]
          name: 'Season__Generations__0'
          value: '1'
        }
        {
          // Key Vault URI — used by Program.cs to load secrets via Managed Identity
          name: 'KeyVaultUri'
          value: keyVault.properties.vaultUri
        }
      ]
    }
  }
}

// ------------------------------------------------------------
// Storage Account — Standard LRS, Managed Identity only
// allowSharedKeyAccess: false enforces RBAC over connection strings
// ------------------------------------------------------------
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

// Blob container for season stats
resource pokemonBotContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${storageAccountName}/default/pokemon-bot'
  dependsOn: [storageAccount]
  properties: {
    publicAccess: 'None'
  }
}

// Role: App Service MI → Storage Blob Data Contributor
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource storageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, appService.id, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: appService.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ------------------------------------------------------------
// Key Vault — reserved for future secrets
// ------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: kvName
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
    softDeleteRetentionInDays: 7
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    publicNetworkAccess: 'Enabled'
  }
}

var kvSecretsUserRoleId    = '4633458b-17de-408a-b874-0445c86b69e6'
var kvSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, appService.id, kvSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: appService.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource kvDeployerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerObjectId)) {
  name: guid(keyVault.id, deployerObjectId, kvSecretsOfficerRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsOfficerRoleId)
    principalId: deployerObjectId
    principalType: 'User'
  }
}

// ------------------------------------------------------------
// Diagnostic Settings — App Service → Log Analytics
// ------------------------------------------------------------
resource appServiceDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'diag-${appName}'
  scope: appService
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      { category: 'AppServiceConsoleLogs', enabled: true }
      { category: 'AppServiceAppLogs',     enabled: true }
    ]
  }
}

// ------------------------------------------------------------
// Outputs — consumed by the deploy script
// ------------------------------------------------------------
output appServiceUrl           string = 'https://${appService.properties.defaultHostName}'
output keyVaultName            string = keyVault.name
output appServiceName          string = appService.name
output appInsightsName         string = appInsights.name
output storageAccountName      string = storageAccount.name
