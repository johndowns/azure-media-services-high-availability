param(
    $ResourceGroupName = 'AmsHighAvailability',
    $ResourceGroupLocation = 'australiaeast'
)

$ErrorActionPreference = 'Stop'

$amsInstances = @(
    @{ instanceId = "aus"; location = "australiaeast" },
    @{ instanceId = "eus"; location = "eastus2" }
)
$jobTrackerCurrencyCheckInterval = '00:03:00'
$jobTrackerCurrencyThreshold = '00:05:00'
$jobTrackerTimeoutCheckInterval = '00:05:00'
$jobTrackerTimeoutThreshold = '01:00:00'
$amsInstanceRoutingMethod = 'Random'

Write-Host 'Creating resource group.'
New-AzResourceGroup -Name $ResourceGroupName -Location $ResourceGroupLocation -Force

# Deploy the Azure Media Services instances.
Write-Host 'Deploying Azure Media Services instances.'
$amsDeployment = New-AzResourceGroupDeployment -ResourceGroupName $ResourceGroupName -TemplateFile 'template-ams.json' `
    -instances $amsInstances
$createdAmsInstances = $amsDeployment.Outputs['createdInstances'].Value

# Deploy the function app resources.
Write-Host 'Deploying Azure Functions app resources.'
Write-Host 'Often this step fails the first time it executes because the managed identity does not provision successfully. If this happens, the script will retry the deployment.'
$hasDeployedFunctionApp = $false
while ($hasDeployedFunctionApp -ne $true)
{
    $functionAppDeployment = New-AzResourceGroupDeployment -ResourceGroupName $ResourceGroupName -TemplateFile 'template-functionapp.json'
    if ($null -ne $functionAppDeployment.Outputs)
    {
        break
    }
    Write-Host 'Retrying Azure Functions app resources deployment in 5 seconds.'
    Start-Sleep -Seconds 5
}

$functionAppName = $functionAppDeployment.Outputs['functionAppName'].Value
$functionAppIdentityPrincipalId = $functionAppDeployment.Outputs['functionAppIdentityPrincipalId'].Value
$functionAppApiKey = $functionAppDeployment.Outputs['functionAppApiKey'].Value

# Deploy the function app settings with references to the Azure Media Services resources.
Write-Host 'Constructing function app settings.'
$functionAppDefinition = Get-AzWebApp -Name $functionAppName -ResourceGroupName $ResourceGroupName
$functionAppSettings = $functionAppDefinition.SiteConfig.AppSettings
$functionAppSettingsHash = @{}
foreach ($kvp in $functionAppSettings)
{
    $functionAppSettingsHash[$kvp.Name] = $kvp.Value
}

$functionAppSettingsHash['Options:JobTrackerCurrencyCheckInterval'] = $jobTrackerCurrencyCheckInterval
$functionAppSettingsHash['Options:JobTrackerCurrencyThreshold'] = $jobTrackerCurrencyThreshold
$functionAppSettingsHash['Options:JobTrackerTimeoutCheckInterval'] = $jobTrackerTimeoutCheckInterval
$functionAppSettingsHash['Options:JobTrackerTimeoutThreshold'] = $jobTrackerTimeoutThreshold
$functionAppSettingsHash['Options:AmsInstanceRoutingMethod'] = $amsInstanceRoutingMethod

foreach ($createdAmsInstance in $createdAmsInstances)
{
    $createdAmsInstanceId = $createdAmsInstance["instanceId"].Value
    $createdAmsInstanceResourceName = $createdAmsInstance["amsInstanceResourceName"].Value
    $createdAmsInstanceSubscriptionId = $createdAmsInstance["azureSubscriptionId"].Value
    $createdAmsInstanceResourceGroupName = $createdAmsInstance["resourceGroupName"].Value

    if ($functionAppSettingsHash['Options:AllAmsInstanceIds'])
    {
        $functionAppSettingsHash['Options:AllAmsInstanceIds'] = $functionAppSettingsHash['Options:AllAmsInstanceIds'] + ';' + $createdAmsInstanceId
    }
    else
    {
        $functionAppSettingsHash['Options:AllAmsInstanceIds'] = $createdAmsInstanceId
    }

    $functionAppSettingsHash["Options:AmsInstances:$($createdAmsInstanceId):MediaServicesSubscriptionId"] = $createdAmsInstanceSubscriptionId
    $functionAppSettingsHash["Options:AmsInstances:$($createdAmsInstanceId):MediaServicesResourceGroupName"] = $createdAmsInstanceResourceGroupName
    $functionAppSettingsHash["Options:AmsInstances:$($createdAmsInstanceId):MediaServicesInstanceName"] = $createdAmsInstanceResourceName
}
Write-Host 'Updating Azure Functions app settings.'
Set-AzWebApp -Name $functionAppName -ResourceGroupName $ResourceGroupName -AppSettings $functionAppSettingsHash

# Compile the app and deploy the binaries to the function app.
Write-Host 'Building application.'
. dotnet publish ../src/AmsHighAvailability/AmsHighAvailability.sln
$functionZipFilePath = (New-TemporaryFile).FullName + '.zip'
Compress-Archive -Path ../src/AmsHighAvailability/bin/Debug/netcoreapp3.1/publish/* -DestinationPath $functionZipFilePath -Force

Write-Host 'Publishing application to Azure Functions app.'
Publish-AzWebApp -ResourceGroupName $ResourceGroupName -Name $functionAppName -ArchivePath $functionZipFilePath -Force

# Deploy the Azure IAM role assignments, to allow the function app's managed identity to access the Azure Media Services instances.
Write-Host 'Deploying Azure IAM role assignments.'
New-AzResourceGroupDeployment -ResourceGroupName $ResourceGroupName -TemplateFile 'template-iam.json' `
    -amsInstances $amsInstances -functionAppIdentityPrincipalId $functionAppIdentityPrincipalId

# Deploy the Event Grid subscriptions.
Write-Host 'Deploying Event Grid subscriptions.'
New-AzResourceGroupDeployment -ResourceGroupName $ResourceGroupName -TemplateFile 'template-eventgrid.json' `
    -amsInstances $amsInstances -functionAppName $functionAppName

# Output the URL to use to create a media encoding job.
Write-Host 'Deployment successful.'
Write-Host "To test this API, try submitting a POST request to https://$functionAppName.azurewebsites.net/api/jobCoordinators?code=$functionAppApiKey"
