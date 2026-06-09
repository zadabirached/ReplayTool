using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReplayTool.Application;

internal static class JsonConfig
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
