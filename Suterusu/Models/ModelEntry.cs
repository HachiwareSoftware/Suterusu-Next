using System.Collections.Generic;

namespace Suterusu.Models
{
    public class ModelEntry
    {
        public string Name    { get; set; }
        public string BaseUrl { get; set; }
        public string ApiKey  { get; set; }
        public string Model   { get; set; }

        public ModelEntry Clone()
        {
            return new ModelEntry
            {
                Name    = Name,
                BaseUrl = BaseUrl,
                ApiKey  = ApiKey,
                Model   = Model
            };
        }

        public EndpointConfig ToEndpointConfig()
        {
            return new EndpointConfig
            {
                Name   = Name,
                BaseUrl = BaseUrl,
                ApiKey  = ApiKey,
                Models  = new List<string> { Model }
            };
        }
    }
}
