targetScope = 'resourceGroup'

@description('Location for App Service resources.')
param location string = resourceGroup().location

@description('Name of the App Service plan.')
param planName string

@description('Name of the App Service.')
param appName string

@description('SKU name for the App Service plan.')
param planSkuName string = 'P1v3'

@description('Instance count for the App Service plan.')
@minValue(1)
param planSkuCapacity int = 1

@description('App Service runtime stack, e.g. DOTNETCORE|8.0.')
param linuxFxVersion string = 'DOTNETCORE|8.0'

@description('Create a staging slot.')
param createSlot bool = true

@description('Slot name for staging slot.')
param slotName string = 'staging'

@description('Use the staging slot as the Front Door origin.')
param frontDoorUseStagingSlot bool = true

@description('Deploy Azure Front Door resources.')
param deployFrontDoor bool = false

@description('ASP.NET Core environment.')
param environmentName string = 'Production'

@description('JWT signing key (32+ bytes).')
@secure()
param jwtKey string

@description('Database connection string.')
@secure()
param dbConnectionString string

@description('Allowed CORS origins.')
@minLength(1)
param corsAllowedOrigins array

@description('Extra IP CIDR blocks allowed to reach the app (optional).')
param allowedIpCidrs array = []

@description('Forwarded headers forward limit.')
@minValue(1)
param forwardedHeadersForwardLimit int = 2

@description('Trust all forwarded headers (only when ingress is locked down).')
param forwardedHeadersTrustAll bool = true

@description('Set true only when App Service ingress is locked down to Front Door.')
param forwardedHeadersIngressLockedDown bool = true

@description('Require known proxies/networks for forwarded headers.')
param forwardedHeadersRequireKnownProxies bool = false

@description('Azure Front Door profile name.')
param frontDoorProfileName string

@description('Azure Front Door endpoint name.')
param frontDoorEndpointName string

@description('Azure Front Door origin group name.')
param frontDoorOriginGroupName string

@description('Azure Front Door origin name.')
param frontDoorOriginName string

@description('Azure Front Door route name.')
param frontDoorRouteName string

@description('Azure Front Door SKU name.')
@allowed([
  'Standard_AzureFrontDoor'
  'Premium_AzureFrontDoor'
])
param frontDoorSkuName string = 'Standard_AzureFrontDoor'

var appHostName = '${appName}.azurewebsites.net'
var slotHostName = '${appName}-${slotName}.azurewebsites.net'
var useStagingSlot = createSlot && frontDoorUseStagingSlot
var originHostName = useStagingSlot ? slotHostName : appHostName

var effectiveForwardedHeadersTrustAll = deployFrontDoor ? forwardedHeadersTrustAll : false
var effectiveForwardedHeadersIngressLockedDown = deployFrontDoor ? forwardedHeadersIngressLockedDown : false
var effectiveForwardedHeadersRequireKnownProxies = deployFrontDoor ? forwardedHeadersRequireKnownProxies : false

var baseAppSettings = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: environmentName
  }
  {
    name: 'WEBSITE_HEALTHCHECK_PATH'
    value: '/health/live'
  }
  {
    name: 'Jwt__Key'
    value: jwtKey
  }
  {
    name: 'ConnectionStrings__DefaultConnection'
    value: dbConnectionString
  }
  {
    name: 'Seed__Enabled'
    value: 'false'
  }
  {
    name: 'Seed__Admin__Enabled'
    value: 'false'
  }
  {
    name: 'Startup__AutoMigrate'
    value: 'false'
  }
  {
    name: 'ForwardedHeaders__TrustAll'
    value: effectiveForwardedHeadersTrustAll ? 'true' : 'false'
  }
  {
    name: 'ForwardedHeaders__IngressLockedDown'
    value: effectiveForwardedHeadersIngressLockedDown ? 'true' : 'false'
  }
  {
    name: 'ForwardedHeaders__RequireKnownProxies'
    value: effectiveForwardedHeadersRequireKnownProxies ? 'true' : 'false'
  }
  {
    name: 'ForwardedHeaders__ForwardLimit'
    value: string(forwardedHeadersForwardLimit)
  }
]

