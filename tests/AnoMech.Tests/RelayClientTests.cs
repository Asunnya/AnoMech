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

// RelayClient against hand-driven fake servers.
public class RelayClientTests
{
    [TestCase("current")]
    [TestCase("wrong version")]
    [TestCase("missing capabilities")]
    [TestCase("older relay build")]
    [TestCase("wrong identity")]
    [TestCase("timeout")]
    public async Task Greeting(string mode)
    {
        var valid = mode == "current";
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var secret = RelayWire.NewSecret();
        using var client = new RelayClient(secret);
        Exception? failure = null;
        client.Disconnected += e => failure = e;
        var connect = client.ConnectAndHostAsync($"ws://localhost:{port}");
        var accepted = await AcceptSocket(listener).WaitAsync(TimeSpan.FromSeconds(5));
        using var socket = accepted.Socket;
        listener.Stop();
        Assert.That(accepted.Headers.GetValueOrDefault("X-AnoMech-Peer-Secret"), Is.EqualTo(secret), "credential header sent");
        Assert.That(accepted.Headers.GetValueOrDefault("X-AnoMech-Protocol"), Is.EqualTo(RelayWire.Version.ToString()), "protocol header sent");
        if (mode != "timeout")
        {
            var capabilities = new[] { "binaryCompression", "authenticatedIdentity", "roomModeration" };
            var data = mode switch
            {
                "older relay build" => JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version, capabilities = new[] { "binaryCompression", "senderIdentity" }, sessionCode = "ABCDEF" }),
                "wrong version" => JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version + 1, capabilities, sessionCode = "ABCDEF", peerId = RelayWire.PeerId(secret) }),
                "missing capabilities" => JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version, capabilities = Array.Empty<string>(), sessionCode = "ABCDEF", peerId = RelayWire.PeerId(secret) }),
                "wrong identity" => JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version, capabilities, sessionCode = "ABCDEF", peerId = Guid.NewGuid() }),
                _ => JsonSerializer.SerializeToUtf8Bytes(new { relayVersion = RelayWire.Version, capabilities, sessionCode = "ABCDEF", peerId = RelayWire.PeerId(secret) }),
            };
            // Fragmented, so the client must reassemble the greeting.
            await socket.SendAsync(data.AsMemory(0, 7), WebSocketMessageType.Text, false, CancellationToken.None);
            await socket.SendAsync(data.AsMemory(7), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        var code = await connect.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.That(client.IsConnected, Is.EqualTo(valid));
        Assert.That(code == "ABCDEF", Is.EqualTo(valid));
        if (mode == "timeout") Assert.That(failure, Is.Not.Null.And.Not.InstanceOf<RelaySessionRejectedException>(), "slow greeting isn't treated as a refusal");
        else if (!valid) Assert.That(failure, Is.InstanceOf<RelaySessionRejectedException>(), "greeting is a terminal refusal");
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
            var connect = client.ConnectAndHostAsync($"ws://localhost:{sourcePort}");
            using (var accepted = await source.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            {
                var stream = accepted.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                await stream.WriteAsync(Bytes($"HTTP/1.1 302 Found\r\nLocation: {scheme}://localhost:{targetPort}/host\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
            }
            // The redirect, if followed at all, is only attempted once this connection closes.
            var settled = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(5))) == connect;
            await Task.Delay(500);
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
