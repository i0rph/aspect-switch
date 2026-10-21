#nullable enable
using System.Text.Json;
namespace System.Web.Script.Serialization;
// API adapter for the shared, unchanged .NET Framework settings model.
public sealed class JavaScriptSerializer
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    public string Serialize(object value) => JsonSerializer.Serialize(value, Options);
    public T Deserialize<T>(string json)
    {
        if (typeof(T) == typeof(Dictionary<string, object>))
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Null) return default!;
            var fields = new Dictionary<string, object>();
            foreach (var field in doc.RootElement.EnumerateObject()) fields[field.Name] = field.Value.ValueKind switch {
                JsonValueKind.String => field.Value.GetString()!, JsonValueKind.Number => field.Value.GetInt32(),
                JsonValueKind.True => true, JsonValueKind.False => false, _ => field.Value.Clone()
            };
            return (T)(object)fields;
        }
        return JsonSerializer.Deserialize<T>(json, Options)!;
    }
}
