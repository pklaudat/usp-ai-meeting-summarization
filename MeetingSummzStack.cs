using Pulumi;
using Pulumi.AzureNative.Resources;


namespace UspMeetingSummz {
    public class MeetingSummzStack: Stack {
        
        private Config? _config;
        private readonly string? _location;

        private readonly string? _env;

        public MeetingSummzStack() {
            _config = new Config();
            _location = _config.Get("location");
            _env = _config.Get("env");

            var storageResourceGroup = $"rg-storage-{_location}-{_env}";
            var dataFlowResourceGroup = $"rg-dataflow-{_location}-{_env}";

            CreateStorage(storageResourceGroup, "rawaudio");

            CreateDataFlow(dataFlowResourceGroup, "dataflow");

        }

        public void CreateStorage(string resourceGroupName, string storageAccountName)
        {
            
            var storageRg = new ResourceGroup(resourceGroupName, new ResourceGroupArgs {
                ResourceGroupName = resourceGroupName,
                Tags = {
                    { "environment", _env }
                }
            });

            var rawData = new Storage(storageAccountName, _location, _env, storageRg);
        }
        public void CreateDataFlow(string resourceGroupName, string functionName)
        {

            var dataFlowRg = new ResourceGroup(resourceGroupName, new ResourceGroupArgs {
                ResourceGroupName = resourceGroupName,
                Tags = {
                    { "environment", _env }
                }
            });

            var dataOrchestrator = new Function(functionName, _location, _env, dataFlowRg);
        }

        // public void exposeApi()
        // {

        // }

    }
}