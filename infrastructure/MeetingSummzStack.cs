using System.Collections.Generic;
using Pulumi;
using Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Web.Inputs;

namespace UspMeetingSummz
{
    public class MeetingSummzStack : Stack
    {
        private Config? _config;
        private readonly string? _location;
        private readonly string? _env;

        private readonly string _cognitiveServicesUserRoleId = "a97b65f3-24c7-4388-baec-2e87135dc908";

        private readonly string _blobDataContributorRoleId = "ba92f5b4-2d11-453d-a403-e96b0029c9fe";
        
        [Output]
        public Output<string> FunctionName { get; private set; }

        public MeetingSummzStack()
        {
            _config = new Config();
            _location = _config.Get("location");
            _env = _config.Get("env");

            var storageResourceGroup = $"rg-storage-{_location}-{_env}";
            var dataFlowResourceGroup = $"rg-dataflow-{_location}-{_env}";
            var speechResourceGroup = $"rg-speech-{_location}-{_env}";

            // CreateStorage(storageResourceGroup, "rawaudio");
            var dataSourceEndpoint = $"dataflow{_location}{_env}.blob.core.windows.net";

            var aiSpeech = CreateAiServices(speechResourceGroup, new List<string> {
               dataSourceEndpoint
            });

            var createTranscriptUrl = Output.Format($"https://{aiSpeech.CustomDomainEndpoint.Apply(endpoint => $"{endpoint}")}/speechtotext/v3.2/transcriptions");
            var getTranscriptUrl = Output.Format($"https://{aiSpeech.CustomDomainEndpoint.Apply(endpoint => $"{endpoint}")}/speechtotext/v3.2/transcriptions/{0}/files");

            var function = CreateDataFlow(dataFlowResourceGroup, "dataflow", new List<NameValuePairArgs>
            {
                new NameValuePairArgs
                {
                    Name = "AzureSpeechEndpoint",
                    Value = aiSpeech.CustomDomainEndpoint.Apply(endpoint => $"{endpoint}")
                },
                new NameValuePairArgs
                {
                    Name = "AzureSpeechRegion",
                    Value = aiSpeech.GetAccountLocation()
                },
                new NameValuePairArgs
                {
                    Name = "CREATE_TRANSCRIPT_URL",
                    Value = createTranscriptUrl
                },
                new NameValuePairArgs
                {
                    Name = "GET_TRANSCRIPT_URL",
                    Value = getTranscriptUrl
                },
                new NameValuePairArgs
                {
                    Name = "transcriptsContainerEndpoint",
                    Value = $"{dataSourceEndpoint}/transcripts"
                }
            });

            FunctionName = function.FunctionName;

            new RoleAssignment($"aispeechstorageaccess-{_blobDataContributorRoleId}", new RoleAssignmentArgs
                {
                    PrincipalId = aiSpeech.PrincipalId.Apply(n=>n),
                    RoleDefinitionId = $"/providers/Microsoft.Authorization/roleDefinitions/{_blobDataContributorRoleId}",
                    Scope = function.ResourceGroupId.Apply(r=>r),
                    PrincipalType = "ServicePrincipal"
                });

            new RoleAssignment($"functionspeechaccess-{_cognitiveServicesUserRoleId}", new RoleAssignmentArgs
                {
                    PrincipalId = function.FunctionPrincipalId.Apply(n=>n),
                    RoleDefinitionId = $"/providers/Microsoft.Authorization/roleDefinitions/{_cognitiveServicesUserRoleId}",
                    Scope = aiSpeech.ResourceGroupId.Apply(r=>r),
                    PrincipalType = "ServicePrincipal"
                }
            );
        }

        private Function CreateDataFlow(string resourceGroupName, string functionName, List<NameValuePairArgs> environmentVariables)
        {
            var dataFlowRg = new ResourceGroup(resourceGroupName, new ResourceGroupArgs
            {
                ResourceGroupName = resourceGroupName,
                Tags = {
                    { "environment", _env }
                }
            });

            var dataOrchestrator = new Function(functionName, _location, _env, dataFlowRg, "meet_summz", environmentVariables);
            return dataOrchestrator;
        }

        private CognitiveServices CreateAiServices(string resourceGroupName, List<string> allowedFqdnList)
        {
            var speechRg = new ResourceGroup(resourceGroupName, new ResourceGroupArgs
            {
                ResourceGroupName = resourceGroupName,
                Tags = {
                    { "environment", _env }
                }
            });

            var aiSpeech = new CognitiveServices("meet_summz", "SpeechServices", "S0", _location, _env, allowedFqdnList ,speechRg);

            var openai = new CognitiveServices("meet_summz", "OpenAI", "S0", _location, _env, new List<string> {}, speechRg);

            return aiSpeech;
        }
    }
}
