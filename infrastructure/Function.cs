using System;
using Pulumi;
using System.Collections.Generic;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;
using Pulumi.AzureNative.Authorization;
using ManagedServiceIdentityType = Pulumi.AzureNative.Web.ManagedServiceIdentityType;
using System.Linq;


namespace UspMeetingSummz {
    class Function
    {
        private string _hostingPlanName;
        private ResourceGroup _resourceGroup;
        private string _functionName;
        private readonly string _location;
        private readonly string _env;

        public Function(string name, string location, string env, ResourceGroup resourceGroup, string planName = "meet_summz", List<NameValuePairArgs> appEnvironmentVariables = null)
        {
            this._resourceGroup = resourceGroup;
            this._hostingPlanName = $"asp-{planName}-{location}-{env}";
            this._functionName = $"fn-{name}-{location}-{env}";
            this._location = location;
            this._env = env;

            appEnvironmentVariables = appEnvironmentVariables ?? new List<NameValuePairArgs>(); 
            CreateServerlessFunction(this._functionName, appEnvironmentVariables);
        }

        private void OauthAccessToStorage(WebApp function, Output<string> storageAccountId)
        {
            var builtinRolesIds = new List<string>
            {
                "b7e6dc6d-f1e8-4753-8033-0f276bb0955b",
                "974c5e8b-45b9-4653-ba55-5f855dd0fb88",
                "0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3"

            };

            foreach (var roleId in builtinRolesIds) 
            {
                var roleAssignment = new RoleAssignment($"{_functionName}-{roleId}", new RoleAssignmentArgs
                {
                    PrincipalId = function.Identity.Apply(identity => identity.PrincipalId),
                    RoleDefinitionId = $"/providers/Microsoft.Authorization/roleDefinitions/{roleId}",
                    Scope = storageAccountId,
                    PrincipalType = "ServicePrincipal"
                });
            }

        }

        public void CreateServerlessFunction(string functionName, List<NameValuePairArgs> appEnvironmentVariables)
        {
            var hostingPlan = new AppServicePlan(_hostingPlanName, new AppServicePlanArgs
            {
                Name = _hostingPlanName,
                ResourceGroupName = _resourceGroup.Name,
                Sku = new SkuDescriptionArgs
                {
                    Tier = "Dynamic",
                    Name = "Y1"
                },
                Kind = "Windows",
                Reserved = false
            });

            var storage = new Storage(functionName.Split("-")[1], _location, _env, _resourceGroup);
            
            // var containerAssetUrl = storage.CreateBlobContainer($"{functionName.Split("-")[1]}-content");

            var function = new WebApp(functionName, new WebAppArgs
            {
                Name = functionName,
                ResourceGroupName = _resourceGroup.Name,
                Reserved = false,
                Kind = "FunctionApp",
                Identity = new ManagedServiceIdentityArgs
                {
                    Type = ManagedServiceIdentityType.SystemAssigned
                },
                HttpsOnly = true,
                ServerFarmId = hostingPlan.Id,
                SiteConfig = new SiteConfigArgs
                {
                    Cors = new CorsSettingsArgs
                    {
                        AllowedOrigins = new[]
                        {
                            "https://portal.azure.com",
                        },
                        SupportCredentials = false,
                    },
                    AppSettings = appEnvironmentVariables.Concat(
                    [
                       new NameValuePairArgs
                        {
                            Name = "AzureWebJobsStorage__accountName",
                            Value = storage.GetAccountName()
                        },
                        new NameValuePairArgs
                        {
                            Name = "AzureWebJobsStorage__blobServiceUri",
                            Value = storage.GetBlobEndpoint() 
                        },
                        new NameValuePairArgs
                        {
                            Name = "AzureWebJobsStorage__queueServiceUri",
                            Value = storage.GetQueueEndpoint() 
                        },
                        new NameValuePairArgs
                        {
                            Name = "AzureWebJobsStorage__tableServiceUri",
                            Value = storage.GetTableEndpoint() 
                        },

                        new NameValuePairArgs
                        {
                            Name = "FUNCTIONS_EXTENSION_VERSION",
                            Value = "~4"
                        },
                        new NameValuePairArgs
                        {
                            Name = "WEBSITE_RUN_FROM_PACKAGE",
                            Value = "1"
                        },
                        new NameValuePairArgs
                        {
                            Name = "FUNCTIONS_WORKER_RUNTIME",
                            Value = "dotnet"
                        }
                    ]).ToList()
                }
            });

            OauthAccessToStorage(function, storage.GetStorageAccountId());
        }
    }
}