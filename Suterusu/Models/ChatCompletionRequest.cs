using System.Collections.Generic;
using Newtonsoft.Json;

namespace Suterusu.Models
{
    public class ChatCompletionRequest
    {
        public string Model { get; set; }

        public List<ChatMessage> Messages { get; set; }

        public double Temperature { get; set; } = 0.7;

        public int? MaxTokens { get; set; }
    }
}
