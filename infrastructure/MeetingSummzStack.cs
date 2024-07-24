using System.Collections.Generic;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Web.Inputs;

namespace UspMeetingSummz
{
    public class MeetingSummzStack : Stack
    {
        private readonly Config _config;
        private readonly string _location;
        private readonly string _env;

        public MeetingSummzStack()
        {
            _config = new Config();
            _location = _config.Require("location");
            _env = _config.Require("env");

            var storageResourceGroup = $"rg-storage-{_location}-{_env}";
            var dataFlowResourceGroup = $"rg-dataflow-{_location}-{_env}";
            var speechResourceGroup = $"rg-speech-{_location}-{_env}";

            // CreateStorage(storageResourceGroup, "rawaudio");

            var aiSpeech = CreateAiServices(speechResourceGroup);

            CreateDataFlow(dataFlowResourceGroup, "dataflow", new List<NameValuePairArgs>
            {
                new NameValuePairArgs
                {
                    Name = "AzureSpeechEndpoint",
                    Value = aiSpeech.GetRegionalPublicEndpoint()
                },
                new NameValuePairArgs
                {
                    Name = "AzureSpeechRegion",
                    Value = aiSpeech.GetAccountLocation()
                },
                new NameValuePairArgs
                {
                    Name = "AzureSpeechSubscriptionKey",
                    Value = aiSpeech.GetSubscriptionKey()
                }
            });
        }

        private void CreateDataFlow(string resourceGroupName, string functionName, List<NameValuePairArgs> environmentVariables)
        {
            var dataFlowRg = new ResourceGroup(resourceGroupName, new ResourceGroupArgs
            {
                ResourceGroupName = resourceGroupName,
                Tags = 
                {
                    { "environment", _env }
                }
            });

            var dataOrchestrator = new Function(functionName, _location, _env, dataFlowRg, "meet_summz", environmentVariables);
        }

        private CognitiveServices CreateAiServices(string resourceGroupName)
        {
            var speechRg = new ResourceGroup(resourceGroupName, new ResourceGroupArgs
            {
                ResourceGroupName = resourceGroupName,
                Tags = 
                {
                    { "environment", _env }
                }
            });

            var aiSpeech = new CognitiveServices("meet_summz", "SpeechServices", "S0", _location, _env, speechRg);

            return aiSpeech;
        }
    }
}
