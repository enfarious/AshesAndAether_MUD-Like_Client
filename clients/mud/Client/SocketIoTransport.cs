using System.Text.Json;
using SocketIOClient;
using SocketIOClient.Transport;

namespace WodMudClient;

public sealed class SocketIoTransport : IDisposable
{
    private SocketIO? _socket;
    private string _receiveEventName = "message";
    private SocketMessageMode _receiveMode = SocketMessageMode.Envelope;

    public bool IsConnected => _socket?.Connected ?? false;

    public event Action? Connected;
    public event Action<string>? Disconnected;
    public event Action<string>? MessageReceived;

    public async Task ConnectAsync(string serverUrl, string receiveEventName, SocketMessageMode receiveMode)
    {
        if (_socket != null)
        {
            await DisconnectAsync();
        }

        _receiveEventName = receiveEventName;
        _receiveMode = receiveMode;

        var socket = new SocketIO(serverUrl, new SocketIOOptions
        {
            Transport = TransportProtocol.WebSocket
        });

        socket.OnConnected += (_, _) => Connected?.Invoke();
        socket.OnDisconnected += (_, reason) => Disconnected?.Invoke(reason);
        if (_receiveMode == SocketMessageMode.Event)
        {
            socket.OnAny((eventName, response) =>
            {
                if (ShouldIgnoreEvent(eventName))
                {
                    return;
                }

                var payloadJson = "null";
                var payloadIsJson = true;
                if (TryGetJson(response, out var json))
                {
                    payloadJson = json;
                    payloadIsJson = IsValidJson(json);
                }

                var envelope = BuildEnvelopeJson(eventName, payloadJson, payloadIsJson);
                MessageReceived?.Invoke(envelope);
            });
        }
        else
        {
            socket.On(_receiveEventName, response =>
            {
                if (TryGetJson(response, out var json))
                {
                    MessageReceived?.Invoke(json);
                }
            });
        }

        _socket = socket;
        await socket.ConnectAsync();
    }

    public async Task DisconnectAsync()
    {
        if (_socket == null)
        {
            return;
        }

        await _socket.DisconnectAsync();
        _socket.Dispose();
        _socket = null;
    }

    public async Task SendAsync(string eventName, object payload)
    {
        if (_socket == null || !_socket.Connected)
        {
            return;
        }

        await _socket.EmitAsync(eventName, payload);
    }

    private static bool TryGetJson(SocketIOResponse response, out string json)
    {
        json = string.Empty;

        try
        {
            json = response.GetValue<string>();
            return !string.IsNullOrWhiteSpace(json);
        }
        catch (Exception)
        {
        }

        try
        {
            var element = response.GetValue<JsonElement>();
            json = element.GetRawText();
            return !string.IsNullOrWhiteSpace(json);
        }
        catch (Exception)
        {
        }

        return false;
    }

    private static bool IsValidJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind != JsonValueKind.Undefined;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildEnvelopeJson(string eventName, string payloadJson, bool payloadIsJson)
    {
        var typeJson = JsonSerializer.Serialize(eventName);
        var payload = payloadIsJson ? payloadJson : JsonSerializer.Serialize(payloadJson);
        return $"{{\"type\":{typeJson},\"payload\":{payload}}}";
    }

    private static bool ShouldIgnoreEvent(string eventName)
    {
        return eventName.Equals("connect", StringComparison.OrdinalIgnoreCase)
            || eventName.Equals("disconnect", StringComparison.OrdinalIgnoreCase)
            || eventName.Equals("connect_error", StringComparison.OrdinalIgnoreCase)
            || eventName.Equals("reconnect", StringComparison.OrdinalIgnoreCase)
            || eventName.Equals("reconnect_attempt", StringComparison.OrdinalIgnoreCase)
            || eventName.Equals("reconnect_failed", StringComparison.OrdinalIgnoreCase)
            || eventName.Equals("reconnect_error", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_socket == null)
        {
            return;
        }

        _socket.Dispose();
        _socket = null;
    }
}
