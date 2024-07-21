using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Azure.Storage.Blobs;
using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Extensions.Azure;

namespace Company.Function
{
    public static class TranscriptionActivities
    {
        [Function("SpeechToText")]
        public static async Task<string> ConvertAudioToText([ActivityTrigger] string blobUri, FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger("SpeechToText");
            logger.LogInformation("Starting transcription for file: {blobUri}", blobUri);

            try
            {
                string subscriptionKey = Environment.GetEnvironmentVariable("AzureSpeechSubscriptionKey");
                string serviceRegion = Environment.GetEnvironmentVariable("AzureSpeechRegion");

                if (string.IsNullOrEmpty(subscriptionKey) || string.IsNullOrEmpty(serviceRegion))
                {
                    logger.LogError("AzureSpeechSubscriptionKey or AzureSpeechRegion is not set in the environment variables.");
                    return "Error: AzureSpeechSubscriptionKey or AzureSpeechRegion is missing.";
                }

                var speechConfig = SpeechConfig.FromSubscription(subscriptionKey, serviceRegion);

                // Create a BlobClient to read the audio file from Azure Blob storage
                var credential = new DefaultAzureCredential();
                BlobClient blobClient = new BlobClient(new Uri(blobUri), credential);

                Stream audioStream = new MemoryStream();
                await blobClient.DownloadToAsync(audioStream);
                audioStream.Position = 0; // Reset the stream position to the beginning

                var audioInputStream = AudioInputStream.CreatePullStream(new CustomAudioInputStreamCallback(audioStream));
                var audioConfig = AudioConfig.FromStreamInput(audioInputStream);
                var recognizer = new SpeechRecognizer(speechConfig, audioConfig);
                var result = await recognizer.RecognizeOnceAsync();

                logger.LogInformation("result for the transcription: {result}", result.Text);

                switch (result.Reason)
                {
                    case ResultReason.RecognizedSpeech:
                        logger.LogInformation($"Recognition successful: {result.Text}");
                        return result.Text;
                    case ResultReason.NoMatch:
                        logger.LogInformation("No speech could be recognized. {result}", result.Text);
                        return "No speech could be recognized.";
                    case ResultReason.Canceled:
                        var cancellation = CancellationDetails.FromResult(result);
                        logger.LogError($"CANCELED: Reason={cancellation.Reason}");
                        if (cancellation.Reason == CancellationReason.Error)
                        {
                            logger.LogError($"CANCELED: ErrorCode={cancellation.ErrorCode}");
                            logger.LogError($"CANCELED: ErrorDetails={cancellation.ErrorDetails}");
                        }
                        return "Transcription failed.";
                    default:
                        logger.LogError("Unexpected result reason.");
                        return "Unexpected result.";
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred during the transcription process.");
                return "Error: An unexpected error occurred.";
            }
        }

        private class CustomAudioInputStreamCallback : PullAudioInputStreamCallback
        {
            private readonly Stream _stream;

            public CustomAudioInputStreamCallback(Stream stream)
            {
                _stream = stream;
            }

            public override int Read(byte[] dataBuffer, uint size)
            {
                try
                {
                    return _stream.Read(dataBuffer, 0, (int)size);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading audio stream: {ex.Message}");
                    return 0;
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _stream.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
