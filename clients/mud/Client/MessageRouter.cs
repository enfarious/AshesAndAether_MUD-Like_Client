using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AshesAndAether_Client;

public sealed class MessageRouter
{
    private readonly StateStore _state;
    private CombatDisplayConfig _combatDisplay;

    public MessageRouter(StateStore state, CombatDisplayConfig combatDisplay)
    {
        _state = state;
        _combatDisplay = combatDisplay ?? new CombatDisplayConfig();
    }

    public void UpdateCombatDisplay(CombatDisplayConfig? combatDisplay)
    {
        _combatDisplay = combatDisplay ?? new CombatDisplayConfig();
    }

    public IEnumerable<LogLine> Handle(IncomingMessage message, bool includeDiagnostics, bool showDevNotices)
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
            case "dev_ack":
                if (showDevNotices)
                {
                    foreach (var line in RenderDevAck(message.Payload))
                    {
                        yield return line;
                    }
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

    private IEnumerable<LogLine> RenderHandshakeAck(JsonElement payload)
    {
        var compatible = payload.GetPropertyOrDefault("compatible")?.GetBoolean() ?? false;
        var serverVersion = payload.GetPropertyOrDefault("serverVersion")?.GetString();
        var protocolVersion = payload.GetPropertyOrDefault("protocolVersion")?.GetString();
        var requiresAuth = payload.GetPropertyOrDefault("requiresAuth")?.GetBoolean() ?? false;
        yield return $"Handshake: {(compatible ? "ok" : "incompatible")} (server {serverVersion}, protocol {protocolVersion}, auth {(requiresAuth ? "required" : "optional")})";
    }

    private IEnumerable<LogLine> RenderAuthSuccess(JsonElement payload)
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

    private IEnumerable<LogLine> RenderAuthError(JsonElement payload)
    {
        var reason = payload.GetPropertyOrDefault("reason")?.GetString();
        var message = payload.GetPropertyOrDefault("message")?.GetString();
        yield return $"Auth: error ({reason}) {message}";
    }

    private IEnumerable<LogLine> RenderWorldEntry(JsonElement payload)
    {
        if (payload.TryGetProperty("character", out var character))
        {
            var name = character.GetPropertyOrDefault("name")?.GetString();
            yield return $"Entering world as {name}";
            UpdatePlayerPositionFromCharacter(character);
            UpdateVitalsFromCharacter(character);
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

    private IEnumerable<LogLine> RenderStateUpdate(JsonElement payload)
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
            UpdateVitalsFromCharacter(movementCharacter);
        }

        if (payload.TryGetProperty("allies", out var alliesElement) &&
            alliesElement.ValueKind == JsonValueKind.Array)
        {
            UpdatePartyAllies(alliesElement);
        }

        ApplyCombatState(payload);

        foreach (var line in ApplyMovementState(payload, logAvailableDirections: false))
        {
            yield return line;
        }
    }

    private IEnumerable<LogLine> RenderEvent(JsonElement payload)
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
            if (eventType.StartsWith("party_", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in RenderPartyEvent(eventType!, payload))
                {
                    yield return line;
                }
                yield break;
            }

            if (eventType.StartsWith("combat_", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in RenderCombatEvent(eventType!, payload))
                {
                    yield return line;
                }
                yield break;
            }

            if (string.Equals(eventType, "combat_start", StringComparison.OrdinalIgnoreCase))
            {
                _state.UpdateCombatState(null, null, false, null, null, false, true, true, null, false);
            }
            else if (string.Equals(eventType, "combat_end", StringComparison.OrdinalIgnoreCase))
            {
                _state.ClearCombatState();
            }
            yield return $"Event: {eventType}";
        }
        else
        {
            yield return $"Event: {payload.GetRawText()}";
        }
    }

    private IEnumerable<LogLine> RenderCombatEvent(string eventType, JsonElement payload)
    {
        var attackerId = payload.GetPropertyOrDefault("attackerId")?.GetString();
        var targetId = payload.GetPropertyOrDefault("targetId")?.GetString();
        var abilityName = payload.GetPropertyOrDefault("abilityName")?.GetString()
            ?? payload.GetPropertyOrDefault("abilityId")?.GetString();
        var outcome = payload.GetPropertyOrDefault("outcome")?.GetString();
        var amount = payload.GetPropertyOrDefault("amount")?.GetInt32();
        var floatText = FormatFloatText(payload, "floatText");
        var floatTextTarget = FormatFloatText(payload, "floatTextTarget");
        var floatTextColorKey = GetFloatTextColorKey(payload, "floatText");
        var floatTextTargetColorKey = GetFloatTextColorKey(payload, "floatTextTarget");
        var floatTextShake = GetFloatTextShake(payload, "floatText");
        var floatTextTargetShake = GetFloatTextShake(payload, "floatTextTarget");
        var outcomeFlags = GetOutcomeFlags(outcome, payload);
        var style = ResolveCombatStyle();

        var attackerName = ResolveEntityDisplayName(attackerId);
        var targetName = ResolveEntityDisplayName(targetId);
        var isSelf = !string.IsNullOrWhiteSpace(attackerId) &&
            string.Equals(attackerId, _state.PlayerId, StringComparison.OrdinalIgnoreCase);
        if (isSelf)
        {
            attackerName = "You";
        }
        var isSelfTarget = !string.IsNullOrWhiteSpace(targetId) &&
            string.Equals(targetId, _state.PlayerId, StringComparison.OrdinalIgnoreCase);
        var colorKey = ResolveCombatColorKey(eventType, isSelf, isSelfTarget, floatTextColorKey, floatTextTargetColorKey);
        var shake = ResolveCombatShake(eventType, isSelf, isSelfTarget, floatTextShake, floatTextTargetShake);

        if (string.Equals(eventType, "combat_action", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(abilityName) && !string.IsNullOrWhiteSpace(targetName))
            {
                yield return $"{attackerName} use{(attackerName == "You" ? string.Empty : "s")} {abilityName} on {targetName}.";
                yield break;
            }
        }

        if (string.Equals(eventType, "combat_miss", StringComparison.OrdinalIgnoreCase))
        {
            var tagPrefix = style == CombatDisplayStyle.Tagged ? BuildOutcomeTagPrefix(eventType, outcomeFlags) : string.Empty;
            if (style == CombatDisplayStyle.Split)
            {
                yield return new LogLine($"{attackerName} miss{(attackerName == "You" ? string.Empty : "es")} {targetName}.", colorKey, shake);
                foreach (var line in BuildFxLines(floatText, floatTextTarget, isSelfTarget))
                {
                    yield return line;
                }
                yield break;
            }

            yield return new LogLine($"{tagPrefix}{attackerName} miss{(attackerName == "You" ? string.Empty : "es")} {targetName}.", colorKey, shake);
            yield break;
        }

        if (string.Equals(eventType, "combat_hit", StringComparison.OrdinalIgnoreCase))
        {
            var tagPrefix = style == CombatDisplayStyle.Tagged ? BuildOutcomeTagPrefix(eventType, outcomeFlags) : string.Empty;
            var outcomeSummary = style == CombatDisplayStyle.Tagged ? string.Empty : BuildOutcomeSummary(outcomeFlags);
            var amountText = amount.HasValue ? $" for {amount.Value}" : string.Empty;
            if (string.IsNullOrWhiteSpace(attackerId) && string.IsNullOrWhiteSpace(targetId))
            {
                var baseText = amount.HasValue ? $"Hit{amountText}" : "Hit";
                if (style == CombatDisplayStyle.Split)
                {
                    yield return new LogLine($"{baseText}{outcomeSummary}.", colorKey, shake);
                    foreach (var line in BuildFxLines(floatText, floatTextTarget, isSelfTarget))
                    {
                        yield return line;
                    }
                    yield break;
                }
                yield return new LogLine($"{tagPrefix}{baseText}{outcomeSummary}.", colorKey, shake);
                yield break;
            }
            if (style == CombatDisplayStyle.Split)
            {
                yield return new LogLine($"{attackerName} hit{(attackerName == "You" ? string.Empty : "s")} {targetName}{amountText}{outcomeSummary}.", colorKey, shake);
                foreach (var line in BuildFxLines(floatText, floatTextTarget, isSelfTarget))
                {
                    yield return line;
                }
                yield break;
            }
            yield return new LogLine($"{tagPrefix}{attackerName} hit{(attackerName == "You" ? string.Empty : "s")} {targetName}{amountText}{outcomeSummary}.", colorKey, shake);
            yield break;
        }

        if (string.Equals(eventType, "combat_death", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(targetName))
            {
                yield return $"{targetName} falls.";
                yield break;
            }
        }

        if (string.Equals(eventType, "combat_end", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Combat ends.";
            yield break;
        }

        yield return $"Event: {eventType}";
    }

    private string ResolveEntityDisplayName(string? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return "Unknown";
        }

        if (_state.TryGetEntityName(entityId, out var name))
        {
            return name;
        }

        return entityId;
    }

    private static CombatOutcomeFlags GetOutcomeFlags(string? outcome, JsonElement payload)
    {
        var critical = payload.GetPropertyOrDefault("critical")?.GetBoolean() == true;
        var glancing = payload.GetPropertyOrDefault("glancing")?.GetBoolean() == true;
        var deflected = payload.GetPropertyOrDefault("deflected")?.GetBoolean() == true;
        var penetrating = payload.GetPropertyOrDefault("penetrating")?.GetBoolean() == true;

        if (!critical && !glancing && !deflected && !penetrating && !string.IsNullOrWhiteSpace(outcome))
        {
            var normalized = outcome.ToLowerInvariant();
            switch (normalized)
            {
                case "crit":
                    critical = true;
                    break;
                case "glance":
                case "glancing":
                    glancing = true;
                    break;
                case "deflected":
                    deflected = true;
                    break;
                case "penetrating":
                    penetrating = true;
                    break;
            }
        }

        return new CombatOutcomeFlags(critical, glancing, deflected, penetrating);
    }

    private static string BuildOutcomeTagPrefix(string eventType, CombatOutcomeFlags outcomeFlags)
    {
        var tags = new List<string>();
        if (string.Equals(eventType, "combat_miss", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("MISS");
        }
        if (outcomeFlags.Critical)
        {
            tags.Add("CRIT");
        }
        if (outcomeFlags.Penetrating)
        {
            tags.Add("PEN");
        }
        if (outcomeFlags.Deflected)
        {
            tags.Add("DEFLECT");
        }
        if (outcomeFlags.Glancing)
        {
            tags.Add("GLANCE");
        }

        return tags.Count > 0 ? $"[{string.Join("][", tags)}] " : string.Empty;
    }

    private IEnumerable<LogLine> BuildFxLines(string? floatText, string? floatTextTarget, bool isSelfTarget)
    {
        if (!_combatDisplay.ShowFx)
        {
            yield break;
        }

        if (_combatDisplay.ShowFx && !string.IsNullOrWhiteSpace(floatText))
        {
            yield return $"  FX: {floatText}";
        }

        if (_combatDisplay.ShowImpactFx && isSelfTarget && !string.IsNullOrWhiteSpace(floatTextTarget))
        {
            yield return $"  Impact: {floatTextTarget}";
        }
    }

    private static string BuildOutcomeSummary(CombatOutcomeFlags outcomeFlags)
    {
        var tags = new List<string>();
        if (outcomeFlags.Critical)
        {
            tags.Add("critical");
        }
        if (outcomeFlags.Penetrating)
        {
            tags.Add("penetrating");
        }
        if (outcomeFlags.Glancing)
        {
            tags.Add("glancing");
        }
        if (outcomeFlags.Deflected)
        {
            tags.Add("deflected");
        }

        return tags.Count > 0 ? $" [{string.Join(" | ", tags)}]" : string.Empty;
    }

    private static string? ResolveCombatColorKey(
        string eventType,
        bool isSelf,
        bool isSelfTarget,
        string? floatTextColorKey,
        string? floatTextTargetColorKey)
    {
        if (string.Equals(eventType, "combat_miss", StringComparison.OrdinalIgnoreCase))
        {
            return floatTextColorKey;
        }

        if (string.Equals(eventType, "combat_hit", StringComparison.OrdinalIgnoreCase))
        {
            if (isSelfTarget && !string.IsNullOrWhiteSpace(floatTextTargetColorKey))
            {
                return floatTextTargetColorKey;
            }

            if (isSelf && !string.IsNullOrWhiteSpace(floatTextColorKey))
            {
                return floatTextColorKey;
            }

            return floatTextColorKey ?? floatTextTargetColorKey;
        }

        if (string.Equals(eventType, "combat_death", StringComparison.OrdinalIgnoreCase))
        {
            return floatTextTargetColorKey ?? floatTextColorKey;
        }

        return null;
    }

    private static double? ResolveCombatShake(
        string eventType,
        bool isSelf,
        bool isSelfTarget,
        double? floatTextShake,
        double? floatTextTargetShake)
    {
        if (string.Equals(eventType, "combat_miss", StringComparison.OrdinalIgnoreCase))
        {
            return floatTextShake;
        }

        if (string.Equals(eventType, "combat_hit", StringComparison.OrdinalIgnoreCase))
        {
            if (isSelfTarget && floatTextTargetShake.HasValue)
            {
                return floatTextTargetShake;
            }

            if (isSelf && floatTextShake.HasValue)
            {
                return floatTextShake;
            }

            return floatTextShake ?? floatTextTargetShake;
        }

        if (string.Equals(eventType, "combat_death", StringComparison.OrdinalIgnoreCase))
        {
            return floatTextTargetShake ?? floatTextShake;
        }

        return null;
    }

    private CombatDisplayStyle ResolveCombatStyle()
    {
        var style = _combatDisplay.Style?.Trim().ToLowerInvariant();
        return style switch
        {
            "tagged" => CombatDisplayStyle.Tagged,
            "split" => CombatDisplayStyle.Split,
            _ => CombatDisplayStyle.Compact
        };
    }

    private static string? FormatFloatText(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var floatText) ||
            floatText.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var text = floatText.GetPropertyOrDefault("text")?.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var details = new List<string>();
        var color = floatText.GetPropertyOrDefault("color")?.GetString();
        if (!string.IsNullOrWhiteSpace(color))
        {
            details.Add($"color {color}");
        }
        else
        {
            var colorKey = floatText.GetPropertyOrDefault("colorKey")?.GetString();
            if (!string.IsNullOrWhiteSpace(colorKey))
            {
                details.Add($"colorKey {colorKey}");
            }
        }

        if (TryGetDouble(floatText, "scale", out var scale))
        {
            details.Add($"scale {scale.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        if (TryGetDouble(floatText, "shake", out var shake))
        {
            details.Add($"shake {shake.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        return details.Count == 0
            ? text
            : $"{text} ({string.Join(", ", details)})";
    }

    private static string? GetFloatTextColorKey(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var floatText) ||
            floatText.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var colorKey = floatText.GetPropertyOrDefault("colorKey")?.GetString();
        if (!string.IsNullOrWhiteSpace(colorKey))
        {
            return colorKey;
        }

        var color = floatText.GetPropertyOrDefault("color")?.GetString();
        if (!string.IsNullOrWhiteSpace(color))
        {
            return color;
        }

        return null;
    }

    private static double? GetFloatTextShake(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var floatText) ||
            floatText.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetDouble(floatText, "shake", out var shake) ? shake : null;
    }

    private enum CombatDisplayStyle
    {
        Compact,
        Tagged,
        Split
    }

    private readonly record struct CombatOutcomeFlags(
        bool Critical,
        bool Glancing,
        bool Deflected,
        bool Penetrating);

    private IEnumerable<LogLine> RenderError(JsonElement payload)
    {
        var code = payload.GetPropertyOrDefault("code")?.GetString();
        var message = payload.GetPropertyOrDefault("message")?.GetString();
        yield return $"Error: {code} {message}".Trim();
    }

    private IEnumerable<LogLine> RenderCommunication(JsonElement payload)
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

    private IEnumerable<LogLine> RenderCommandResponse(JsonElement payload)
    {
        var success = payload.GetPropertyOrDefault("success")?.GetBoolean();
        var command = payload.GetPropertyOrDefault("command")?.GetString();
        var message = payload.GetPropertyOrDefault("message")?.GetString();

        if (payload.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("members", out var members) &&
            members.ValueKind == JsonValueKind.Array)
        {
            var names = members.EnumerateArray()
                .Select(entry => entry.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();
            if (names.Count > 0)
            {
                _state.Party.SetMembers(names);
            }
        }

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

    private IEnumerable<LogLine> RenderPartyEvent(string eventType, JsonElement payload)
    {
        if (string.Equals(eventType, "party_invite", StringComparison.OrdinalIgnoreCase))
        {
            var fromName = payload.GetPropertyOrDefault("fromName")?.GetString();
            yield return string.IsNullOrWhiteSpace(fromName)
                ? "Party invite received."
                : $"Party invite from {fromName}.";
            yield break;
        }

        if (string.Equals(eventType, "party_roster", StringComparison.OrdinalIgnoreCase))
        {
            var leaderId = payload.GetPropertyOrDefault("leaderId")?.GetString();
            _state.Party.SetLeader(leaderId);
            if (payload.TryGetProperty("members", out var members) &&
                members.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var entry in members.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object)
                    {
                        var id = entry.GetPropertyOrDefault("id")?.GetString();
                        var name = entry.GetPropertyOrDefault("name")?.GetString();
                        _state.Party.AddMember(id, name);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            names.Add(name!);
                        }
                    }
                }

                if (names.Count > 0)
                {
                    yield return $"Party roster: {string.Join(", ", names)}";
                    yield break;
                }
            }
        }

        if (string.Equals(eventType, "party_joined", StringComparison.OrdinalIgnoreCase))
        {
            var memberId = payload.GetPropertyOrDefault("memberId")?.GetString();
            var memberName = payload.GetPropertyOrDefault("memberName")?.GetString();
            _state.Party.AddMember(memberId, memberName);
            if (!string.IsNullOrWhiteSpace(memberName))
            {
                yield return $"{memberName} joined the party.";
                yield break;
            }
            yield return "A party member joined.";
            yield break;
        }

        if (string.Equals(eventType, "party_left", StringComparison.OrdinalIgnoreCase))
        {
            var memberId = payload.GetPropertyOrDefault("memberId")?.GetString();
            var memberName = payload.GetPropertyOrDefault("memberName")?.GetString();
            _state.Party.RemoveMember(memberId, memberName);
            if (!string.IsNullOrWhiteSpace(memberName))
            {
                yield return $"{memberName} left the party.";
                yield break;
            }
            yield return "A party member left.";
            yield break;
        }

        if (string.Equals(eventType, "party_kicked", StringComparison.OrdinalIgnoreCase))
        {
            var memberId = payload.GetPropertyOrDefault("memberId")?.GetString();
            var memberName = payload.GetPropertyOrDefault("memberName")?.GetString();
            _state.Party.RemoveMember(memberId, memberName);
            if (!string.IsNullOrWhiteSpace(memberName))
            {
                yield return $"{memberName} was removed from the party.";
                yield break;
            }
            yield return "A party member was removed.";
            yield break;
        }

        yield return $"Event: {eventType}";
    }

    private IEnumerable<LogLine> RenderDevAck(JsonElement payload)
    {
        var message = payload.GetPropertyOrDefault("message")?.GetString();
        if (!string.IsNullOrWhiteSpace(message))
        {
            yield return $"dev_ack: {message}";
            yield break;
        }

        yield return $"dev_ack: {payload.GetRawText()}";
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

    private static IEnumerable<LogLine> WrapText(string text, int width)
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

    private IEnumerable<LogLine> ApplyMovementState(JsonElement payload, bool logAvailableDirections)
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
            var entities = new List<ProximityEntity>();
            var sampleProvided = false;
            var lastSpeakerProvided = false;
            List<string>? sample = null;
            string? lastSpeaker = null;
            int? countValue = null;
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
                        entities.Add(entity);
                    }
                }
            }

            if (channelPayload.TryGetProperty("count", out var count))
            {
                if (count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out var parsedCount))
                {
                    countValue = parsedCount;
                }
            }

            if (channelPayload.TryGetProperty("sample", out var sampleElement) &&
                sampleElement.ValueKind == JsonValueKind.Array)
            {
                sampleProvided = true;
                sample = sampleElement.EnumerateArray()
                    .Select(entry => entry.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .ToList();
            }
            else if (channelPayload.TryGetProperty("sample", out var sampleNull) &&
                     sampleNull.ValueKind == JsonValueKind.Null)
            {
                sampleProvided = true;
                sample = null;
            }

            if (channelPayload.TryGetProperty("lastSpeaker", out var lastSpeakerElement))
            {
                lastSpeakerProvided = true;
                lastSpeaker = lastSpeakerElement.ValueKind == JsonValueKind.Null ? null : lastSpeakerElement.GetString();
            }

            _state.ProximityRoster.ReplaceChannel(channel.Name, entities, countValue, sample, sampleProvided, lastSpeaker, lastSpeakerProvided);
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
                delta.SampleChanged = true;
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

        if (string.IsNullOrWhiteSpace(entity.Id) && string.IsNullOrWhiteSpace(entity.Name))
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

        if (TryGetDouble(entry, "range", out var range) || TryGetDouble(entry, "distance", out range))
        {
            entity.Range = range;
        }

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

    private static int? GetInt(JsonElement? element)
    {
        if (!element.HasValue)
        {
            return null;
        }

        return element.Value.ValueKind switch
        {
            JsonValueKind.Number => element.Value.TryGetInt32(out var value) ? value : null,
            JsonValueKind.String => int.TryParse(element.Value.GetString(), out var value) ? value : null,
            _ => null
        };
    }

    private static int? GetIntFromObject(JsonElement? element, string propertyName)
    {
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetInt(element.Value.GetPropertyOrDefault(propertyName));
    }

    private void UpdatePlayerPositionFromCharacter(JsonElement character)
    {
        if (character.TryGetProperty("id", out var idElement))
        {
            _state.UpdatePlayerId(idElement.GetString());
        }
        if (TryParsePosition(character.GetPropertyOrDefault("position"), out var position))
        {
            _state.UpdatePlayerPosition(position);
        }
    }

    private void UpdateVitalsFromCharacter(JsonElement character)
    {
        var currentHp = GetInt(character.GetPropertyOrDefault("currentHp"))
            ?? GetIntFromObject(character.GetPropertyOrDefault("hp"), "current")
            ?? GetIntFromObject(character.GetPropertyOrDefault("health"), "current");
        var maxHp = GetInt(character.GetPropertyOrDefault("maxHp"))
            ?? GetIntFromObject(character.GetPropertyOrDefault("hp"), "max")
            ?? GetIntFromObject(character.GetPropertyOrDefault("health"), "max");
        var currentMana = GetInt(character.GetPropertyOrDefault("currentMana"))
            ?? GetInt(character.GetPropertyOrDefault("currentMp"))
            ?? GetIntFromObject(character.GetPropertyOrDefault("mana"), "current")
            ?? GetIntFromObject(character.GetPropertyOrDefault("mp"), "current");
        var maxMana = GetInt(character.GetPropertyOrDefault("maxMana"))
            ?? GetInt(character.GetPropertyOrDefault("maxMp"))
            ?? GetIntFromObject(character.GetPropertyOrDefault("mana"), "max")
            ?? GetIntFromObject(character.GetPropertyOrDefault("mp"), "max");
        var currentStamina = GetInt(character.GetPropertyOrDefault("currentStamina"))
            ?? GetIntFromObject(character.GetPropertyOrDefault("stamina"), "current");
        var maxStamina = GetInt(character.GetPropertyOrDefault("maxStamina"))
            ?? GetIntFromObject(character.GetPropertyOrDefault("stamina"), "max");

        if (currentHp.HasValue || maxHp.HasValue ||
            currentMana.HasValue || maxMana.HasValue ||
            currentStamina.HasValue || maxStamina.HasValue)
        {
            _state.UpdateVitals(currentHp, maxHp, currentMana, maxMana, currentStamina, maxStamina);
        }
    }

    private void UpdatePartyAllies(JsonElement alliesElement)
    {
        var allies = new List<PartyAllyStatus>();
        foreach (var entry in alliesElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var entityId = entry.GetPropertyOrDefault("entityId")?.GetString();
            if (string.IsNullOrWhiteSpace(entityId))
            {
                continue;
            }

            int? atbCurrent = null;
            int? atbMax = null;
            if (entry.TryGetProperty("atb", out var atb) && atb.ValueKind == JsonValueKind.Object)
            {
                atbCurrent = GetInt(atb.GetPropertyOrDefault("current"));
                atbMax = GetInt(atb.GetPropertyOrDefault("max"));
            }

            var staminaPct = GetNumber(entry.GetPropertyOrDefault("staminaPct"));
            var manaPct = GetNumber(entry.GetPropertyOrDefault("manaPct"));

            allies.Add(new PartyAllyStatus
            {
                EntityId = entityId,
                AtbCurrent = atbCurrent,
                AtbMax = atbMax,
                StaminaPct = staminaPct,
                ManaPct = manaPct
            });
        }

        _state.Party.UpdateAllies(allies);
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

    private void ApplyCombatState(JsonElement payload)
    {
        if (!payload.TryGetProperty("combat", out var combat) ||
            combat.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var atbProvided = false;
        int? atbCurrent = null;
        int? atbMax = null;
        if (combat.TryGetProperty("atb", out var atb) &&
            atb.ValueKind == JsonValueKind.Object)
        {
            atbProvided = true;
            if (atb.TryGetProperty("current", out var current) &&
                current.ValueKind == JsonValueKind.Number &&
                current.TryGetInt32(out var parsedCurrent))
            {
                atbCurrent = parsedCurrent;
            }

            if (atb.TryGetProperty("max", out var max) &&
                max.ValueKind == JsonValueKind.Number &&
                max.TryGetInt32(out var parsedMax))
            {
                atbMax = parsedMax;
            }
        }

        var autoAttackProvided = false;
        double? autoAttackCurrent = null;
        double? autoAttackMax = null;
        if (combat.TryGetProperty("autoAttack", out var autoAttack) &&
            autoAttack.ValueKind == JsonValueKind.Object)
        {
            autoAttackProvided = true;
            if (TryGetDouble(autoAttack, "current", out var parsedCurrent))
            {
                autoAttackCurrent = parsedCurrent;
            }
            if (TryGetDouble(autoAttack, "max", out var parsedMax))
            {
                autoAttackMax = parsedMax;
            }
        }

        var inCombatProvided = false;
        bool? inCombat = null;
        if (combat.TryGetProperty("inCombat", out var inCombatElement) &&
            (inCombatElement.ValueKind == JsonValueKind.True ||
                inCombatElement.ValueKind == JsonValueKind.False))
        {
            inCombatProvided = true;
            inCombat = inCombatElement.GetBoolean();
        }

        var autoAttackTargetProvided = false;
        string? autoAttackTarget = null;
        if (combat.TryGetProperty("autoAttackTarget", out var autoAttackTargetElement))
        {
            autoAttackTargetProvided = true;
            if (autoAttackTargetElement.ValueKind != JsonValueKind.Null)
            {
                autoAttackTarget = autoAttackTargetElement.GetString();
            }
        }

        _state.UpdateCombatState(
            atbCurrent,
            atbMax,
            atbProvided,
            autoAttackCurrent,
            autoAttackMax,
            autoAttackProvided,
            inCombat,
            inCombatProvided,
            autoAttackTarget,
            autoAttackTargetProvided);
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
