namespace WodMudClient;

public sealed record LogLine(string Text, string? ColorKey = null, double? Shake = null)
{
    public static implicit operator LogLine(string text) => new(text);
}
