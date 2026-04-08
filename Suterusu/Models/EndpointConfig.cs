using System.Collections.Generic;
using System.Linq;

namespace Suterusu.Models
{
    public class EndpointConfig
    {
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public List<string> Models { get; set; }

        public static EndpointConfig CreateDefault()
        {
            return new EndpointConfig
            {
                Name = "OpenRouter",
                BaseUrl = "https://openrouter.ai/api/v1/chat/completions",
                ApiKey = "",
                Models = new List<string> { "openai/gpt-5.4-mini" }
            };
        }

        public EndpointConfig Normalize()
        {
            if (string.IsNullOrWhiteSpace(Name))
                Name = "Custom";

            if (string.IsNullOrWhiteSpace(BaseUrl))
                BaseUrl = "https://api.openai.com/v1/chat/completions";

            BaseUrl = BaseUrl.TrimEnd('/');

            if (Models == null)
                Models = new List<string>();

            Models = Models
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .Distinct()
                .ToList();

            if (Models.Count == 0)
                Models.Add("gpt-5.4-mini");

            return this;
        }

        public EndpointConfig Clone()
        {
            return new EndpointConfig
            {
                Name = Name,
                BaseUrl = BaseUrl,
                ApiKey = ApiKey,
                Models = new List<string>(Models)
            };
        }
    }
}
