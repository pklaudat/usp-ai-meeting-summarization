

using Newtonsoft.Json;

namespace ETL.Models {
    
     public class MeetingDto
    {
        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("duration")]
        public string Duration { get; set; }
        
        [JsonProperty("combinedRecognizedPhrases")]
        public List<MeetingContentDto> MeetingContentList {get; set;}
    }
    public class MeetingContentDto
    {
        [JsonProperty("channel")]
        public int Channel { get; set; }


        [JsonProperty("maskedITN")]
        public string MaskedITN { get; set; }

    }

    public class MeetingTokensDto
    {
        public string Source { get; set; }

        public List<MeetingChunkDto> ChunkList { get; set; }
    }
    
    public class MeetingChunkDto
    {
        public int Size { get; set; }
        public string Content { get ; set; }
    }

}