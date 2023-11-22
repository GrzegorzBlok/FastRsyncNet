using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastRsync.Core
{
    public class JsonSerializationSettings
    {
        static JsonSerializationSettings()
        {
            JsonSettings = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        public static JsonSerializerOptions JsonSettings { get; }
    }
}
