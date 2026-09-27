using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnoMech.Multiplayer;
using AnoMech.Network;
using static AnoMech.Tests.TestWire;

namespace AnoMech.Tests;

public class RelayClientTests
{
    private static readonly string[] Capabilities = ["binaryCompression", "authenticatedIdentity", "roomModeration"];

    [TestCase("current", true)]
    [TestCase("joining without a code", false)]
    [TestCase("wrong version", true)]
    [TestCase("missing capabilities", true)]
    [TestCase("older relay build", true)]
    [TestCase("wrong identity", true)]
    [TestCase("hosting without a code", true)]
    [TestCase("not json", true)]
    public void Greeting(string mode, bool hosting)
    {
        var self = Guid.NewGuid();
        var greeting = mode switch
        {
            "not json" => Bytes("{\"relayVersion\":"),
            "older relay build" => JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version, capabilities = new[] { "binaryCompression", "senderIdentity" }, sessionCode = "ABCDEF" }),
            "wrong version" => JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version + 1, capabilities = Capabilities, sessionCode = "ABCDEF", peerId = self }),
            "missing capabilities" => JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version, capabilities = Array.Empty<string>(), sessionCode = "ABCDEF", peerId = self }),
            "wrong identity" => JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version, capabilities = Capabilities, sessionCode = "ABCDEF", peerId = Guid.NewGuid() }),
            "hosting without a code" or "joining without a code" => JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version, capabilities = Capabilities, peerId = self }),
            _ => JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version, capabilities = Capabilities, sessionCode = "ABCDEF", peerId = self }),
        };
        if (mode is "current" or "joining without a code")
        {
            var (code, capabilities) = RelayClient.ParseGreeting(greeting, self, hosting);
            Assert.That(code, Is.EqualTo(hosting ? "ABCDEF" : null));
            Assert.That(capabilities, Is.EquivalentTo(Capabilities));
        }
        else Assert.That(() => RelayClient.ParseGreeting(greeting, self, hosting), Throws.InstanceOf<RelaySessionRejectedException>(), "greeting is a terminal refusal");
    }

    [TestCase("fragmented")]
    [TestCase("closed")]
    [TestCase("slow")]
    public async Task GreetingOverTheWire(string mode)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var secret = RelayWire.NewSecret();
        using var client = new RelayClient(secret) { GreetingTimeout = TimeSpan.FromMilliseconds(100) };
        Exception? failure = null;
        client.Disconnected += e => failure = e;
        var connect = client.ConnectAndHostAsync($"ws://127.0.0.1:{port}");
        var accepted = await AcceptSocket(listener).WaitAsync(TimeSpan.FromSeconds(5));
        using var socket = accepted.Socket;
        listener.Stop();
        Assert.That(accepted.Headers.GetValueOrDefault("X-AnoMech-Peer-Secret"), Is.EqualTo(secret), "credential header sent");
        Assert.That(accepted.Headers.GetValueOrDefault("X-AnoMech-Protocol"), Is.EqualTo(RelayWire.Version.ToString()), "protocol header sent");
        if (mode == "fragmented")
        {
            // Split, so the client must reassemble it.
            var data = JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version, capabilities = Capabilities, sessionCode = "ABCDEF", peerId = RelayWire.PeerId(secret) });
            await socket.SendAsync(data.AsMemory(0, 7), WebSocketMessageType.Text, false, CancellationToken.None);
            await socket.SendAsync(data.AsMemory(7), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        else if (mode == "closed") await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "session not found", CancellationToken.None);
        var code = await connect.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(client.IsConnected, Is.EqualTo(mode == "fragmented"));
        Assert.That(code, Is.EqualTo(mode == "fragmented" ? "ABCDEF" : null));
        if (mode == "slow") Assert.That(failure, Is.Not.Null.And.Not.InstanceOf<RelaySessionRejectedException>(), "slow greeting isn't treated as a refusal");
        if (mode == "closed") Assert.That(failure, Is.InstanceOf<RelaySessionRejectedException>().With.Message.EqualTo("session not found"));
    }

    // A redirect must not carry the credential headers to a server the user never named.
    [TestCase("ws")]
    [TestCase("http")]
    public async Task RedirectDoesNotForwardPeerCredentials(string scheme)
    {
        var source = new TcpListener(IPAddress.Loopback, 0);
        var target = new TcpListener(IPAddress.Loopback, 0);
        source.Start(); target.Start();
        try
        {
            var sourcePort = ((IPEndPoint)source.LocalEndpoint).Port;
            var targetPort = ((IPEndPoint)target.LocalEndpoint).Port;
            using var client = new RelayClient(RelayWire.NewSecret());
            var connect = client.ConnectAndHostAsync($"ws://127.0.0.1:{sourcePort}");
            using (var accepted = await source.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            {
                var stream = accepted.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                await stream.WriteAsync(Bytes($"HTTP/1.1 302 Found\r\nLocation: {scheme}://127.0.0.1:{targetPort}/host\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
            }
            // Following the redirect would leave connect waiting on the target, which never answers.
            var settled = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(5))) == connect;
            Assert.That(settled, "connect settled");
            Assert.That(client.IsConnected, Is.False);
            Assert.That(target.Pending(), Is.False, "redirect target never contacted");
        }
        finally { source.Stop(); target.Stop(); }
    }

    private static async Task<(WebSocket Socket, string Path, Dictionary<string, string> Headers)> AcceptSocket(TcpListener listener)
    {
        var client = await listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var request = (await reader.ReadLineAsync())!.Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
        {
            var colon = line.IndexOf(':');
            headers[line[..colon]] = line[(colon + 1)..].Trim();
        }
        var accept = Convert.ToBase64String(SHA1.HashData(Bytes(headers["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        await stream.WriteAsync(Bytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));
        return (WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30)), request[1], headers);
    }
}
