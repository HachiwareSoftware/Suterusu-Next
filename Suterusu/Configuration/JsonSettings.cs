using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Suterusu.Configuration
{
    public static class JsonSettings
    {
        public static JsonSerializerSettings SnakeCase { get; } = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        public static JsonSerializerSettings Compact { get; } = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        public static string Serialize(object obj) => JsonConvert.SerializeObject(obj, SnakeCase);
        public static string SerializeCompact(object obj) => JsonConvert.SerializeObject(obj, Compact);
        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, SnakeCase);
    }
}
