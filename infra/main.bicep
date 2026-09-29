// Multi-tenant SaaS API on Azure: App Service + Cosmos DB, secured with managed identity.
// Deploy:  az deployment group create -g <resource-group> -f infra/main.bicep

@description('Short prefix for resource names (3-11 lowercase letters or numbers).')
@minLength(3)
@maxLength(11)
param prefix string = 'saas'

param location string = resourceGroup().location

@description('Tenants on the Dedicated isolation model. Each gets its own container and throughput.')
param dedicatedTenantIds array = [
  'initech'
]

@description('Autoscale ceiling (RU/s) for the shared, pooled container.')
param pooledMaxThroughput int = 4000

@description('Autoscale ceiling (RU/s) for each dedicated tenant container.')
param dedicatedMaxThroughput int = 1000

var suffix = uniqueString(resourceGroup().id)

// Built-in "Cosmos DB Built-in Data Contributor" role: read and write items, nothing else.
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: '${prefix}-cosmos-${suffix}'
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    disableLocalAuth: true // keys are disabled: only Microsoft Entra ID (managed identity) can access data
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmos
  name: 'saas'
  properties: {
    resource: {
      id: 'saas'
    }
  }
}

var tenantPartitionKey = {
  paths: [
    '/tenantId'
  ]
  kind: 'Hash'
}

resource pooledContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'projects'
  properties: {
    resource: {
      id: 'projects'
      partitionKey: tenantPartitionKey
    }
    options: {
      autoscaleSettings: {
        maxThroughput: pooledMaxThroughput
      }
    }
  }
}

resource dedicatedContainers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = [for tenantId in dedicatedTenantIds: {
  parent: database
  name: 'projects-${tenantId}'
  properties: {
    resource: {
      id: 'projects-${tenantId}'
      partitionKey: tenantPartitionKey
    }
    options: {
      autoscaleSettings: {
        maxThroughput: dedicatedMaxThroughput
      }
    }
  }
}]

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${prefix}-plan-${suffix}'
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
  }
  properties: {
    reserved: true
  }
}

resource api 'Microsoft.Web/sites@2023-12-01' = {
  name: '${prefix}-api-${suffix}'
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      appSettings: [
        { name: 'Cosmos__AccountEndpoint', value: cosmos.properties.documentEndpoint }
        { name: 'Cosmos__Database', value: database.name }
      ]
    }
  }
}

// Give the API's managed identity data access to this Cosmos account only.
resource apiDataAccess 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmos
  name: guid(cosmos.id, api.id, cosmosDataContributorRoleId)
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: api.identity.principalId
    scope: cosmos.id
  }
}

output apiUrl string = 'https://${api.properties.defaultHostName}'
output cosmosEndpoint string = cosmos.properties.documentEndpoint
