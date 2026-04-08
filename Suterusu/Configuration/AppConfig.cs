using System.Collections.Generic;
using System.Linq;
using Suterusu.Models;

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

        public MultiRequestMode MultiRequestMode { get; set; }

        public int MultiRequestTimeoutMs { get; set; }

        public List<EndpointConfig> Endpoints { get; set; }

        public int RoundRobinIndex { get; set; }

        /// <summary>
        /// Flat ordered list of (endpoint, model) pairs used by all multi-request modes.
        /// Models are tried top-to-bottom in Sequential mode, rotated in RoundRobin, raced in Fastest.
        /// Replaces the old Endpoints list as the primary configuration.
        /// </summary>
        public List<ModelEntry> ModelPriority { get; set; }

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

        /// <summary>
        /// Total time (in milliseconds) the Circle Dot overlay is visible and pulsing.
        /// The sine-wave animation always completes exactly two full cycles in this period.
        /// </summary>
        public int CircleDotPulseMs { get; set; }

        public int CircleDotBlinkCount { get; set; }

        public int CircleDotBlinkDurationMs { get; set; }

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
                MultiRequestMode      = MultiRequestMode.RoundRobin,
                MultiRequestTimeoutMs = 60000,
                Endpoints             = new List<EndpointConfig>(),
                RoundRobinIndex       = 0,
                ModelPriority         = new List<ModelEntry>(),
                FlashWindowTarget     = "Chrome",
                FlashWindowDurationMs = 1600,
                CircleDotPulseMs      = 800,
                CircleDotBlinkCount   = 3,
                CircleDotBlinkDurationMs = 600
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

            if (CircleDotPulseMs < 200)
                CircleDotPulseMs = 200;

            if (CircleDotPulseMs > 5000)
                CircleDotPulseMs = 5000;

            if (MultiRequestTimeoutMs <= 0)
                MultiRequestTimeoutMs = 60000;

            if (MultiRequestTimeoutMs > 120000)
                MultiRequestTimeoutMs = 120000;

            if (CircleDotBlinkCount < 1)
                CircleDotBlinkCount = 3;

            if (CircleDotBlinkCount > 10)
                CircleDotBlinkCount = 10;

            if (CircleDotBlinkDurationMs < 200)
                CircleDotBlinkDurationMs = 200;

            if (CircleDotBlinkDurationMs > 5000)
                CircleDotBlinkDurationMs = 5000;

            if (RoundRobinIndex < 0)
                RoundRobinIndex = 0;

            // Ensure ModelPriority is initialised
            if (ModelPriority == null)
                ModelPriority = new List<ModelEntry>();

            // Migrate from old Endpoints list
            if (ModelPriority.Count == 0 && Endpoints != null && Endpoints.Count > 0)
            {
                foreach (var ep in Endpoints)
                {
                    foreach (var model in ep.Models ?? new List<string>())
                    {
                        ModelPriority.Add(new ModelEntry
                        {
                            Name    = ep.Name ?? "Endpoint",
                            BaseUrl = ep.BaseUrl,
                            ApiKey  = ep.ApiKey ?? string.Empty,
                            Model   = model
                        });
                    }
                }
            }

            // Migrate from old single-endpoint fields
            if (ModelPriority.Count == 0 && !string.IsNullOrWhiteSpace(ApiBaseUrl) && Models.Count > 0)
            {
                foreach (var model in Models)
                {
                    ModelPriority.Add(new ModelEntry
                    {
                        Name    = "Default",
                        BaseUrl = ApiBaseUrl,
                        ApiKey  = ApiKey ?? string.Empty,
                        Model   = model
                    });
                }
            }

            // Remove entries that have no URL or model
            ModelPriority = ModelPriority
                .Where(e => !string.IsNullOrWhiteSpace(e.BaseUrl) && !string.IsNullOrWhiteSpace(e.Model))
                .ToList();

            return this;
        }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (ModelPriority == null || ModelPriority.Count == 0)
            {
                errors.Add("At least one entry must be added to the Model Priority list.");
            }
            else
            {
                for (int i = 0; i < ModelPriority.Count; i++)
                {
                    var entry = ModelPriority[i];
                    if (string.IsNullOrWhiteSpace(entry.BaseUrl))
                        errors.Add($"Entry {i + 1} ({entry.Name}) has no Base URL.");
                    if (string.IsNullOrWhiteSpace(entry.Model))
                        errors.Add($"Entry {i + 1} ({entry.Name}) has no model specified.");
                }
            }

            return errors;
        }
    }
}
