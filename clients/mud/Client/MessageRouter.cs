using System.Text;
using System.Text.Json;

namespace WodMudClient;

public sealed class MessageRouter
{
    private readonly StateStore _state;

    public MessageRouter(StateStore state)
    {
        _state = state;
    }

    public IEnumerable<string> Handle(IncomingMessage message)
    {
        if (message.Payload.ValueKind == JsonValueKind.Undefined ||
            message.Payload.ValueKind == JsonValueKind.Null)
        {
            yield return $"< {message.Type} (no payload)";
            yield break;
        }

        switch (message.Type)
        {
            case "handshake_ack":
                foreach (var line in RenderHandshakeAck(message.Payload))
                {
                    yield return line;
                }
                yield break;
            case "auth_success":
                foreach (var line in RenderAuthSuccess(message.Payload))
                {
                    yield return line;
                }
                yield break;
            case "auth_error":
                foreach (var line in RenderAuthError(message.Payload))
                {
                    yield return line;
                }
                yield break;
            case "world_entry":
                foreach (var line in RenderWorldEntry(message.Payload))
                {
                    yield return line;
                }
                yield break;
            case "state_update":
                foreach (var line in RenderStateUpdate(message.Payload))
                {
                    yield return line;
                }
                yield break;
            case "event":
                foreach (var line in RenderEvent(message.Payload))
                {
                    yield return line;
                }
                yield break;
            case "error":
                foreach (var line in RenderError(message.Payload))
                {
                    yield return line;
                }
                yield break;
            default:
                yield return $"< {message.Type} {message.Payload.GetRawText()}";
                yield break;
        }
    }

    private IEnumerable<string> RenderHandshakeAck(JsonElement payload)
    {
        var compatible = payload.GetPropertyOrDefault("compatible")?.GetBoolean() ?? false;
        var serverVersion = payload.GetPropertyOrDefault("serverVersion")?.GetString();
        var protocolVersion = payload.GetPropertyOrDefault("protocolVersion")?.GetString();
        var requiresAuth = payload.GetPropertyOrDefault("requiresAuth")?.GetBoolean() ?? false;
        yield return $"Handshake: {(compatible ? "ok" : "incompatible")} (server {serverVersion}, protocol {protocolVersion}, auth {(requiresAuth ? "required" : "optional")})";
    }

