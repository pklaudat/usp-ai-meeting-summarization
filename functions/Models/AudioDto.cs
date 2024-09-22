

namespace ETL.Models {
    
     public class AudioInputDto
    {
        public required string Name { get; set; }
        public required string DataUri { get; set; }

        public required bool IsBatch {get; set;}
    }
    public class AudioTranscriptDto
    {
        public required string InstanceId { get; set; }
        public required List<TranscriptDto> TranscriptList { get; set;}
    }

}