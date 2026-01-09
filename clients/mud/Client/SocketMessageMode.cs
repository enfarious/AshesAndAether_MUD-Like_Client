namespace WodMudClient;

public enum SocketMessageMode
{
    Envelope,
    Event
}

public static class SocketMessageModeParser
{
    public static SocketMessageMode Parse(string? value, SocketMessageMode fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "event" => SocketMessageMode.Event,
            "event-per-type" => SocketMessageMode.Event,
            "per-event" => SocketMessageMode.Event,
            "per_event" => SocketMessageMode.Event,
            "envelope" => SocketMessageMode.Envelope,
            "message" => SocketMessageMode.Envelope,
            _ => fallback
        };
    }
}
