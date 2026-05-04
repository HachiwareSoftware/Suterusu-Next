using System.Collections.Generic;

namespace Suterusu.Models
{
    public class EndpointConfig
    {
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public List<string> Models { get; set; }
        public ModelCapability Capability { get; set; }
    }
}
