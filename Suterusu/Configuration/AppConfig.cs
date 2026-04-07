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

        /// <summary>
        /// Process name (or partial name) of the window to flash, e.g. "Chrome".
        /// Use "All" to flash every visible window, or "None" / empty to disable.
        /// </summary>
        public string FlashWindowTarget { get; set; }

        /// <summary>
        /// How long (in milliseconds) to let the flash run before sending FLASHW_STOP.
        /// Mirrors the original 1600 ms hard-coded delay.
        /// </summary>
        public int FlashWindowDurationMs { get; set; }

        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                ApiBaseUrl            = "https://api.openai.com/v1/chat/completions",
                ApiKey                = "",
                Models                = new List<string> { "gpt-5.4-mini" },
                SystemPrompt          = "You are a helpful assistant.",
                HistoryLimit          = 10,
                NotificationMode      = NotificationMode.FlashWindow,
                FlashWindowTarget     = "Chrome",
                FlashWindowDurationMs = 1600
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
                Models.Add("gpt-5.4-mini");

            if (string.IsNullOrWhiteSpace(SystemPrompt))
                SystemPrompt = "You are a helpful assistant.";

            if (HistoryLimit < 0)
                HistoryLimit = 0;

            if (HistoryLimit > 100)
                HistoryLimit = 100;

            if (string.IsNullOrWhiteSpace(FlashWindowTarget))
                FlashWindowTarget = "Chrome";

            if (FlashWindowDurationMs <= 0)
                FlashWindowDurationMs = 1600;

            if (FlashWindowDurationMs > 10000)
                FlashWindowDurationMs = 10000;

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
