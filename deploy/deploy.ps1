$resourceGroupName = 'MyHASample'
$resourceGroupLocation = 'australiaeast'    
$parametersFileName = 'parameters.json'

az group create -n $resourceGroupName -l $resourceGroupLocation

# Deploy the majority of the resources for the stamps.
az group deployment create -g $resourceGroupName --template-file 'template-stamps-allregions.json' --parameters $parametersFileName --handle-extended-json-format       

# TODO deploy function app

# Deploy the Event Grid subscriptions. This has to be done after the function apps are deployed.
az group deployment create -g $resourceGroupName --template-file 'template-eventgrid-allregions.json' --parameters $parametersFileName --handle-extended-json-format
