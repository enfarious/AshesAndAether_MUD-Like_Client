using System.Text.Json;
using System.Text.Json.Serialization;

namespace AshesAndAether_Client;

public sealed class OutgoingMessage
{
    [JsonPropertyName("type")]
    public string Type { get; }
    [JsonPropertyName("payload")]
    public object Payload { get; }
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; }

    public OutgoingMessage(string type, object payload, long timestamp)
    {
        Type = type;
        Payload = payload;
        Timestamp = timestamp;
    }
}

public sealed class IncomingMessage
{
    public string Type { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
    public long? Timestamp { get; init; }
    public long? Sequence { get; init; }

    public static bool TryParse(string json, out IncomingMessage? message)
    {
        message = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp))
            {
                return false;
            }

            var payload = root.TryGetProperty("payload", out var payloadProp)
                ? payloadProp.Clone()
                : default;

            message = new IncomingMessage
            {
                Type = typeProp.GetString() ?? string.Empty,
                Payload = payload,
                Timestamp = root.TryGetProperty("timestamp", out var ts) ? ts.GetInt64() : null,
                Sequence = root.TryGetProperty("sequence", out var seq) ? seq.GetInt64() : null
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