    private IEnumerable<string> RenderAuthSuccess(JsonElement payload)
    {
        var accountId = payload.GetPropertyOrDefault("accountId")?.GetString();
        yield return $"Auth: success (account {accountId})";

        if (payload.TryGetProperty("characters", out var characters) &&
            characters.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in characters.EnumerateArray())
            {
                var id = entry.GetPropertyOrDefault("id")?.GetString();
                var name = entry.GetPropertyOrDefault("name")?.GetString();
                var level = entry.GetPropertyOrDefault("level")?.GetInt32();
                var location = entry.GetPropertyOrDefault("location")?.GetString();
                yield return $"Character: {name} (id {id}, lvl {level}, {location})";
            }
        }
    }

    private IEnumerable<string> RenderAuthError(JsonElement payload)
    {
        var reason = payload.GetPropertyOrDefault("reason")?.GetString();
        var message = payload.GetPropertyOrDefault("message")?.GetString();
        yield return $"Auth: error ({reason}) {message}";
    }

    private IEnumerable<string> RenderWorldEntry(JsonElement payload)
    {
        if (payload.TryGetProperty("character", out var character))
        {
            var name = character.GetPropertyOrDefault("name")?.GetString();
            yield return $"Entering world as {name}";
        }

        if (payload.TryGetProperty("zone", out var zone))
        {
            var zoneName = zone.GetPropertyOrDefault("name")?.GetString();
            var description = zone.GetPropertyOrDefault("description")?.GetString();
            var rating = zone.GetPropertyOrDefault("contentRating")?.GetString();
            if (!string.IsNullOrWhiteSpace(zoneName))
            {
                yield return zoneName!;
            }
            if (!string.IsNullOrWhiteSpace(description))
            {
                foreach (var line in WrapText(description!, 78))
                {
                    yield return line;
                }
            }
            if (!string.IsNullOrWhiteSpace(rating))
            {
                yield return $"Content Rating: {FormatContentRating(rating!)}";
            }
        }

        if (payload.TryGetProperty("exits", out var exits) && exits.ValueKind == JsonValueKind.Array)
        {
            var exitNames = new List<string>();
            foreach (var exit in exits.EnumerateArray())
            {
                var direction = exit.GetPropertyOrDefault("direction")?.GetString();
                if (!string.IsNullOrWhiteSpace(direction))
                {
                    exitNames.Add(direction!);
                }
            }

            if (exitNames.Count > 0)
            {
                yield return $"Exits: [{string.Join("] [", exitNames)}]";
            }
        }

        if (payload.TryGetProperty("entities", out var entitiesElement) &&
            entitiesElement.ValueKind == JsonValueKind.Array)
        {
            var entities = new List<EntityInfo>();
            var lines = new List<string>();
            foreach (var entry in entitiesElement.EnumerateArray())
            {
                var info = ParseEntity(entry);
                entities.Add(info);

                var desc = entry.GetPropertyOrDefault("description")?.GetString();
                if (!string.IsNullOrWhiteSpace(info.Name))
                {
                    lines.Add($"- {info.Name} ({info.Type}){(string.IsNullOrWhiteSpace(desc) ? string.Empty : $": {desc}")}");
                }
            }

            _state.ResetEntities(entities);

            if (lines.Count > 0)
            {
                yield return "You see:";
                foreach (var line in lines)
                {
                    yield return line;
                }
            }
        }
    }

    private IEnumerable<string> RenderStateUpdate(JsonElement payload)
    {
        if (payload.TryGetProperty("entities", out var entitiesElement))
        {
            if (entitiesElement.TryGetProperty("added", out var added) &&
                added.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in added.EnumerateArray())
                {
                    var info = ParseEntity(entry);
                    _state.AddOrUpdateEntity(info);
                    yield return $"Arrives: {info.Name} ({info.Type})";
                }
            }

            if (entitiesElement.TryGetProperty("updated", out var updated) &&
                updated.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in updated.EnumerateArray())
                {
                    var info = ParseEntity(entry);
                    _state.AddOrUpdateEntity(info);
                    if (!string.IsNullOrWhiteSpace(info.Name))
                    {
                        yield return $"Moves: {info.Name}";
                    }
                }
            }

            if (entitiesElement.TryGetProperty("removed", out var removed) &&
                removed.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in removed.EnumerateArray())
                {
                    var id = entry.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        _state.RemoveEntity(id!);
                        yield return $"Leaves: {id}";
                    }
                }
            }
        }

        if (payload.TryGetProperty("zone", out var zone))
        {
            var time = zone.GetPropertyOrDefault("timeOfDay")?.GetString();
            var weather = zone.GetPropertyOrDefault("weather")?.GetString();
            var rating = zone.GetPropertyOrDefault("contentRating")?.GetString();
            if (!string.IsNullOrWhiteSpace(time) || !string.IsNullOrWhiteSpace(weather))
            {
                yield return $"Zone: {(string.IsNullOrWhiteSpace(time) ? "time ?" : time)} {(string.IsNullOrWhiteSpace(weather) ? string.Empty : $"({weather})")}".Trim();
            }
            if (!string.IsNullOrWhiteSpace(rating))
            {
                yield return $"Content Rating: {FormatContentRating(rating!)}";
            }
        }
    }

    private IEnumerable<string> RenderEvent(JsonElement payload)
    {
        var narrative = payload.GetPropertyOrDefault("narrative")?.GetString();
        if (!string.IsNullOrWhiteSpace(narrative))
        {
            yield return narrative!;
            yield break;
        }

        var eventType = payload.GetPropertyOrDefault("eventType")?.GetString();
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            yield return $"Event: {eventType}";
        }
        else
        {
            yield return $"Event: {payload.GetRawText()}";
        }
    }

    private IEnumerable<string> RenderError(JsonElement payload)
    {
        var code = payload.GetPropertyOrDefault("code")?.GetString();
        var message = payload.GetPropertyOrDefault("message")?.GetString();
        yield return $"Error: {code} {message}".Trim();
    }

    private static EntityInfo ParseEntity(JsonElement entry)
    {
        return new EntityInfo
        {
            Id = entry.GetPropertyOrDefault("id")?.GetString() ?? string.Empty,
            Name = entry.GetPropertyOrDefault("name")?.GetString() ?? string.Empty,
            Type = entry.GetPropertyOrDefault("type")?.GetString() ?? "entity"
        };
    }

    private static IEnumerable<string> WrapText(string text, int width)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (line.Length + word.Length + 1 > width)
            {
                if (line.Length > 0)
                {
                    yield return line.ToString().TrimEnd();
                    line.Clear();
                }
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString().TrimEnd();
        }
    }

    private static string FormatContentRating(string rating)
    {
        return rating.ToUpperInvariant() switch
        {
            "T" => "Teen (13+) [T]",
            "M" => "Mature (17+) [M]",
            "AO" => "Adults Only (18+) [AO]",
            _ => $"{rating} [Unknown]"
        };
    }
}

internal static class JsonElementExtensions
{
    public static JsonElement? GetPropertyOrDefault(this JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
        {
            return value;
        }

        return null;
    }
}
