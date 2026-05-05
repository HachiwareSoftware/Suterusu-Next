using System.Collections.Generic;
using Newtonsoft.Json;

namespace Suterusu.Models
{
    public class ChatCompletionRequest
    {
        public string Model { get; set; }

        public List<ChatRequestMessage> Messages { get; set; }

        public double Temperature { get; set; } = 0.7;

        public int? MaxTokens { get; set; }

        public string ReasoningEffort { get; set; }
    }
}
