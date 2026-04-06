using System.Collections.Generic;
using System.Linq;

namespace Suterusu.Configuration
{
    public class AppConfig
    {
        public string ApiBaseUrl { get; set; }

        public string ApiKey { get; set; }

        public List<string> Models { get; set; }

        public string SystemPrompt { get; set; }

        public int HistoryLimit { get; set; }

        public NotificationMode NotificationMode { get; set; }

        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                ApiBaseUrl       = "https://api.openai.com/v1/chat/completions",
                ApiKey           = "",
                Models           = new List<string> { "gpt-4o-mini" },
                SystemPrompt     = "You are a helpful assistant.",
                HistoryLimit     = 10,
                NotificationMode = NotificationMode.FlashWindow
            };
        }

        public AppConfig Normalize()
        {
            if (string.IsNullOrWhiteSpace(ApiBaseUrl))
                ApiBaseUrl = "https://api.openai.com/v1/chat/completions";

            ApiBaseUrl = ApiBaseUrl.TrimEnd('/');

            if (Models == null)
                Models = new List<string>();

            Models = Models
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .Distinct()
                .ToList();

            if (Models.Count == 0)
                Models.Add("gpt-4o-mini");

            if (string.IsNullOrWhiteSpace(SystemPrompt))
                SystemPrompt = "You are a helpful assistant.";

            if (HistoryLimit < 0)
                HistoryLimit = 0;

            if (HistoryLimit > 100)
                HistoryLimit = 100;

            return this;
        }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(ApiBaseUrl))
                errors.Add("API Base URL is required.");

            if (Models == null || Models.Count == 0)
                errors.Add("At least one model must be specified.");

            return errors;
        }
    }
}
