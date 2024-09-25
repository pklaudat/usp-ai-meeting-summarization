using System.Collections.Generic;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.CognitiveServices;
using Pulumi.AzureNative.CognitiveServices.Inputs;


namespace UspMeetingSummz
{
    class CognitiveServices
    {
        private ResourceGroup _resourceGroup;
        private string _kind;
        private string _accountName;

        private Account _account;
        private readonly string _location;
        private readonly string _env;

        public Output<string> AccountName { get; private set; }

        public Output<string> CustomDomainEndpoint { get; private set; }

        public Output<string> PrincipalId { get; private set; }

        public Output<string> ResourceGroupId { get; private set; }

        public CognitiveServices(
            string name, 
            string kind,
            string sku, 
            string location, 
            string env,
            List<string> allowedFqdns, 
            ResourceGroup resourceGroup)
        {
            this._resourceGroup = resourceGroup;
            this._kind = kind;
            this._accountName = $"{kind.ToLower()}-{name}-{location}-{env}".Replace("services", "");
            this._location = location;
            this._env = env;

            // Use the proper endpoint domain for Cognitive Services
            CustomDomainEndpoint = Output.Create($"{_accountName.Replace("_", "-")}.cognitiveservices.azure.com");
            
            ResourceGroupId = resourceGroup.Id;

            _account = new Account(_accountName, new AccountArgs
            {
                AccountName = _accountName,
                Kind = kind,
                Identity = new IdentityArgs
                {
                    Type = Pulumi.AzureNative.CognitiveServices.ResourceIdentityType.SystemAssigned,
                },
                ResourceGroupName = resourceGroup.Name,
                Location = resourceGroup.Location,
                Properties = new AccountPropertiesArgs
                {
                    DisableLocalAuth = true,
                    RestrictOutboundNetworkAccess = true,
                    AllowedFqdnList = allowedFqdns,
                    Restore = false,
                    CustomSubDomainName = _accountName.Replace("_", "-")
                    // NetworkAcls = new NetworkRuleSetArgs
                    // {
                    //     DefaultAction = "Deny",  // Deny access unless explicitly allowed
                    //     // Define empty rules for now
                    //     // VirtualNetworkRules = new VirtualNetworkRuleArgs[] {},
                    //     // IpRules = new IPRuleArgs[] {}
                    // }
                },

                Sku = new SkuArgs
                {
                    Name = sku
                }
            },
            new CustomResourceOptions
            {
                DependsOn = { _resourceGroup }
            });

            PrincipalId = _account.Identity.Apply(identity => identity.PrincipalId);
            AccountName = _account.Name;
        }

        // Function to get the location of the Cognitive Services account
        public Output<string> GetAccountLocation()
        {
            return Output.Format($"{_account.Location}");
        }

        public void CreateOpenAIModel(string modelName, string modelVersion, int capacity)
        {
            new Pulumi.AzureNative.CognitiveServices.Deployment(
                modelName,
                new Pulumi.AzureNative.CognitiveServices.DeploymentArgs
                {
                    AccountName = _account.Name,
                    ResourceGroupName = _resourceGroup.Name,
                    DeploymentName = modelName,
                    Sku = new SkuArgs
                    {
                        Name = "Standard",
                        Capacity = capacity
                    },
                    Properties = new DeploymentPropertiesArgs
                    {
                        Model = new DeploymentModelArgs
                        {
                            Version = modelVersion,
                            Name = modelName,
                            Format = "OpenAI"
                        }
                        
                    }
                    
                   
                }
                
            
            );
        }

        // Function to get the subscription key for the Cognitive Services account
        public Output<string> GetSubscriptionKey()
        {
            return Output.Tuple(_account.Name, _resourceGroup.Name).Apply(async t =>
            {
                var (accountName, resourceGroupName) = t;
                var keys = await ListAccountKeys.InvokeAsync(new ListAccountKeysArgs
                {
                    AccountName = accountName,
                    ResourceGroupName = resourceGroupName
                });
                return keys.Key1; // Returning the first key
            });
        }
    }
    
    public class ModelSpecs
    {
        public string Name {get; set;}
        public string Version {get; set;}
        public int Capacity {get; set;}
    }
}
