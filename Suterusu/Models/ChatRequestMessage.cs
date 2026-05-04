namespace Suterusu.Models
{
    public class ChatRequestMessage
    {
        public string Role { get; set; }

        public object Content { get; set; }

        public ChatRequestMessage() { }

        public ChatRequestMessage(string role, object content)
        {
            Role = role;
            Content = content;
        }

        public static ChatRequestMessage FromChatMessage(ChatMessage message)
            => new ChatRequestMessage(message.Role, message.Content);
    }
}
