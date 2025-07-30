using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;

namespace FastRsync.Tests.FastRsyncLegacy;

public class NewtonsoftJsonSerializationSettings
{
    static NewtonsoftJsonSerializationSettings()
    {
        JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
        };
    }

    public static JsonSerializerSettings JsonSettings { get; }
}