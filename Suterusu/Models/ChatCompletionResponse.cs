using System.Collections.Generic;

namespace Suterusu.Models
{
    public class ChatCompletionResponse
    {
        public List<Choice> Choices { get; set; }

        public ApiError Error { get; set; }
    }

    public class Choice
    {
        public ChatMessage Message { get; set; }

        public string FinishReason { get; set; }
    }

    public class ApiError
    {
        public string Message { get; set; }

        public string Type { get; set; }

        public string Code { get; set; }
    }
}
