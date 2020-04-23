$resourceGroupName = 'YetAnotherTempAms'
$resourceGroupLocation = 'australiaeast'
$amsInstances = @(
    @{ name = "aus"; location = "australiaeast"},
    @{ name = "eus"; location = "eastus2"}
)
$jobTrackerStatusTimeoutCheckInterval = '00:05:00'
$jobTrackerTimeoutThreshold = '01:00:00'
$amsInstanceRoutingMethod = 'RoundRobin'

Write-Host 'Creating resource group.'
New-AzResourceGroup -Name $resourceGroupName -Location $resourceGroupLocation -Force

# Deploy the AMS instances
Write-Host 'Deploying Azure Media Services instances.'
$amsDeployment = New-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -TemplateFile 'template-ams.json' -Verbose `
    -instances $amsInstances
$createdAmsInstances = $amsDeployment.Outputs['createdInstances'].Value

# Deploy the function app resource
Write-Host 'Deploying Azure Functions app resources.'
$functionAppDeployment = New-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -TemplateFile 'template-functionapp.json' -Verbose
$functionAppName = $functionAppDeployment.Outputs['functionAppName'].Value
$functionAppIdentityPrincipalId = $functionAppDeployment.Outputs['functionAppIdentityPrincipalId'].Value
# TODO put extra resiliency in here, since the managed identity can sometimes take time to provision

# Deploy the function app settings
Write-Host 'Constructing function app settings.'
$functionAppDefinition = Get-AzWebApp -Name $functionAppName -ResourceGroupName $resourceGroupName
$functionAppSettings = $functionAppDefinition.SiteConfig.AppSettings
$functionAppSettingsHash = @{}
foreach ($kvp in $functionAppSettings)
{
    $functionAppSettingsHash[$kvp.Name] = $kvp.Value
}

$functionAppSettingsHash['Options:JobTrackerStatusTimeoutCheckInterval'] = $jobTrackerStatusTimeoutCheckInterval
$functionAppSettingsHash['Options:JobTrackerTimeoutThreshold'] = $jobTrackerTimeoutThreshold
$functionAppSettingsHash['Options:AmsInstanceRoutingMethod'] = $amsInstanceRoutingMethod

foreach ($createdAmsInstance in $createdAmsInstances)
{
    $createdAmsInstanceName = $createdAmsInstance["name"].Value
    $createdAmsInstanceResourceName = $createdAmsInstance["instanceName"].Value
    $createdAmsInstanceSubscriptionId = $createdAmsInstance["azureSubscriptionId"].Value
    $createdAmsInstanceResourceGroupName = $createdAmsInstance["resourceGroupName"].Value

    if ($allAmsInstanceIds)
    {
        $allAmsInstanceIds = $allAmsInstanceIds + ';' + $createdAmsInstanceName
    }
    else
    {
        $allAmsInstanceIds = $createdAmsInstanceName
    }

    $functionAppSettingsHash["Options:AmsInstances:$($createdAmsInstanceName):MediaServicesSubscriptionId"] = $createdAmsInstanceSubscriptionId
    $functionAppSettingsHash["Options:AmsInstances:$($createdAmsInstanceName):MediaServicesResourceGroupName"] = $createdAmsInstanceResourceGroupName
    $functionAppSettingsHash["Options:AmsInstances:$($createdAmsInstanceName):MediaServicesInstanceName"] = $createdAmsInstanceResourceName
}
$functionAppSettingsHash['Options:AllAmsInstanceIds'] = $allAmsInstanceIds
Write-Host 'Updating Azure Functions app settings.'
Set-AzWebApp -Name $functionAppName -ResourceGroupName $resourceGroupName -AppSettings $functionAppSettingsHash

# Compile the app and deploy the binaries to the function app
Write-Host 'Building application.'
. dotnet publish ../src/AmsHighAvailability/AmsHighAvailability.sln
$functionZipFilePath = (New-TemporaryFile).FullName + '.zip'
Compress-Archive -Path ../src/AmsHighAvailability/bin/Debug/netcoreapp3.1/publish/* -DestinationPath $functionZipFilePath -Force

Write-Host 'Publishing application to Azure Functions app.'
Publish-AzWebApp -ResourceGroupName $resourceGroupName -Name $functionAppName -ArchivePath $functionZipFilePath -Force

# Deploy the Azure IAM role assignments
Write-Host 'Deploying Azure IAM role assignments.'
New-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -TemplateFile 'template-iam.json' -Verbose `
    -amsInstances $amsInstances -functionAppIdentityPrincipalId $functionAppIdentityPrincipalId

# Deploy the Event Grid subscriptions
Write-Host 'Deploying Event Grid subscriptions.'
New-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -TemplateFile 'template-eventgrid.json' -Verbose `
    -amsInstances $amsInstances -functionAppName $functionAppName
