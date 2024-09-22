using Grpc.Core;
using Newtonsoft.Json;

namespace ETL.Models {

    public class TranscriptRequestDto
    {

        [JsonProperty("contentUrls")]
        public List<string> ContentUrls { get; set; }

        [JsonProperty("properties")]
        public TranscriptProperties Properties { get; set; }

        [JsonProperty("locale")]
        public string Locale { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

    }

    public class TranscriptProperties
    {

        [JsonProperty("diarizationEnabled")]
        public bool DiarizationEnabled { get; set; }

        [JsonProperty("wordLevelTimestampsEnabled")]
        public bool WordLevelTimestampsEnabled { get; set; }

        [JsonProperty("ponctuationMode")]
        public string PunctuationMode { get; set; }

        [JsonProperty("profanityFilterMode")]
        public string ProfanityFilterMode { get; set; }

        [JsonProperty("channels")]
        public List<int> Channels {get; set;}

        [JsonProperty("destinationContainerUrl")]
        public string DestinationContainerUrl { get; set; }

    }   

    public class TranscriptResponseDto
    {

        public string InstanceId { get; set; }

        [JsonProperty("values")]
        public List<TranscriptDto> Transcripts { get; set; }        

    }

    // public enum TranscriptionStatus
    // {
    //     NotStarted,    // The transcription job has not started yet.
    //     Running,       // The transcription job is currently in progress.
    //     Succeeded,     // The transcription job completed successfully.
    //     Failed,        // The transcription job failed.
    //     Cancelled      // The transcription job was cancelled.
    // }

    public class TranscriptDto
    {

        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("name")]
        public string? Name {get; set;}

        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("links")]
        public TranscriptLinkDto Links { get; set; }

        [JsonProperty("status")]
        public string? Status {get; set;}

    }

    public class TranscriptLinkDto
    {

        [JsonProperty("contentUrl")]
        public string ContentUrl { get; set; }
    }

    public class TranscriptCodeDto
    {

        public string InstanceId { get; set; }
        public string TranscriptCode { get; set; }

    }
}