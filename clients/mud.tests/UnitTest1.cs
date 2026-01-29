using System.Text.Json;
using AshesAndAether_Client;
using Xunit;

namespace AshesAndAether_Client.Tests;

public sealed class ClientProtocolTests
{
    [Fact]
    public void RenderChatMessage_UsesChannelVerb()
    {
        var state = new StateStore();
        var router = new MessageRouter(state, new CombatDisplayConfig());
        using var doc = JsonDocument.Parse("""
        {
          "channel": "say",
          "sender": "Aria",
          "senderId": "char-1",
          "message": "Hello there",
          "timestamp": 1700000000000
        }
        """);

        var message = new IncomingMessage
        {
            Type = "chat",
            Payload = doc.RootElement.Clone()
        };

        var lines = router.Handle(message, includeDiagnostics: true, showDevNotices: false)
            .Select(line => line.Text)
            .ToList();

        Assert.Contains("Aria says, Hello there", lines);
    }

    [Fact]
    public void ProximityRosterDelta_AddsEntities()
    {
        var state = new StateStore();
        var router = new MessageRouter(state, new CombatDisplayConfig());
        using var doc = JsonDocument.Parse("""
        {
          "channels": {
            "say": {
              "added": [
                {
                  "id": "npc-1",
                  "name": "Warden",
                  "type": "npc",
                  "bearing": 90,
                  "elevation": 0,
                  "range": 12.5
                }
              ],
              "count": 1,
              "sample": ["Warden"]
            }
          }
        }
        """);

        var message = new IncomingMessage
        {
            Type = "proximity_roster_delta",
            Payload = doc.RootElement.Clone()
        };

        router.Handle(message, includeDiagnostics: false, showDevNotices: false).ToList();

        var entities = state.ProximityRoster.GetEntitiesForNavigation();
        Assert.Single(entities);
        Assert.Equal("npc-1", entities[0].Id);
        Assert.Equal("Warden", entities[0].Name);
    }
}

public sealed class ClientIntegrationTests
{
    [Fact]
    public async Task CanHandshakeAndGuestAuth_WhenServerAvailable()
    {
        var serverUrl = Environment.GetEnvironmentVariable("MUD_TEST_SERVER_URL");
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new Xunit.Sdk.XunitException("MUD_TEST_SERVER_URL is not set. Example: $env:MUD_TEST_SERVER_URL = \"http://localhost:3100\"");
        }

        var transport = new SocketIoTransport();
        try
        {
            await transport.ConnectAsync(serverUrl, "message", SocketMessageMode.Event);
            await WaitForConnectedAsync(transport, serverUrl);

            var handshakeTask = WaitForMessageAsync(transport, msg => msg.Type == "handshake_ack", serverUrl);
            await transport.SendAsync("handshake", new
            {
                protocolVersion = "1.0.0",
                clientType = "text",
                clientVersion = "tests",
                capabilities = new
                {
                    graphics = false,
                    audio = false,
                    input = new[] { "keyboard" },
                    maxUpdateRate = 1
                }
            });
            await handshakeTask;

            var authTask = WaitForMessageAsync(transport, msg => msg.Type == "auth_success", serverUrl);
            await transport.SendAsync("auth", new { method = "guest", guestName = "TestGuest" });
            await authTask;
        }
        finally
        {
            await transport.DisconnectAsync();
            transport.Dispose();
        }
    }

    private static async Task<IncomingMessage> WaitForMessageAsync(
        SocketIoTransport transport,
        Func<IncomingMessage, bool> predicate,
        string serverUrl,
        int timeoutMs = 5000)
    {
        var tcs = new TaskCompletionSource<IncomingMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastRaw = string.Empty;
        var lastSeenAt = DateTimeOffset.MinValue;

        void Handler(string json)
        {
            lastRaw = json;
            lastSeenAt = DateTimeOffset.UtcNow;
            if (!IncomingMessage.TryParse(json, out var message) || message == null)
            {
                return;
            }

            if (predicate(message))
            {
                tcs.TrySetResult(message);
            }
        }

        transport.MessageReceived += Handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (completed != tcs.Task)
            {
                var detail = string.IsNullOrWhiteSpace(lastRaw)
                    ? "No messages received."
                    : $"Last message at {lastSeenAt:O}: {lastRaw}";
                throw new TimeoutException($"Timed out waiting for server message from {serverUrl}. {detail}");
            }

            return await tcs.Task;
        }
        finally
        {
            transport.MessageReceived -= Handler;
        }
    }

    private static async Task WaitForConnectedAsync(SocketIoTransport transport, string serverUrl, int timeoutMs = 5000)
    {
        if (transport.IsConnected)
        {
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler()
        {
            tcs.TrySetResult(true);
        }

        transport.Connected += Handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (completed != tcs.Task)
            {
                throw new TimeoutException($"Timed out waiting for socket connection to {serverUrl}.");
            }
        }
        finally
        {
            transport.Connected -= Handler;
        }
    }
}
