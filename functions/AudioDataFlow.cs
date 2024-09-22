using ETL.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Identity;
using Azure.Core;
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

            // Get the audio input DTO from the orchestration trigger context
            var audio = context.GetInput<AudioInputDto>();

            // Call the transcription task activity to get the initial transcript
            var transcript = await context.CallActivityAsync<TranscriptDto>("TranscribeTask", audio);

            logger.LogInformation($"Transcribe task started - sub orchestrator id: {context.InstanceId}");

            // Extract transcript code from the transcription response
            var transcriptCode = GetTranscriptCode(transcript);

            logger.LogInformation($"Transcript code is: {transcriptCode}");

            var transcriptStatus = await context.CallSubOrchestratorAsync<string>(
                "GetTranscriptStatus",
                transcriptCode
            );

            logger.LogInformation($"Transcription Job completed with status : {transcriptStatus}.");

            if (transcriptStatus == "Succeeded")
            {
                var transcriptList = await context.CallActivityAsync<List<TranscriptDto>>
                    ("GetTranscriptFiles", transcriptCode);

                var transcriptFiles = new TranscriptResponseDto{
                    InstanceId = context.InstanceId,
                    Transcripts = transcriptList
                };

                logger.LogInformation($"results: {JsonConvert.SerializeObject(transcriptFiles.Transcripts)}");
                // var processResults = await context.CallSubOrchestratorAsync<string>
                //     ("ProcessResults", transcriptFiles);

                logger.LogInformation($"Audio transcription completed - {context.InstanceId}: {audio.Name}");
            }

            using (var cts = new CancellationTokenSource())
            {

                var dueTime = context.CurrentUtcDateTime.AddMinutes(5);
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

            return $"{blobClient.Uri}{containerName}?{sasToken}";
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


        [Function("WaitForTranscriptReadiness")]
        public static async Task<string> WaitForTranscriptReadinessAsyncTask(
            [ActivityTrigger] string transcriptionCode, FunctionContext context
        )
        {
            var logger = context.GetLogger("WaitForTranscriptReadiness");

            try
            {
                var token = AdOAuth("https://cognitiveservices.azure.com/.default");
                var getTranscriptDetailsUrlString = Environment.GetEnvironmentVariable("GET_TRANSCRIPT_URL");
                var getTranscriptDetailsUrl = string.Format(getTranscriptDetailsUrlString, transcriptionCode);

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                var transcriptDetailsResponse = await httpClient.GetAsync(getTranscriptDetailsUrl);

                if (!transcriptDetailsResponse.IsSuccessStatusCode)
                {
                    logger.LogError($"Failed to retrieve transcript details: {transcriptDetailsResponse.StatusCode}");
                    throw new Exception($"Error fetching transcript details: {transcriptDetailsResponse.StatusCode}");
                }

                var transcriptDetails = await transcriptDetailsResponse.Content.ReadAsStringAsync();
                var transcript = JsonConvert.DeserializeObject<TranscriptDto>(transcriptDetails);

                logger.LogInformation($"Transcription status: {transcript.Status.ToUpper()}");

                if (transcript.Status == "Running" || transcript.Status == "InProgress")
                {
                    throw new TranscriptionInProgressException();
                }

                return transcript.Status;
            }
            catch (Exception ex)
            {
                logger.LogError($"Error in WaitForTranscriptReadiness: {ex.Message}");
                throw;
            }
        }

        
        [Function("GetTranscriptStatus")]
        public static async Task<string> GetTranscriptStatusAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context
        )
        {

            var transcriptCode = context.GetInput<string>();

            var logger = context.CreateReplaySafeLogger("GetTranscriptStatus");

            logger.LogInformation($"Checking status for transcript id: {transcriptCode}");

            var retryPolicy = new RetryPolicy(5, TimeSpan.FromMinutes(2))
            {
                HandleAsync = exception => Task.FromResult(exception.Message.Equals("The transcription is still in progress."))
            };

            // Define your retry policy
            var retryOptions = new TaskRetryOptions(retryPolicy);

            // Call the sub-orchestrator with retry policy
            var transcriptStatus = await context.CallActivityAsync<string>(
                "WaitForTranscriptReadiness",
                transcriptCode,
                new TaskOptions
                {
                    Retry = retryOptions
                }
            );

            return transcriptStatus;
        }

        public static async Task<List<TranscriptDto>> GetFilesListAsync(
            [ActivityTrigger] string transcriptCode, string token
        )
        {
            var getTranscriptFilesString = Environment.GetEnvironmentVariable("GET_TRANSCRIPT_FILES_URL");
            var getTranscriptFilesUrl = string.Format(getTranscriptFilesString, transcriptCode);

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var transcriptFiles = await httpClient.GetAsync(getTranscriptFilesUrl);

            var transcriptFilesContent = await transcriptFiles.Content.ReadAsStringAsync();

            var transcripts = JsonConvert.DeserializeObject<TranscriptResponseDto>(transcriptFilesContent);

            return transcripts.Transcripts;
        
        }

        [Function("GetTranscriptFiles")]
        public static async Task<List<TranscriptDto>> GetTranscriptFilesAsync(
            [ActivityTrigger] string transcriptCode, FunctionContext context
        )
        {
            var logger = context.GetLogger("GetTranscriptFiles");

            var token = AdOAuth("https://cognitiveservices.azure.com/.default");

            List<TranscriptDto> transcriptFilesList = null;
            do
            {
                transcriptFilesList = await GetFilesListAsync(transcriptCode, token);
                await Task.Delay(TimeSpan.FromSeconds(3));

            } while (transcriptFilesList.Count <= 1);

            return transcriptFilesList;
        }

    }

    public class TranscriptionInProgressException : Exception
    {
        public TranscriptionInProgressException() 
            : base("The transcription is still in progress.")
        {
        }
    }
}