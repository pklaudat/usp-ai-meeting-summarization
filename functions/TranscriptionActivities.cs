using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.CognitiveServices.Speech;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

public static class TranscriptionActivities
{
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
}
