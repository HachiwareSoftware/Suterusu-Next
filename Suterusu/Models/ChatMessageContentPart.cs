namespace Suterusu.Models
{
    public class ChatMessageContentPart
    {
        public string Type { get; set; }

        public string Text { get; set; }

        public ChatImageUrl ImageUrl { get; set; }

        public static ChatMessageContentPart TextPart(string text)
        {
            return new ChatMessageContentPart
            {
                Type = "text",
                Text = text ?? string.Empty
            };
        }

        public static ChatMessageContentPart ImageUrlPart(string url)
        {
            return new ChatMessageContentPart
            {
                Type = "image_url",
                ImageUrl = new ChatImageUrl { Url = url }
            };
        }
    }

    public class ChatImageUrl
    {
        public string Url { get; set; }
    }
}