var corsAppSettings = [
  for (origin, i) in corsAllowedOrigins: {
    name: 'Cors__AllowedOrigins__${i}'
    value: origin
  }
]

var appSettings = concat(baseAppSettings, corsAppSettings)

var baseIpRestrictions = deployFrontDoor ? [
  {
    name: 'AllowAzureFrontDoor'
    description: 'Allow Azure Front Door backends.'
    action: 'Allow'
    priority: 100
    ipAddress: 'AzureFrontDoor.Backend'
    tag: 'ServiceTag'
  }
] : []

var extraIpRestrictions = [
  for (cidr, i) in allowedIpCidrs: {
    name: 'AllowExtra${i}'
    description: 'Allow additional ingress.'
    action: 'Allow'
    priority: 200 + i
    ipAddress: cidr
  }
]

var useIpRestrictions = deployFrontDoor || length(allowedIpCidrs) > 0
var ipSecurityRestrictions = useIpRestrictions ? concat(baseIpRestrictions, extraIpRestrictions) : []
var ipSecurityRestrictionsDefaultAction = useIpRestrictions ? 'Deny' : 'Allow'

var siteConfig = {
  linuxFxVersion: linuxFxVersion
  alwaysOn: true
  ftpsState: 'Disabled'
  minTlsVersion: '1.2'
  healthCheckPath: '/health/live'
  appSettings: appSettings
  ipSecurityRestrictions: ipSecurityRestrictions
  ipSecurityRestrictionsDefaultAction: ipSecurityRestrictionsDefaultAction
  scmIpSecurityRestrictionsUseMain: true
  scmIpSecurityRestrictionsDefaultAction: ipSecurityRestrictionsDefaultAction
}

resource appPlan 'Microsoft.Web/serverfarms@2022-03-01' = {
  name: planName
  location: location
  sku: {
    name: planSkuName
    capacity: planSkuCapacity
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource app 'Microsoft.Web/sites@2022-03-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: appPlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: siteConfig
  }
}

resource appSlot 'Microsoft.Web/sites/slots@2022-03-01' = if (createSlot) {
  parent: app
  name: slotName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: appPlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: siteConfig
  }
}

resource frontDoorProfile 'Microsoft.Cdn/profiles@2024-02-01' = if (deployFrontDoor) {
  name: frontDoorProfileName
  location: 'global'
  sku: {
    name: frontDoorSkuName
  }
}

resource frontDoorEndpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = if (deployFrontDoor) {
  parent: frontDoorProfile
  name: frontDoorEndpointName
  location: 'global'
  properties: {
    enabledState: 'Enabled'
  }
}

resource frontDoorOriginGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = if (deployFrontDoor) {
  parent: frontDoorProfile
  name: frontDoorOriginGroupName
  properties: {
    healthProbeSettings: {
      probePath: '/health/live'
      probeProtocol: 'Https'
      probeRequestType: 'GET'
      probeIntervalInSeconds: 30
    }
    loadBalancingSettings: {
      additionalLatencyInMilliseconds: 0
      sampleSize: 4
      successfulSamplesRequired: 3
    }
  }
}

resource frontDoorOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = if (deployFrontDoor) {
  parent: frontDoorOriginGroup
  name: frontDoorOriginName
  properties: {
    hostName: originHostName
    originHostHeader: originHostName
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
    httpPort: 80
    httpsPort: 443
  }
}

resource frontDoorRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = if (deployFrontDoor) {
  parent: frontDoorEndpoint
  name: frontDoorRouteName
  properties: {
    originGroup: {
      id: frontDoorOriginGroup.id
    }
    supportedProtocols: [
      'Https'
    ]
    patternsToMatch: [
      '/*'
    ]
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    linkToDefaultDomain: 'Enabled'
    enabledState: 'Enabled'
  }
  dependsOn: [
    frontDoorOrigin
  ]
}

output appServiceHostName string = appHostName
output frontDoorHostName string = deployFrontDoor ? frontDoorEndpoint.properties.hostName : ''
