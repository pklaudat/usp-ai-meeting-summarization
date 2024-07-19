using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Threading.Tasks;

public static class ChunkProcessor
{
    [Function("ProcessChunk")]
    public static async Task ProcessChunk([QueueTrigger("transcriptionchunks")] string queueMessage, FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger("ProcessChunk");
        logger.LogInformation("Processing chunk from queue.");

        var chunk = Encoding.UTF8.GetString(Convert.FromBase64String(queueMessage));

        // Simulate processing
        await Task.Delay(2000); // Simulates processing delay

        logger.LogInformation("Processed chunk: {chunk}", chunk);
    }
}
