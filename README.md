# azure-container-landscape
From container instances to Kubernetes

## Prerequisites

1. Visual Studio or Visual Studio Code with .NET Framework 6.
2. Docker Desktop to run the containerized application locally.
https://www.docker.com/products/docker-desktop
3. DAPR CLI installed on a local machine.
https://docs.dapr.io/getting-started/install-dapr-cli/
4. AZ CLI tools installation(for cloud deployment)
https://aka.ms/installazurecliwindows
5. Azure subscription, if you want to deploy applications to Kubernetes(AKS).
https://azure.microsoft.com/en-us/free/
6. Kubectl installation https://kubernetes.io/docs/tasks/tools/install-kubectl-windows/#install-kubectl-binary-with-curl-on-windows
7. Good mood :)

## Step 1. Azure infrastructure
Script below should be run via Azure Portal bash console. 
You will receive database connection strings with setx command as output of this script. Along with Application Insights key
Please add a correct name of your subscription to the first row of the script. 

As result of this deployemt you should open the local command line as admin and execute output strings from the script execution to set environment variables.
It is also good to store them in the text file for the future usage

You might need to reboot your PC so secrets will be available from the OS.

For the start the preferrable way is to use Azue CLI bash console via Azure portal.

```bash
subscriptionID=$(az account list --query "[?contains(name,'Microsoft')].[id]" -o tsv)
echo "Test subscription ID is = " $subscriptionID
az account set --subscription $subscriptionID
az account show

location=northeurope
postfix=$RANDOM

#----------------------------------------------------------------------------------
# Database infrastructure
#----------------------------------------------------------------------------------

export dbResourceGroup=cont-land-data$postfix
export dbServername=cont-land-sql$postfix
export dbPoolname=dbpool
export dbAdminlogin=FancyUser3
export dbAdminpassword=Sup3rStr0ng52$postfix
export dbPaperName=paperorders
export dbDeliveryName=deliveries

az group create --name $dbResourceGroup --location $location

az sql server create --resource-group $dbResourceGroup --name $dbServername --location $location \
--admin-user $dbAdminlogin --admin-password $dbAdminpassword
	
az sql elastic-pool create --resource-group $dbResourceGroup --server $dbServername --name $dbPoolname \
--edition Standard --dtu 50 --zone-redundant false --db-dtu-max 50

az sql db create --resource-group $dbResourceGroup --server $dbServername --elastic-pool $dbPoolname \
--name $dbPaperName --catalog-collation SQL_Latin1_General_CP1_CI_AS
	
az sql db create --resource-group $dbResourceGroup --server $dbServername --elastic-pool $dbPoolname \
--name $dbDeliveryName --catalog-collation SQL_Latin1_General_CP1_CI_AS	

sqlClientType=ado.net

SqlPaperString=$(az sql db show-connection-string --name $dbPaperName --server $dbServername --client $sqlClientType --output tsv)
SqlPaperString=${SqlPaperString/Password=<password>;}
SqlPaperString=${SqlPaperString/<username>/$dbAdminlogin}

SqlDeliveryString=$(az sql db show-connection-string --name $dbDeliveryName --server $dbServername --client $sqlClientType --output tsv)
SqlDeliveryString=${SqlDeliveryString/Password=<password>;}
SqlDeliveryString=${SqlDeliveryString/<username>/$dbAdminlogin}

SqlPaperPassword=$dbAdminpassword

#----------------------------------------------------------------------------------
# AKS infrastructure
#----------------------------------------------------------------------------------

location=northeurope
groupName=cont-land-cluster$postfix
clusterName=cont-land-cluster$postfix
registryName=contlandregistry$postfix


az group create --name $groupName --location $location

az acr create --resource-group $groupName --name $registryName --sku Standard
az acr identity assign --identities [system] --name $registryName

az aks create --resource-group $groupName --name $clusterName --node-count 3 --generate-ssh-keys --network-plugin azure
az aks update --resource-group $groupName --name $clusterName --attach-acr $registryName
az aks enable-addons --addon monitoring --name $clusterName --resource-group $groupName

#----------------------------------------------------------------------------------
# Service bus queue
#----------------------------------------------------------------------------------

groupName=cont-land-extras$postfix
location=northeurope
az group create --name $groupName --location $location
namespaceName=contLand$postfix
queueName=createdelivery

az servicebus namespace create --resource-group $groupName --name $namespaceName --location $location
az servicebus queue create --resource-group $groupName --name $queueName --namespace-name $namespaceName

serviceBusString=$(az servicebus namespace authorization-rule keys list --resource-group $groupName --namespace-name $namespaceName --name RootManageSharedAccessKey --query primaryConnectionString --output tsv)

#----------------------------------------------------------------------------------
# Application insights
#----------------------------------------------------------------------------------

insightsName=contLandlogs$postfix
az monitor app-insights component create --resource-group $groupName --app $insightsName --location $location --kind web --application-type web --retention-time 120

instrumentationKey=$(az monitor app-insights component show --resource-group $groupName --app $insightsName --query  "instrumentationKey" --output tsv)

#----------------------------------------------------------------------------------
# Azure Container Apps
#----------------------------------------------------------------------------------

instancesGroupName=cont-land-instances$postfix
location=northeurope
az group create --name $instancesGroupName --location $location

#----------------------------------------------------------------------------------
# Azure Container Apps
#----------------------------------------------------------------------------------

az extension add --name containerapp --upgrade

az provider register --namespace Microsoft.App

az provider register --namespace Microsoft.OperationalInsights

acaGroupName=cont-land-containerapp$postfix
location=northeurope
logAnalyticsWorkspace=cont-land-logs$postfix
containerAppsEnv=contl-environment$postfix

az group create --name $acaGroupName --location $location

az monitor log-analytics workspace create \
--resource-group $acaGroupName --workspace-name $logAnalyticsWorkspace

logAnalyticsWorkspaceClientId=`az monitor log-analytics workspace show --query customerId -g $acaGroupName -n $logAnalyticsWorkspace -o tsv | tr -d '[:space:]'`

logAnalyticsWorkspaceClientSecret=`az monitor log-analytics workspace get-shared-keys --query primarySharedKey -g $acaGroupName -n $logAnalyticsWorkspace -o tsv | tr -d '[:space:]'`

az containerapp env create \
--name $containerAppsEnv \
--resource-group $acaGroupName \
--logs-workspace-id $logAnalyticsWorkspaceClientId \
--logs-workspace-key $logAnalyticsWorkspaceClientSecret \
--dapr-instrumentation-key $instrumentationKey \
--logs-destination log-analytics \
--location $location

az containerapp env show --resource-group $acaGroupName --name $containerAppsEnv

# we don't need a section below for this workshop, but you can use it later
# use command below to fill credentials values if you want to use section below 
#az acr credential show --name $registryName 

#imageName=<CONTAINER_IMAGE_NAME>
#acrServer=<REGISTRY_SERVER>
#acrUser=<REGISTRY_USERNAME>
#acrPassword=<REGISTRY_PASSWORD>

#az containerapp create \
#  --name my-container-app \
#  --resource-group $acaGroupName \
#  --image $imageName \
#  --environment $containerAppsEnv \
#  --registry-server $acrServer \
#  --registry-username $acrUser \
#  --registry-password $acrPassword

#----------------------------------------------------------------------------------
# Azure Key Vault with secrets assignment and access setup
#----------------------------------------------------------------------------------

keyvaultName=cont-land$postfix
principalName=vaultadmin
principalCertName=vaultadmincert

az keyvault create --resource-group $groupName --name $keyvaultName --location $location
az keyvault secret set --name SqlPaperPassword --vault-name $keyvaultName --value $SqlPaperPassword

az ad sp create-for-rbac --name $principalName --create-cert --cert $principalCertName --keyvault $keyvaultName --skip-assignment --years 3

# get appId from output of step above and add it after --id in command below.

# az ad sp show --id 474f817c-7eba-4656-ae09-979a4bc8d844
# get object Id (located before info object) from command output above and set it to command below 

# az keyvault set-policy --name $keyvaultName --object-id f1d1a707-1356-4fb8-841b-98e1d9557b05 --secret-permissions get
#----------------------------------------------------------------------------------
# SQL connection strings
#----------------------------------------------------------------------------------

printf "\n\nRun string below in local cmd prompt to assign secret to environment variable SqlPaperString:\nsetx SqlPaperString \"$SqlPaperString\"\n\n"
printf "\n\nRun string below in local cmd prompt to assign secret to environment variable SqlDeliveryString:\nsetx SqlDeliveryString \"$SqlDeliveryString\"\n\n"
printf "\n\nRun string below in local cmd prompt to assign secret to environment variable SqlPaperPassword:\nsetx SqlPaperPassword \"$SqlPaperPassword\"\n\n"
printf "\n\nRun string below in local cmd prompt to assign secret to environment variable SqlDeliveryPassword:\nsetx SqlDeliveryPassword \"$SqlPaperPassword\"\n\n"
printf "\n\nRun string below in local cmd prompt to assign secret to environment variable ServiceBusString:\nsetx ServiceBusString \"$serviceBusString\"\n\n"

echo "Update open-telemetry-collector-appinsights.yaml in Step 5 End => <INSTRUMENTATION-KEY> value with:  " $instrumentationKey
```

