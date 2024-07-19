using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.SpeechClient;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Company.Function
{
    public static class DataFlowOrchestration
    {

        [Function(nameof(DataFlowOrchestration))]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var input = context.GetInput<(string name, string content)>();
            var logger = context.CreateReplaySafeLogger(nameof(DataFlowOrchestration));
            logger.LogInformation("Starting orchestration for file: {name}", input.name);

            var outputs = new List<string>();

            var transcription = await context.CallActivityAsync<string>("SpeechToText", input.content);
            outputs.Add(transcription);

            logger.LogInformation(transcription);

            return outputs;
        }

        [Function("SpeechToText")]
        public static async Task<string> ConvertAudioToText([ActivityTrigger] string audioFilePath, FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger("SpeechToText");
            logger.LogInformation("Converting audio to text.");

            // Configure the speech recognizer
            string subscriptionKey = Environment.GetEnvironmentVariable("AzureSpeechSubscriptionKey");
            string serviceRegion = Environment.GetEnvironmentVariable("AzureSpeechRegion");
            var speechConfig = SpeechConfig.FromSubscription(subscriptionKey, serviceRegion);

            // Create the speech recognizer
            var audioConfig = AudioConfig.FromWavFileInput(audioFilePath);
            var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            // Perform the transcription
            var result = await recognizer.RecognizeOnceAsync();

            if (result.Reason == ResultReason.RecognizedSpeech)
            {
                logger.LogInformation($"Recognized: {result.Text}");
                return result.Text;
            }
            else if (result.Reason == ResultReason.NoMatch)
            {
                logger.LogInformation("No speech could be recognized.");
                return "No speech could be recognized.";
            }
            else if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = CancellationDetails.FromResult(result);
                logger.LogError($"CANCELED: Reason={cancellation.Reason}");
                if (cancellation.Reason == CancellationReason.Error)
                {
                    logger.LogError($"CANCELED: ErrorCode={cancellation.ErrorCode}");
                    logger.LogError($"CANCELED: ErrorDetails={cancellation.ErrorDetails}");
                }
                return "Transcription failed.";
            }

            return "Unexpected result.";
        }

        // [Function("SpeechToText")]
        // public static Task<string> ConvertAudioToText([ActivityTrigger] string audioContent, FunctionContext executionContext)
        // {
        //     var logger = executionContext.GetLogger("SpeechToText");
        //     logger.LogInformation("Converting audio to text.");

        //     var subscriptionKey = Environment.GetEnvironmentVariable("SpeechSubscriptionKey");
        //     var endpoint = Environment.GetEnvironmentVariable("SpeechEndpoint");

        //     var client = new Speech.SpeechClient(new Uri(endpoint), new AzureKeyCredential(subscriptionKey));

        //     // Assume we have a method to transcribe the audio
        //     var transcription = await TranscribeAudioAsync(client, audioContent);

        //     return transcription;
        // }

        // private static async Task<string> TranscribeAudioAsync(SpeechClient client, string audioContent)
        // {
        //     // Implement your transcription logic here
        //     return await Task.FromResult("Transcribed text");
        // }

        [Function("DataFlowOrchestration_BlobStart")]
        public static async Task Run(
            [BlobTrigger("wavfiles/{name}", Connection = "")]Stream stream, string name,
            [DurableClient] DurableTaskClient client,
            FunctionContext executionContext)
        {
            using var blobStreamReader = new StreamReader(stream);
            var content = await blobStreamReader.ReadToEndAsync();
            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(DataFlowOrchestration), name);
            
            ILogger logger = executionContext.GetLogger("DataFlowOrchestration_BlobStart");
            logger.LogInformation("New audio file upload into the raw data source {name}", name);
            logger.LogInformation("Data flow orchestration started for {instanceId}", instanceId);
        }
    }
}
