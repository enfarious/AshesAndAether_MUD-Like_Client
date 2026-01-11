using System;
using System.Globalization;
using System.Linq;
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

    public IEnumerable<string> Handle(IncomingMessage message, bool includeDiagnostics)
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
                if (includeDiagnostics)
                {
                    foreach (var line in RenderHandshakeAck(message.Payload))
                    {
                        yield return line;
                    }
                }
                yield break;
            case "auth_success":
                if (includeDiagnostics)
                {
                    foreach (var line in RenderAuthSuccess(message.Payload))
                    {
                        yield return line;
                    }
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
                    if (includeDiagnostics)
                    {
                        yield return line;
                    }
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
            case "communication":
            case "chat":
                foreach (var line in RenderCommunication(message.Payload))
                {
                    yield return line;
                }
                yield break;
            case "command_response":
                foreach (var line in RenderCommandResponse(message.Payload))
                {
                    yield return line;
                }
                yield break;
            case "proximity_roster":
                ApplyProximityRoster(message.Payload);
                yield break;
            case "proximity_roster_delta":
                ApplyProximityRosterDelta(message.Payload);
                yield break;
            default:
                if (includeDiagnostics)
                {
                    yield return $"< {message.Type} {message.Payload.GetRawText()}";
                }
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
            UpdatePlayerPositionFromCharacter(character);
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

        foreach (var line in ApplyMovementState(payload, logAvailableDirections: true))
        {
            yield return line;
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

        if (payload.TryGetProperty("character", out var movementCharacter))
        {
            UpdatePlayerPositionFromCharacter(movementCharacter);
        }

        foreach (var line in ApplyMovementState(payload, logAvailableDirections: false))
        {
            yield return line;
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

    private IEnumerable<string> RenderCommunication(JsonElement payload)
    {
        var type = payload.GetPropertyOrDefault("channel")?.GetString()
            ?? payload.GetPropertyOrDefault("type")?.GetString()
            ?? "say";
        var senderName = payload.GetPropertyOrDefault("sender")?.GetString()
            ?? payload.GetPropertyOrDefault("senderName")?.GetString()
            ?? "Unknown";
        var content = payload.GetPropertyOrDefault("message")?.GetString()
            ?? payload.GetPropertyOrDefault("content")?.GetString()
            ?? string.Empty;

        if (string.Equals(type, "emote", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(senderName) &&
                content.StartsWith(senderName + " ", StringComparison.OrdinalIgnoreCase))
            {
                yield return content.TrimEnd();
                yield break;
            }
            yield return $"{senderName} {content}".TrimEnd();
            yield break;
        }

        if (string.Equals(type, "cfh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "callforhelp", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{senderName} calls for help! ({content})".TrimEnd();
            yield break;
        }

        var verb = string.Equals(type, "shout", StringComparison.OrdinalIgnoreCase) ? "shouts" : "says";
        yield return $"{senderName} {verb}, {content}".TrimEnd();
    }

    private IEnumerable<string> RenderCommandResponse(JsonElement payload)
    {
        var success = payload.GetPropertyOrDefault("success")?.GetBoolean();
        var command = payload.GetPropertyOrDefault("command")?.GetString();
        var message = payload.GetPropertyOrDefault("message")?.GetString();

        if (!string.IsNullOrWhiteSpace(message))
        {
            yield return message!;
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(command))
        {
            var status = success.HasValue && success.Value ? "ok" : "failed";
            yield return $"Command {status}: {command}";
        }
    }

    private static EntityInfo ParseEntity(JsonElement entry)
    {
        var info = new EntityInfo
        {
            Id = entry.GetPropertyOrDefault("id")?.GetString() ?? string.Empty,
            Name = entry.GetPropertyOrDefault("name")?.GetString() ?? string.Empty,
            Type = entry.GetPropertyOrDefault("type")?.GetString() ?? "entity",
            Description = entry.GetPropertyOrDefault("description")?.GetString()
        };

        if (TryParsePosition(entry.GetPropertyOrDefault("position"), out var position))
        {
            info.Position = position;
        }

        if (TryGetDouble(entry, "bearing", out var bearing))
        {
            info.Bearing = bearing;
        }

        if (TryGetDouble(entry, "elevation", out var elevation))
        {
            info.Elevation = elevation;
        }

        if (TryGetDouble(entry, "range", out var range) || TryGetDouble(entry, "distance", out range))
        {
            info.Range = range;
        }

        return info;
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

    private IEnumerable<string> ApplyMovementState(JsonElement payload, bool logAvailableDirections)
    {
        if (payload.TryGetProperty("character", out var character))
        {
            var heading = GetNumber(character.GetPropertyOrDefault("heading"));
            var speed = character.GetPropertyOrDefault("currentSpeed")?.GetString();
            _state.UpdateMovementState(heading, speed, null);
        }

        if (TryGetTextMovement(payload, out var textMovement))
        {
            var directions = ParseAvailableDirections(textMovement);
            var heading = GetNumber(textMovement.GetPropertyOrDefault("currentHeading"));
            var speed = textMovement.GetPropertyOrDefault("currentSpeed")?.GetString();
            _state.UpdateMovementState(heading, speed, directions);

            if (logAvailableDirections && directions.Count > 0)
            {
                yield return FormatDirections(directions);
            }
        }
    }

    private void ApplyProximityRoster(JsonElement payload)
    {
        if (payload.TryGetProperty("dangerState", out var danger))
        {
            _state.ProximityRoster.UpdateDangerState(danger.GetBoolean());
        }

        if (!payload.TryGetProperty("channels", out var channels) ||
            channels.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var channel in channels.EnumerateObject())
        {
            var delta = new ProximityChannelDelta();
            var channelPayload = channel.Value;
            if (channelPayload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (channelPayload.TryGetProperty("entities", out var entitiesElement) &&
                entitiesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entitiesElement.EnumerateArray())
                {
                    if (TryParseProximityEntity(entry, out var entity))
                    {
                        delta.Added.Add(entity);
                    }
                }
            }

            if (channelPayload.TryGetProperty("count", out var count))
            {
                if (count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out var countValue))
                {
                    delta.Count = countValue;
                }
            }

            if (channelPayload.TryGetProperty("sample", out var sample) &&
                sample.ValueKind == JsonValueKind.Array)
            {
                delta.Sample = sample.EnumerateArray()
                    .Select(entry => entry.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .ToList();
            }

            if (channelPayload.TryGetProperty("lastSpeaker", out var lastSpeaker))
            {
                delta.LastSpeaker = lastSpeaker.GetString();
                delta.LastSpeakerChanged = true;
            }

            _state.ProximityRoster.ApplyDelta(channel.Name, delta);
        }
    }

    private void ApplyProximityRosterDelta(JsonElement payload)
    {
        if (payload.TryGetProperty("dangerState", out var danger))
        {
            _state.ProximityRoster.UpdateDangerState(danger.GetBoolean());
        }

        if (!payload.TryGetProperty("channels", out var channels) ||
            channels.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var channel in channels.EnumerateObject())
        {
            var delta = new ProximityChannelDelta();
            var channelPayload = channel.Value;
            if (channelPayload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (channelPayload.TryGetProperty("added", out var added) &&
                added.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in added.EnumerateArray())
                {
                    if (TryParseProximityEntity(entry, out var entity))
                    {
                        delta.Added.Add(entity);
                    }
                }
            }

            if (channelPayload.TryGetProperty("removed", out var removed) &&
                removed.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in removed.EnumerateArray())
                {
                    var id = entry.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        delta.Removed.Add(id!);
                    }
                }
            }

            if (channelPayload.TryGetProperty("updated", out var updated) &&
                updated.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in updated.EnumerateArray())
                {
                    if (TryParseProximityEntityDelta(entry, out var entity))
                    {
                        delta.Updated.Add(entity);
                    }
                }
            }

            if (channelPayload.TryGetProperty("count", out var count))
            {
                if (count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out var countValue))
                {
                    delta.Count = countValue;
                }
            }

            if (channelPayload.TryGetProperty("sample", out var sample))
            {
                if (sample.ValueKind == JsonValueKind.Null)
                {
                    delta.Sample = null;
                }
                else if (sample.ValueKind == JsonValueKind.Array)
                {
                    delta.Sample = sample.EnumerateArray()
                        .Select(entry => entry.GetString())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!)
                        .ToList();
                }
            }

            if (channelPayload.TryGetProperty("lastSpeaker", out var lastSpeaker))
            {
                delta.LastSpeakerChanged = true;
                delta.LastSpeaker = lastSpeaker.ValueKind == JsonValueKind.Null ? null : lastSpeaker.GetString();
            }

            _state.ProximityRoster.ApplyDelta(channel.Name, delta);
        }
    }

    private static bool TryParseProximityEntity(JsonElement entry, out ProximityEntity entity)
    {
        entity = new ProximityEntity
        {
            Id = entry.GetPropertyOrDefault("id")?.GetString() ?? string.Empty,
            Name = entry.GetPropertyOrDefault("name")?.GetString() ?? string.Empty,
            Type = entry.GetPropertyOrDefault("type")?.GetString() ?? "entity"
        };

        if (!TryGetDouble(entry, "bearing", out var bearing) ||
            !TryGetDouble(entry, "elevation", out var elevation) ||
            !TryGetDouble(entry, "range", out var range))
        {
            return false;
        }

        entity.Bearing = bearing;
        entity.Elevation = elevation;
        entity.Range = range;
        return true;
    }

    private static bool TryParseProximityEntityDelta(JsonElement entry, out ProximityEntityDelta entity)
    {
        entity = new ProximityEntityDelta
        {
            Id = entry.GetPropertyOrDefault("id")?.GetString() ?? string.Empty,
            Name = entry.GetPropertyOrDefault("name")?.GetString(),
            Type = entry.GetPropertyOrDefault("type")?.GetString()
        };

        if (string.IsNullOrWhiteSpace(entity.Id))
        {
            return false;
        }

        if (TryGetDouble(entry, "bearing", out var bearing))
        {
            entity.Bearing = bearing;
        }
        if (TryGetDouble(entry, "elevation", out var elevation))
        {
            entity.Elevation = elevation;
        }
        if (TryGetDouble(entry, "range", out var range))
        {
            entity.Range = range;
        }

        return true;
    }

    private static List<string> ParseAvailableDirections(JsonElement textMovement)
    {
        var directions = new List<string>();
        if (textMovement.TryGetProperty("availableDirections", out var directionElement) &&
            directionElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in directionElement.EnumerateArray())
            {
                var direction = entry.GetString();
                if (!string.IsNullOrWhiteSpace(direction))
                {
                    directions.Add(direction!);
                }
            }
        }

        return directions;
    }

    private static string FormatDirections(IReadOnlyList<string> directions)
    {
        return $"Available directions: [{string.Join("] [", directions)}]";
    }

    private static bool TryGetTextMovement(JsonElement payload, out JsonElement textMovement)
    {
        if (payload.TryGetProperty("textMovement", out textMovement))
        {
            return true;
        }

        if (payload.TryGetProperty("character", out var character) &&
            character.ValueKind == JsonValueKind.Object &&
            character.TryGetProperty("textMovement", out textMovement))
        {
            return true;
        }

        textMovement = default;
        return false;
    }

    private static double? GetNumber(JsonElement? element)
    {
        if (!element.HasValue)
        {
            return null;
        }

        return element.Value.ValueKind switch
        {
            JsonValueKind.Number => element.Value.GetDouble(),
            JsonValueKind.String => double.TryParse(element.Value.GetString(), out var value) ? value : null,
            _ => null
        };
    }

    private void UpdatePlayerPositionFromCharacter(JsonElement character)
    {
        if (TryParsePosition(character.GetPropertyOrDefault("position"), out var position))
        {
            _state.UpdatePlayerPosition(position);
        }
    }

    private static bool TryParsePosition(JsonElement? element, out Vector3 position)
    {
        position = default;
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryGetDouble(element.Value, "x", out var x) ||
            !TryGetDouble(element.Value, "y", out var y) ||
            !TryGetDouble(element.Value, "z", out var z))
        {
            return false;
        }

        position = new Vector3(x, y, z);
        return true;
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var prop))
        {
            return false;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false
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
