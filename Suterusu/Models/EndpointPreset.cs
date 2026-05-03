using System.Collections.Generic;

namespace Suterusu.Models
{
    /// <summary>
    /// Pre-configured API endpoint preset for quick setup.
    /// </summary>
    public class EndpointPreset
    {
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public string DefaultModel { get; set; }
        public bool RequiresApiKey { get; set; }
        public string DefaultApiKey { get; set; }
        public string Description { get; set; }

        /// <summary>
        /// Returns all available endpoint presets in display order.
        /// </summary>
        public static List<EndpointPreset> GetPresets()
        {
            return new List<EndpointPreset>
            {
                new EndpointPreset
                {
                    Name = "OpenAI",
                    BaseUrl = "https://api.openai.com/v1/chat/completions",
                    DefaultModel = "gpt-5.4-mini",
                    RequiresApiKey = true,
                    Description = "Official OpenAI API - Fast, reliable, production-ready"
                },
                new EndpointPreset
                {
                    Name = "Anthropic",
                    BaseUrl = "https://api.anthropic.com/v1/chat/completions",
                    DefaultModel = "claude-3-5-sonnet-20241022",
                    RequiresApiKey = true,
                    Description = "Anthropic Claude models - Advanced reasoning capabilities"
                },
                new EndpointPreset
                {
                    Name = "OpenRouter",
                    BaseUrl = "https://openrouter.ai/api/v1/chat/completions",
                    DefaultModel = "openai/gpt-5.4-mini",
                    RequiresApiKey = true,
                    Description = "Unified gateway to 200+ models with automatic fallback"
                },
                new EndpointPreset
                {
                    Name = "Ollama",
                    BaseUrl = "http://localhost:11434/v1/chat/completions",
                    DefaultModel = "llama3.2",
                    RequiresApiKey = false,
                    Description = "Local Ollama instance - Private, offline, no API key needed"
                },
                new EndpointPreset
                {
                    Name = "llama.cpp",
                    BaseUrl = "http://localhost:8080/v1/chat/completions",
                    DefaultModel = "default",
                    RequiresApiKey = false,
                    Description = "Local llama.cpp server - Lightweight local inference"
                },
                new EndpointPreset
                {
                    Name = "CLIProxyAPI",
                    BaseUrl = "http://127.0.0.1:8317/v1",
                    DefaultModel = "",
                    RequiresApiKey = true,
                    Description = "Local CLIProxyAPI instance - Uses existing credentials"
                },
                new EndpointPreset
                {
                    Name = "Custom",
                    BaseUrl = "",
                    DefaultModel = "",
                    RequiresApiKey = true,
                    Description = "Configure your own endpoint manually"
                }
            };
        }

        /// <summary>
        /// Finds a preset by name (case-insensitive).
        /// </summary>
        public static EndpointPreset FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var preset in GetPresets())
            {
                if (preset.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                    return preset;
            }

            return null;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
