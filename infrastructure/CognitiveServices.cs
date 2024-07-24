using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.CognitiveServices;
using Pulumi.AzureNative.CognitiveServices.Inputs;
using Pulumi;
using System.Threading.Tasks;


namespace UspMeetingSummz {
    class CognitiveServices
    {
        private ResourceGroup _resourceGroup;
        private string _kind;
        private string _accountName;

        private Account _account;
        private readonly string _location;
        private readonly string _env;

        public CognitiveServices(
            string name, 
            string kind,
            string sku, 
            string location, 
            string env, 
            ResourceGroup resourceGroup)
        {
            this._resourceGroup = resourceGroup;
            this._kind = kind;
            this._accountName = $"{kind.ToLower()}-{name}-{location}-{env}".Replace("services", "");
            this._location = location;
            this._env = env; 

            _account = new Account(_accountName, new AccountArgs
            {
                AccountName = _accountName,
                Kind = kind,
                ResourceGroupName = resourceGroup.Name,
                Location = resourceGroup.Location,
                Properties = new Pulumi.AzureNative.CognitiveServices.Inputs.AccountPropertiesArgs
                {
                    DisableLocalAuth = false,
                    RestrictOutboundNetworkAccess = true,
                    AllowedFqdnList = [],
                    Restore = false
                },
                Sku = new Pulumi.AzureNative.CognitiveServices.Inputs.SkuArgs
                {
                    Name = sku
                }
            },
            new CustomResourceOptions
            {
                DependsOn = { _resourceGroup }
            });
        }

        public Output<string> GetRegionalPublicEndpoint()
        {
            return Output.Format($"{_account.Location}.api.cognitive.microsoft.com");
        }

        public Output<string> GetAccountLocation()
        {
            return Output.Format($"{_account.Location}");
        }
        public Output<string> GetSubscriptionKey()
            {
                return Output.Tuple(_account.Name, _resourceGroup.Name).Apply(t =>
                {
                    var (accountName, resourceGroupName) = t;
                    var keys = ListAccountKeys.InvokeAsync(new ListAccountKeysArgs
                    {
                        AccountName = accountName,
                        ResourceGroupName = resourceGroupName
                    }).Result;
                    return keys.Key1; // Assuming you want the first key
                });
            }
    }
}