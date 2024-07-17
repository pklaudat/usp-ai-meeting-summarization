using System;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;
using Pulumi.AzureNative.Authorization;
using ManagedServiceIdentityType = Pulumi.AzureNative.Web.ManagedServiceIdentityType;


namespace UspMeetingSummz {
    class Function
    {
        private string _hostingPlanName;
        private ResourceGroup _resourceGroup;
        private string _functionName;
        private readonly string _location;
        private readonly string _env;

        public Function(string name, string location, string env, ResourceGroup resourceGroup, string planName = "meet_summz")
        {
            this._resourceGroup = resourceGroup;
            this._hostingPlanName = $"asp-{planName}-{location}-{env}";
            this._functionName = $"fn-{name}-{location}-{env}";
            this._location = location;
            this._env = env;


            CreateServerlessFunction(this._functionName);
        }

        private void OauthAccessToStorage(WebApp function, Output<string> storageAccountId)
        {
            var roleAssignment = new RoleAssignment($"role-assignment-{Guid.NewGuid()}", new RoleAssignmentArgs
            {
                PrincipalId = function.Identity.Apply(identity => identity.PrincipalId),
                RoleDefinitionId = $"/providers/Microsoft.Authorization/roleDefinitions/ba92f5b4-2d11-453d-a403-e96b0029c9fe",
                Scope = storageAccountId,
                PrincipalType = "ServicePrincipal"
            });
        }

        public void CreateServerlessFunction(string functionName)
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
                Kind = "Linux",
                Reserved = true
            });

            var storage = new Storage(functionName.Split("-")[1], _location, _env, _resourceGroup);
            
            // storage.CreateBlobContainer(functionName);

            var function = new WebApp(functionName, new WebAppArgs
            {
                Name = functionName,
                ResourceGroupName = _resourceGroup.Name,
                Reserved = true,
                Kind = "FunctionApp",
                Identity = new ManagedServiceIdentityArgs
                {
                    Type = ManagedServiceIdentityType.SystemAssigned
                },
                HttpsOnly = true,
                ServerFarmId = hostingPlan.Id,
                SiteConfig = new SiteConfigArgs
                {
                    AppSettings = new[]
                    {
                        new NameValuePairArgs
                        {
                            Name = "AzureWebJobsStorage",
                            Value = Output.Tuple(_resourceGroup.Name, storage.GetAccountName()).Apply(names =>
                                $"DefaultEndpointsProtocol=https;AccountName={names.Item2};EndpointSuffix=core.windows.net")
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
                        }
                    }
                }
            });

            OauthAccessToStorage(function, storage.GetStorageAccountId());
        }
    }
}
