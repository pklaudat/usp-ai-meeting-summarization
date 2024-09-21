using ETL.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Identity;
using System.Net.Http.Headers;
using Azure.Core;
using RetryOptions = DurableTask.Core.RetryOptions;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace ETL
{
    public static class AudioDataFlow
    {
        private static string AdOAuth(string scope)
        {
            // Use the ClientSecretCredential to authenticate
            var clientCredential = new DefaultAzureCredential();

            // Get the OAuth2 token
            var tokenRequestContext = new TokenRequestContext(new[] { scope });
            var accessToken = clientCredential.GetToken(tokenRequestContext);

            return accessToken.Token;
        }

        [Function("ETL_Start")]
        public static async Task AudioProcessingStartAsync(
            [BlobTrigger("wavfiles/{name}", Connection = "")] Stream stream, string name,
            [DurableClient] DurableTaskClient client,
            FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger("ETL_Start");
            logger.LogInformation("New audio file uploaded: {name}", name);

            string storageAccountName = Environment.GetEnvironmentVariable("AzureWebJobsStorage__accountName");

            if (string.IsNullOrEmpty(storageAccountName))
            {
                logger.LogError("Storage Account name environment variable is not set.");
                return;
            }

            // Schedule new orchestration instance with correct input
            try
            {
                var audio = new AudioInputDto
                {
                    Name = name,
                    DataUri = $"https://{storageAccountName}.blob.core.windows.net/wavfiles/{name}",
                    IsBatch = false

                };

                string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                    "TranscribeAudio_Orchestrator", audio);

                logger.LogInformation("Audio Data Flow orchestration started with instanceId: {instanceId}", instanceId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error scheduling new orchestration instance.");
            }
        }

        [Function("TranscribeAudio_Orchestrator")]
        public static async Task TranscribeAudioOrchestrationAsyncTask(
            [OrchestrationTrigger] TaskOrchestrationContext context
        )
        {
            var audio = context.GetInput<AudioInputDto>();
            await context.CallSubOrchestratorAsync("AudioProcessing_Orchestrator", audio);
        }

        private static string GetTranscriptCode(TranscriptDto transcript)
        {

            if (transcript.Self == null)
                return string.Empty;

            var transcriptURLString = transcript.Self;
            var tokenStringsArray = transcriptURLString.Split("/");
            if (tokenStringsArray.Length == 0)
                return string.Empty;

            var transcriptCodeString = tokenStringsArray[tokenStringsArray.Length - 1];
            return transcriptCodeString;

        }


        [Function("AudioProcessing_Orchestrator")]
        public static async Task AudioOrchestrationAsyncTask(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("AudioProcessing_Orchestrator");

            var audio = context.GetInput<AudioInputDto>();

            var transcript = await context.CallActivityAsync<TranscriptDto>
                ("TranscribeTask", audio);

            logger.LogInformation("Transcribe task started");

            var transcriptCode = GetTranscriptCode(transcript);

            logger.LogInformation($"Transcript code is: {transcriptCode}");

            var transcriptCodeData = new TranscriptCodeDto()
            {
                InstanceId = context.InstanceId,
                TranscriptCode = transcriptCode

            };

            // var transcriptsList = await context.CallSubOrchestratorAsync<List<TranscriptDto>>
            //     ("GetTranscriptFiles", transcriptCodeData);

            // var transcripts = new TranscriptResponseDto()
            // {

            //     InstanceId = context.InstanceId,
            //     Transcripts = transcriptsList

            // };

            // await context.CallSubOrchestratorAsync("ProcessAllTranscriptFiles", transcripts);

            using (var cts = new CancellationTokenSource())
            {

                var dueTime = context.CurrentUtcDateTime.AddMinutes(3);
                var timerTask = context.CreateTimer(dueTime, cts.Token);
                var processedTask = context.WaitForExternalEvent<bool>("Processed");
                var completedTask = await Task.WhenAny(processedTask, timerTask);
                var isProcessed = processedTask.Result;

                if (isProcessed == true)
                    logger.LogInformation("Processsed");
                else
                    logger.LogInformation("Not yet");

            }
        }

        private static string GenerateSasToken(string storageUrl, string containerName, BlobSasPermissions permission, int Offset)
        {


            var blobClient = new BlobServiceClient(
                new Uri($"https://{storageUrl}/"),
                new DefaultAzureCredential()
            );

            var startTime = DateTimeOffset.UtcNow;
            var endTime = startTime.AddHours(Offset);

            var sasBuilder = new BlobSasBuilder {
                BlobContainerName = containerName,
                Resource = "c",
                ExpiresOn = endTime
            };

            sasBuilder.SetPermissions(permission);

            var userDelegationKey = blobClient.GetUserDelegationKey(startTime, endTime);

            var sasToken = sasBuilder.ToSasQueryParameters(userDelegationKey, blobClient.AccountName).ToString();

            return $"{blobClient.Uri}?{sasToken}";
        }


        [Function("TranscribeTask")]
        public static async Task<TranscriptDto> TranscribeAsyncTask(
            [ActivityTrigger] AudioInputDto audio, FunctionContext context
        )
        {
            var logger = context.GetLogger("TranscribeTask");

            logger.LogInformation("Started create transcript task ...");

            var destinationContainerUrl = Environment.GetEnvironmentVariable("transcriptsContainerEndpoint");

            logger.LogInformation($"Generating SAS Token for - {destinationContainerUrl}");

            var storageEndpoint = destinationContainerUrl.Split("/")[0];
            var transcriptsContainer = destinationContainerUrl.Split("/")[1];

            logger.LogInformation($"Transcripts will be stored in: {storageEndpoint} - container: {transcriptsContainer}");

            var destinationContainerSas = GenerateSasToken(storageEndpoint, transcriptsContainer, BlobSasPermissions.Write, 2);

            // logger.LogInformation($"Generating SAS for destination container: {destinationContainerSas}");
            
            var transcriptRequest = new TranscriptRequestDto()
            {

                ContentUrls = new List<string>() { audio.DataUri },

                Properties = new TranscriptProperties()
                {

                    DiarizationEnabled = false,
                    WordLevelTimestampsEnabled = false,
                    PunctuationMode = "DictatedAndAutomatic",
                    ProfanityFilterMode = "Masked",
                    Channels = [0],
                    DestinationContainerUrl = destinationContainerSas
                },

                Locale = "en-US",
                DisplayName = "Transcription - meeting:" + audio.Name
            };

            
            var token = AdOAuth("https://cognitiveservices.azure.com/.default");

            // logger.LogInformation($"Retrieved oauth token for Azure Speech - {token}");
  
            var createTranscriptURL = Environment.GetEnvironmentVariable("CREATE_TRANSCRIPT_URL");
            
            var httpClient = new HttpClient();
            
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var transcriptContentString = JsonConvert.SerializeObject(transcriptRequest);
            logger.LogInformation(transcriptContentString);

            var content = new StringContent(transcriptContentString, System.Text.Encoding.UTF8, "application/json");

            // content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            logger.LogInformation($"Create transcription - {createTranscriptURL}");
            
            var transcriptResponse = await httpClient.PostAsync(createTranscriptURL, content);

            var createdTranscript = await transcriptResponse.Content.ReadAsStringAsync();

            logger.LogInformation(createdTranscript.ToString());

            var transcript = JsonConvert.DeserializeObject<TranscriptDto>(createdTranscript);

            // logger.LogInformation($"Transcript url: {transcript.Self}");
            
            return transcript;

        }

        private static RetryOptions GetRetryOptions()
        {

            int.TryParse(Environment.GetEnvironmentVariable("First_Retry_Interval"),
                                                            out int firstRetryInterval);

            int.TryParse(Environment.GetEnvironmentVariable("Retry_TimeOut"),
                                                            out int retryTimeout);

            int.TryParse(Environment.GetEnvironmentVariable("Max_Number_Of_Attempts"),
                                                            out int maxNumberOfAttempts);

            double.TryParse(Environment.GetEnvironmentVariable("Back_Off_Attempts"),
                                                               out double backOffCoefficient);
            
            var retryOptions = new RetryOptions(TimeSpan.FromSeconds(firstRetryInterval),
                                                maxNumberOfAttempts)
            {
                BackoffCoefficient = backOffCoefficient,
                RetryTimeout = TimeSpan.FromMinutes(retryTimeout)               

            };

            return retryOptions;

        }
        
        // [Function("GetTranscriptFiles")]
        // public static async Task<List<TranscriptDto>> GetTranscriptFilesAsync(
        //     [OrchestrationTrigger] TaskOrchestrationContext context,
        //     ILogger logger
        // )
        // {

        //     var transcriptCodeData = context.GetInput<TranscriptCodeDto>();

            


        //     return transcriptList

        // }

    }
}