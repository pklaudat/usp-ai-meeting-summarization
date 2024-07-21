using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Company.Function
{
    public static class DataFlowOrchestration
    {
        [Function(nameof(DataFlowOrchestration))]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            // Log input type and content
            var input = context.GetInput<BlobInputType>();
            var logger = context.CreateReplaySafeLogger(nameof(DataFlowOrchestration));

            var outputs = new List<string>();

            // Ensure the input is correctly retrieved before calling the activity
            if (!string.IsNullOrEmpty(input.BlobUri))
            {
                var transcription = await context.CallActivityAsync<string>("SpeechToText", input.BlobUri);
                outputs.Add(transcription);
                logger.LogInformation("Transcription result: {transcription}", transcription);
            }
            else
            {
                logger.LogError("Blob URI is null or empty.");
            }

            return outputs;
        }

        [Function("DataFlowOrchestration_BlobStart")]
        public static async Task Run(
            [BlobTrigger("wavfiles/{name}", Connection = "")] Stream stream, string name,
            [DurableClient] DurableTaskClient client,
            FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger("DataFlowOrchestration_BlobStart");
            logger.LogInformation("New audio file uploaded: {name}", name);

            string storageAccountName = Environment.GetEnvironmentVariable("AzureWebJobsStorage__accountName");
            if (string.IsNullOrEmpty(storageAccountName))
            {
                logger.LogError("AzureStorageAccountName environment variable is not set.");
                return;
            }

            string containerName = "wavfiles";
            string blobUri = $"https://{storageAccountName}.blob.core.windows.net/{containerName}/{name}";

            logger.LogInformation("Blob URI: {blobUri}", blobUri);

            // Schedule new orchestration instance with correct input
            try
            {
                var input = new
                {
                    Name = name,
                    BlobUri = blobUri
                };

                string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                    nameof(DataFlowOrchestration), input);

                logger.LogInformation("Data flow orchestration started with instanceId: {instanceId}", instanceId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error scheduling new orchestration instance.");
            }
        }
    }

     public class BlobInputType
    {
        public string Name { get; set; }
        public string BlobUri { get; set; }
    }
}
