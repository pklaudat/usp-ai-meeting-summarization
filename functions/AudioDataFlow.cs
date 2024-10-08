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
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using System.Net;
using Castle.Core.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;


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
            var logger = context.CreateReplaySafeLogger("TranscribeAudio_Orchestrator");
            var audio = context.GetInput<AudioInputDto>();
            // string meetingId = await context.CallSubOrchestratorAsync<string>("AudioProcessing_Orchestrator", audio);
            string meetingId = "0b694dec8ee35f4999c523c06db352fd";
            logger.LogInformation("Meeting ID: {0} has been transcripted...", meetingId);
            await context.CallSubOrchestratorAsync("Summarization_Orchestrator", meetingId);
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

        [Function("ProcessResults")]
        public static async Task<string> ProcessResultAsyncTask(
            [ActivityTrigger] TranscriptResponseDto transcriptResponse, FunctionContext context
        )
        {
            var logger = context.GetLogger("ProcessResults");

            if (transcriptResponse.Transcripts == null || transcriptResponse.Transcripts.Count <= 1)
                return null;

            var processedFiles = transcriptResponse.Transcripts
                .Where((TranscriptDto transcript) =>
                {
                    return (transcript.Kind.Equals("Transcription") == true);
                }).ToList();

            var storageResultsUrl = Environment.GetEnvironmentVariable("transcriptsResultContainerEndpoint");

            var destinationContainer = storageResultsUrl.Split("/")[1];
            var storageUrl = storageResultsUrl.Split("/")[0];

            logger.LogInformation(storageUrl);
            logger.LogInformation(storageUrl);
    
            var blobClient = new BlobServiceClient(
                new Uri($"https://{storageUrl}/"),
                new DefaultAzureCredential()
            );

            var destinationContainerClient = blobClient.GetBlobContainerClient(destinationContainer);

            try
            {
                for (int i=0; i< processedFiles.Count; i++)
                {

                    var fileUrl = processedFiles[i].Links.ContentUrl;
                    logger.LogInformation($"copy operation for {fileUrl} - filename: {processedFiles[i].Name}");
                    var dataDestination = destinationContainerClient.GetBlobClient($"{transcriptResponse.InstanceId}/{processedFiles[i].Name}");
                    await dataDestination.StartCopyFromUriAsync(new Uri(fileUrl));
                }

                return "All Files copied successfuly.";

            }
            catch (Exception ex)
            {
                logger.LogError($"Error copying blob: {ex.Message}");
                throw;
            }

        }


        [Function("AudioProcessing_Orchestrator")]
        public static async Task<string> AudioOrchestrationAsyncTask(
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
                
                var processResults = context.CallActivityAsync<string>
                    ("ProcessResults", transcriptFiles);


                logger.LogInformation($"Audio transcription completed - {context.InstanceId}");

                return transcriptFiles.InstanceId;
            }

            // using (var cts = new CancellationTokenSource())
            // {

            //     var dueTime = context.CurrentUtcDateTime.AddMinutes(5);
            //     var timerTask = context.CreateTimer(dueTime, cts.Token);
            //     var processedTask = context.WaitForExternalEvent<bool>("Processed");
            //     var completedTask = await Task.WhenAny(processedTask, timerTask);
            //     var isProcessed = processedTask.Result;

            //     if (isProcessed == true)
            //         logger.LogInformation("Processsed");
            //     else
            //         logger.LogInformation("Not yet");

            // }

            return null;
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
                    Channels = [0 , 1],
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

            if (transcript.Status == "Running" || transcript.Status == "InProgress" || transcript.Status == "NotStarted")
            {
                throw new TranscriptionInProgressException();
            }

            return transcript.Status;
            // catch (Exception ex)
            // {
            //     logger.LogError($"Error in WaitForTranscriptReadiness: {ex.Message}");
            //     throw;
            // }
        }

        
        [Function("GetTranscriptStatus")]
        public static async Task<string> GetTranscriptStatusAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context
        )
        {

            var transcriptCode = context.GetInput<string>();

            var logger = context.CreateReplaySafeLogger("GetTranscriptStatus");

            logger.LogInformation($"Checking status for transcript id: {transcriptCode}");

            var retryPolicy = new RetryPolicy(80, TimeSpan.FromSeconds(10))
            {
                HandleAsync = exception => 
                {
                    logger.LogWarning("Retry triggered due to exception: {0}", exception.Message);
                    return Task.FromResult(exception.Message.Equals("The transcription is still in progress."));
                }
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


        [Function("TokenizeTranscript")]
        public static List<MeetingTokensDto> TokenizeTranscript([ActivityTrigger] List<MeetingDto> meetings, FunctionContext context)
        {

            var logger = context.GetLogger("TokenizeTranscript");

            var tokenSize = int.Parse(Environment.GetEnvironmentVariable("TOKEN_SIZE") ?? "10");

            var tokens = new List<MeetingTokensDto> {};


            foreach (var meeting in meetings)
            {
                var meetingToken = new MeetingTokensDto
                {
                    Source = meeting.Source,
                    ChunkList = new List<MeetingChunkDto> {}
                };

                foreach (var ctx in meeting.MeetingContentList)
                {
                    string fullContent = ctx.MaskedITN;
                    int contentLength = fullContent.Length;

                    for (int chunkStart=0; chunkStart < contentLength; chunkStart += tokenSize )
                    {
                        var chunkSize = Math.Min(tokenSize, contentLength - chunkStart);
                        string chunkContent = fullContent.Substring(chunkStart, chunkSize);

                        var meetingChunk = new MeetingChunkDto
                        {
                            Size = chunkSize,
                            Content = chunkContent
                        };

                        // logger.LogInformation("Chunk: {0} | size: {1}", chunkContent, chunkSize);

                        meetingToken.ChunkList.Add(meetingChunk);
                    }

                }

                tokens.Add(meetingToken);
                
            }


            foreach(var tt in tokens)
            {
                logger.LogInformation("token {0} - number of chunks: {1}", tokens.IndexOf(tt), tt.ChunkList.ToArray().Length);
            }
            
            return tokens;
        }

        public static List<MeetingDto> OpenMeetingContent(List<string> meetingFiles, BlobContainerClient containerClient, ILogger logger)
        {

            var dtoList = new List<MeetingDto> {};

            foreach (var meetingText in meetingFiles)
            {
                // logger.LogInformation("open file: {0}", meetingText);

                BlobClient blob = containerClient.GetBlobClient(meetingText);
                
                var blobContent = blob.OpenRead();

                using (var streamReader = new StreamReader(blobContent))
                {
                    var jsonContent = streamReader.ReadToEnd();

                    // logger.LogInformation(jsonContent);
                    
                    var meetingDto = JsonConvert.DeserializeObject<MeetingDto>(jsonContent);

                    dtoList.Add(meetingDto);
                }
            }

            logger.LogInformation("Number of meeting files: {0}", dtoList.Count);

            return dtoList;

        }

        public static async Task<PromptResponseDto> CallOpenAI(
            PromptRequestDto prompt, ILogger logger
        )
        {

            var completionsEndpoint = String.Format(
                Environment.GetEnvironmentVariable("CHAT_COMPLETIONS_URL"),
                Environment.GetEnvironmentVariable("OPENAI_MODEL")
            );

            var token = AdOAuth("https://cognitiveservices.azure.com/.default");
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var requestContent = new StringContent(JsonConvert.SerializeObject(prompt), System.Text.Encoding.UTF8, "application/json");

            var response = await PostWithRetryAsync(httpClient, completionsEndpoint, requestContent, 10, logger);
            
            var completions = await response.Content.ReadAsStringAsync();
            logger.LogInformation(completions);
            var modelResponse = JsonConvert.DeserializeObject<PromptResponseDto>(completions);
            return modelResponse;

        }

        static async Task<HttpResponseMessage> PostWithRetryAsync(HttpClient client, string url, HttpContent content, int maxRetries, ILogger logger)
        {
            int retryCount = 0;
            int baseDelay = 2000; // Start with a 2-second delay
            HttpResponseMessage response;

            do
            {
                response = await client.PostAsync(url, content);

                if (response.StatusCode == HttpStatusCode.TooManyRequests) // Handle 429 status code
                {
                    retryCount++;
                    logger.LogWarning($"Request throttled (429). Retry {retryCount} of {maxRetries}.");

                    if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
                    {
                        if (int.TryParse(retryAfterValues.First(), out int retryAfterSeconds))
                        {
                            await Task.Delay(retryAfterSeconds * 1000); // Wait based on Retry-After header
                        }
                    }
                    else
                    {
                        // Exponential backoff if no 'Retry-After' header
                        var delay = baseDelay * (int)Math.Pow(2, retryCount - 1); // Exponential backoff
                        await Task.Delay(delay);
                    }
                }
                else if (!response.IsSuccessStatusCode)
                {
                    // Log any other error responses
                    logger.LogError($"Failed request with status code {response.StatusCode}. Response: {await response.Content.ReadAsStringAsync()}");
                    throw new HttpRequestException($"Failed request with status code {response.StatusCode}");
                }

            } while (response.StatusCode == HttpStatusCode.TooManyRequests && retryCount < maxRetries);

            if (retryCount == maxRetries)
            {
                logger.LogError($"Exceeded maximum retry attempts ({maxRetries}) for throttled request.");
                throw new HttpRequestException($"Exceeded max retry attempts due to repeated 429 errors.");
            }

            return response;
        }

        [Function("SummarizeChunks")]
        public static async Task<PromptResponseDto> SummarizeChunksAsyncTask(
            [ActivityTrigger] List<MeetingTokensDto> meetingTokens, FunctionContext context
        )
        {
            var logger = context.GetLogger("SummarizeChunks");
            var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");

            var completionsEndpoint = String.Format(
                Environment.GetEnvironmentVariable("CHAT_COMPLETIONS_URL"),
                model
            );

            var token = AdOAuth("https://cognitiveservices.azure.com/.default");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            logger.LogInformation("Building prompt to summarize the meeting text.");

            var systemMessage = new PromptDto
            {
                Role = "system",
                Content = "Summarize any given text in at most 4 setences, each setence must not exceed 15 words. Keep only the most important details such as: meeting core ideas and any future action as outcome."
            };

            // List to store the tasks for chunk summarization
            var chunkSummarizationTasks = new List<Task<HttpResponseMessage>>(); 

            // Loop through each meeting and process chunks in parallel
            foreach (var meet in meetingTokens)
            {
                foreach (var chunk in meet.ChunkList)
                {
                    // Prepare a prompt for each chunk
                    var prompt = new PromptRequestDto
                    {
                        Model = model,
                        Messages = new List<PromptDto>
                        {
                            systemMessage,
                            new PromptDto
                            {
                                Role = "user",
                                Content = chunk.Content // Chunk content sent for summarization
                            }
                        }
                    };

                    logger.LogInformation("User prompt: {0}", chunk.Content);

                    // Create a task to call OpenAI and add it to the task list
                    var requestContent = new StringContent(JsonConvert.SerializeObject(prompt), System.Text.Encoding.UTF8, "application/json");

                    // Wrap the HTTP call in a task, but do not await it yet
                    var chunkTask = PostWithRetryAsync(httpClient, completionsEndpoint, requestContent, 10, logger);
                    chunkSummarizationTasks.Add(chunkTask);  // Add the task to the list
                }
            }

            logger.LogInformation("Total tasks created: {0}", chunkSummarizationTasks.Count);

            try
            {
                // Wait for all the tasks to complete asynchronously
                var chunkResponses = await Task.WhenAll(chunkSummarizationTasks);

                logger.LogInformation("All chunk requests completed. Processing responses...");

                var chunkSummarizations = new List<PromptResponseDto>();

                // Process each response
                foreach (var response in chunkResponses)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        var summary = JsonConvert.DeserializeObject<PromptResponseDto>(jsonResponse);
                        chunkSummarizations.Add(summary);
                    }
                    else
                    {
                        logger.LogError("Error with status code: {0}", response.StatusCode);
                    }
                }

                // Collect the summarized content from all completed tasks
                var summaries = chunkSummarizations
                    .Select(response => response.Choices[0].Message.Content) // Assuming this is the structure of the response
                    .ToList();

                // Combine the summarized chunks for a final overall meeting summarization
                var totalContent = string.Join("\n", summaries); // Join all chunk summaries into one

                // logger.LogInformation("Total content for summarization: {0}", totalContent);

                var finalPrompt = new PromptRequestDto
                {
                    Model = model,
                    Messages = new List<PromptDto>
                    {
                        new PromptDto
                        {
                            Role = "system",
                            Content = "You're an AI assistance specialized in Meeting Summarization that provides clear summarization based on four topics: 1. overview, 2. attendees and roles, 3. key discussion points  and 4. decisions or future actions.  Avoid terms such as \"The meeting discussed\"."
                        },
                        new PromptDto
                        {
                            Role = "user",
                            Content = $"Summarize the following meeting based on these summaries:\n{totalContent}"
                        }
                    }
                };

                var finalMeetingSummarization = await CallOpenAI(finalPrompt, logger);

                // logger.LogInformation("Final summarization: {0}", finalMeetingSummarization.Choices[0].Message.Content);

                return finalMeetingSummarization;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred during chunk summarization.");
                throw; // Rethrow the exception if necessary
            }

        }

        [Function("Summarization_Orchestrator")]
        public static async Task<string> SummarizationAsyncTask(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            
            var meetingId = context.GetInput<string>();

            var logger = context.CreateReplaySafeLogger("Summarization_Orchestrator");
            logger.LogInformation("Summarization process has been started. Meeting ID: {0}", meetingId);

            string meetingBlobEndpoint = Environment.GetEnvironmentVariable("transcriptsResultContainerEndpoint") + $"/{meetingId}";

            logger.LogInformation("Meeting ID results stored in: {0}", meetingBlobEndpoint);

            string storageUrl = meetingBlobEndpoint.Split("/")[0];

            var resultsContainerClient = new BlobServiceClient(
                new Uri($"https://{storageUrl}/"),
                new DefaultAzureCredential()
            ).GetBlobContainerClient($"results");

            var blobList = resultsContainerClient.GetBlobs();

            var meetingFiles = new List<string> {};

            foreach (var blob in blobList)
            {
                
                if (blob.Name.Contains(meetingId))
                {
                    meetingFiles.Add(blob.Name);
                    logger.LogInformation("Found blob {0} for meeting id: {1} under the transcript results.", blob.Name, meetingId);
                }
            }

            logger.LogInformation("Extract the meeting content...");

            var meetings = OpenMeetingContent(meetingFiles, resultsContainerClient, logger);

            logger.LogInformation("Processed {0} files", meetings.Count);

            var tokens = await context.CallActivityAsync<List<MeetingTokensDto>>("TokenizeTranscript", meetings);

            logger.LogInformation("Chunk summarization started ... number of meeting tokens: {0}", tokens.ToArray().Length);

            await context.CallActivityAsync("SummarizeChunks", tokens);


            return "ok";

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