## Step 1. Local containerisation

First we adding docker containerization via context menu of each project.
<img width="489" alt="image" src="https://user-images.githubusercontent.com/36765741/204159578-5e72e255-928d-4b75-bd67-3b9f8a23e48f.png">

Then we adding orchestration support via docker compose again to the each project


If you decide to add storage at this step, then you should add the environment variable file to the root folder, so secrets will be shared between service for simplicity
<img width="183" alt="image" src="https://user-images.githubusercontent.com/36765741/204159631-754bfbfe-7052-4e8d-a286-c71347266586.png">

Then we changing order controller url for communication inside docker
```
            string url =
                $"http://tpaperorders:80/api/delivery/create/{savedOrder.ClientId}/{savedOrder.Id}/{savedOrder.ProductCode}/{savedOrder.Quantity}";
```
And from this point you should run solution in debug with docker compose option
![image](https://user-images.githubusercontent.com/36765741/204160258-35c356ff-931b-424c-9bac-6d261f432351.png)

!! Be aware, if you have docker build exceptions in Visual studio with errors related to the File system, there is a need to configure docker desktop. 
Open Docker desktop => configuration => Resources => File sharing => Add your project folder or entire drive, C:\ for example. Dont forget to remove drive setting later on.

!! When you try to start the same solution from the new folder, you might need to stop and delete containers via docker compose.

## Step 3. Azure Container instances deploy.

The first thing is we need to login locally to Azure and authenticate to the newly created Azure Container Registry, build, tag and push container there.

Then we will create identity for container registry.

And finally create a new container instance from our container in Azure Container Registry


Let's begin with local CMD promt and pushing of the container to Azure
!!!Use additional command az account set --subscription 95cd9078f8c to deploy resources into the correct subscription
```cmd
az login

az account show
az acr login --name contlandregistry
```

Open Visual studio and re-build your project in the Release mode, check with command line that the new container with the latest tag is created

Set a next version in manifest and command below before execution, check docker images command

```
docker tag tpaperorders:latest contlandregistry.azurecr.io/tpaperorders:v1
docker images
```

then push container to the container registry with
```
docker push contlandregistry.azurecr.io/tpaperorders:v1
```

Check if image is in the container registry

```
az acr repository list --name contlandregistry --output table
```

Then we are moving to the creation of service principal for Container registry via Azure Portal Bash console

```
registry=contlandregistry
principalName=registryPrincipal

registryId=$(az acr show --name $registry --query "id" --output tsv)

regPassword=$(az ad sp create-for-rbac --name $principalName --scopes $registryId --role acrpull --query "password" --output tsv)
regUser=$(az ad sp list --display-name $principalName --query "[].appId" --output tsv)

echo "Service principal ID: $regUser"
echo "Service principal password: $regPassword"
```

The output of the following script should containe login and password

```
Service principal ID: 277a0a62-9fb0
Service principal password: iUe44444444444444444444444a2r
```

Then we can continue from a local command line or azure portal.

Getting the login server

```
az acr show --name contlandregistry --query loginServer
```
 output will be contlandregistry.azurecr.io

So we adding the correct values to our application string
The resource group cont-land-instances with postfix created earlier.
--dns-name-label is your unique public name, so you can create it with your container registry name and postfix.

```
az container create --resource-group cont-land-instances --name cont-land-aci --image contlandregistry.azurecr.io/tpaperorders:v1 --cpu 1 --memory 1 --registry-login-server contlandregistry.azurecr.io --registry-username 277a0a62-9fb0 --registry-password iUe44444444444444444444444a2r --ip-address Public --dns-name-label contlandregistry --ports 80
```

As results we have our new container app deployed

<img width="638" alt="image" src="https://user-images.githubusercontent.com/36765741/208309681-04de647c-118e-4c94-a9a2-134b667c0778.png">

You can monitor deployment of container instance with
```
az container show --resource-group cont-land-instances --name cont-land-aci --query instanceView.state
```

Our application is not using Application insights, so we can check logs quickly via additional command

```
az container logs --resource-group cont-land-instances --name cont-land-aci
```

This way you will see that we have an error with the url referencing the delivery controller, so we can fix it with Container App fqdn

```
            string url =
                $"http://contlandregistry.northeurope.azurecontainer.io:80/api/delivery/create/{savedOrder.ClientId}/{savedOrder.Id}/{savedOrder.ProductCode}/{savedOrder.Quantity}";
```

Rebuild container, tag it with version 2 and deploy it again to the container apps

## Step 4. Azure Container instances multi container group.

This is a quite exotic case, because usage of a container with a sidecar or several services inside one container group without scale possibility is almost pointless.

But you should be aware about this possibilty, so you can leverage simple two service scenario as fast and easy as possible.

let's login to our container registry from a step 4 folder
```cmd
az login

az account show
az acr login --name contlandregistry
```

Not it is time to authenticat docker to your azure subscription and create context for resource group from a command line inside step 4 solution folder

```
docker login azure
docker context create aci instancescontext
```
![image](https://user-images.githubusercontent.com/36765741/208311730-3623ac6c-0265-4da0-822f-4b725a364f05.png)

