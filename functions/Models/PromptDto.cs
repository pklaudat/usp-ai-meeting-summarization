

using Newtonsoft.Json;

namespace ETL.Models {

    public class PromptDto
    {
        [JsonProperty("role")]
        public string Role {get; set;}

        [JsonProperty("content")]
        public string Content {get; set;}
    }
    
    public class PromptRequestDto
    {
        [JsonProperty("model")]
        public string Model;

        [JsonProperty("messages")]
        public List<PromptDto> Messages;

    }


    public class ChoicesDto
    {
        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("message")]
        public PromptDto Message { get; set; }
    }

    public class ModelUsageDto
    {
        [JsonProperty("prompt_tokens")]
        public int PromptTokens { get; set;}

        [JsonProperty("completion_tokens")]
        public int CompletionsTokens { get; set; }

        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }
    }

    public class PromptResponseDto
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("choices")]
        public List<ChoicesDto> Choices { get; set; }

        [JsonProperty("usage")]
        public ModelUsageDto Usage { get; set; }
    }

}