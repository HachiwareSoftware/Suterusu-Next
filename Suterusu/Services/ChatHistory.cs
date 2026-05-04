using System.Collections.Generic;
using System.Linq;
using Suterusu.Models;

namespace Suterusu.Services
{
    /// <summary>
    /// Maintains bounded conversation context.
    /// The system prompt is never counted towards HistoryLimit and is always prepended.
    /// </summary>
    public class ChatHistory
    {
        private readonly object _lock = new object();

        // Stores non-system turns: user + assistant messages interleaved
        private readonly List<ChatMessage> _turns = new List<ChatMessage>();

        public string SystemPrompt { get; private set; }
        public int    HistoryLimit  { get; private set; }

        public IReadOnlyList<ChatMessage> Messages
        {
            get { lock (_lock) { return _turns.ToList().AsReadOnly(); } }
        }

        public ChatHistory(string systemPrompt, int historyLimit)
        {
            SystemPrompt = systemPrompt;
            HistoryLimit = historyLimit;
        }

        public void Reset(string systemPrompt)
        {
            lock (_lock)
            {
                SystemPrompt = systemPrompt;
                _turns.Clear();
            }
        }

        public IReadOnlyList<ChatMessage> BuildRequestMessages(string userText)
        {
            lock (_lock)
            {
                var messages = new List<ChatMessage>();

                if (!string.IsNullOrEmpty(SystemPrompt))
                    messages.Add(new ChatMessage("system", SystemPrompt));

                messages.AddRange(_turns);
                messages.Add(new ChatMessage("user", userText));

                return messages.AsReadOnly();
            }
        }

        public IReadOnlyList<ChatRequestMessage> BuildVisionRequestMessages(string prompt, byte[] imageData)
        {
            lock (_lock)
            {
                var messages = new List<ChatRequestMessage>();

                if (!string.IsNullOrEmpty(SystemPrompt))
                    messages.Add(new ChatRequestMessage("system", SystemPrompt));

                foreach (var turn in _turns)
                    messages.Add(ChatRequestMessage.FromChatMessage(turn));

                string base64Image = System.Convert.ToBase64String(imageData);
                messages.Add(new ChatRequestMessage(
                    "user",
                    new object[]
                    {
                        ChatMessageContentPart.TextPart(prompt),
                        ChatMessageContentPart.ImageUrlPart("data:image/png;base64," + base64Image)
                    }));

                return messages.AsReadOnly();
            }
        }

        public void AppendSuccessfulTurn(string userText, string assistantText)
        {
            lock (_lock)
            {
                _turns.Add(new ChatMessage("user",      userText));
                _turns.Add(new ChatMessage("assistant", assistantText));
                TrimToLimit();
            }
        }

        public void UpdateConfiguration(string systemPrompt, int historyLimit)
        {
            lock (_lock)
            {
                SystemPrompt = systemPrompt;
                HistoryLimit = historyLimit;
                TrimToLimit();
            }
        }

        private void TrimToLimit()
        {
            // Each turn = user + assistant = 2 messages
            // HistoryLimit is the max number of turns (pairs)
            int maxMessages = HistoryLimit * 2;

            while (_turns.Count > maxMessages && _turns.Count >= 2)
            {
                // Remove oldest user+assistant pair
                _turns.RemoveAt(0);
                _turns.RemoveAt(0);
            }
        }
    }
}
