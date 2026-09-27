using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using AnoMech.Multiplayer;
using AnoMech.Network;
using AnoMech.Relay;
using static AnoMech.Tests.TestWire;

namespace AnoMech.Tests;

// RelayClient against the real RelayServer over loopback.
[Category("Integration")]
public class RelayLoopbackTests
{
    // Clients a test places on another network name it in X-Test-Address, which the relay
    // believes because loopback is configured as its proxy.
    [Test]
    public async Task LiveRelay()
    {
        var log = new QuietLog();
        var options = new RelayOptions { BindAddress = IPAddress.Loopback, Port = 0, ClientIpHeader = "X-Test-Address" };
        options.TrustedProxies.Add(new IPNetwork(IPAddress.Loopback, 32));
        await using var server = new RelayServer(options, log);
        server.Start();
        var port = server.LocalEndPoint!.Port;
        var completed = false;
        try
        {
            var url = $"ws://localhost:{port}";

            var hostSecret = RelayWire.NewSecret();
            var hostId = RelayWire.PeerId(hostSecret);
            var host = new RelayClient(hostSecret);
            var code = await host.ConnectAndHostAsync(url);
            Assert.That(host.IsConnected && code != null, "client hosts room");
            var helloReceived = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.MessageReceived += (message, fromHost, connection, sender) => { if (message is HelloMessage) helloReceived.TrySetResult(sender); };

            var peerSecret = RelayWire.NewSecret();
            var peerId = RelayWire.PeerId(peerSecret);
            using var peer = new RelayClient(peerSecret);
            using var peerPings = new BlockingCollection<(bool FromHost, Guid Sender)>();
            var compressedReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            peer.MessageReceived += (message, fromHost, connection, sender) =>
            {
                if (message is PingMessage) peerPings.Add((fromHost, sender));
                if (message is AnnouncementMessage announcement) compressedReceived.TrySetResult(announcement.Text == new string('x', 512));
            };
            await peer.ConnectAsync(url, code!);
            Assert.That(peer.IsConnected, "client joins room");
            await peer.SendAsync(new HelloMessage(peerId, "Test", "1", "test"));
            Assert.That(await Within(helloReceived.Task, "hello"), Is.EqualTo(peerId), "relay stamps the authenticated sender");
            await host.SendAsync(new PingMessage(123));
            Assert.That(peerPings.TryTake(out var ping, 5000) && ping.FromHost && ping.Sender == hostId, "host tag reaches clients");
            await host.SendAsync(new AnnouncementMessage(new string('x', 512)));
            Assert.That(await Within(compressedReceived.Task, "announcement"), "compressed payload survives the envelope");

            var rejections = new ConcurrentDictionary<Guid, TaskCompletionSource<string>>();
            host.MessageRejected += (sender, fromHost, reason) => { if (!fromHost && rejections.TryGetValue(sender, out var waiter)) waiter.TrySetResult(reason); };
            using var observer = await ConnectRaw(url, code!, RelayWire.NewSecret());
            async Task<bool> DroppedByHost(byte[] body, WebSocketMessageType type)
            {
                var secret = RelayWire.NewSecret();
                var waiter = rejections.GetOrAdd(RelayWire.PeerId(secret), _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
                using var attacker = await ConnectRaw(url, code!, secret);
                await SendRaw(attacker, body, type);
                await Within(waiter.Task, "rejection");
                return host.IsConnected && peer.IsConnected;
            }
            Assert.That(await DroppedByHost(Bytes($$"""{"t":"hello","PeerId":"{{hostId}}","DisplayName":"Fake"}"""), WebSocketMessageType.Text),
                "host identity spoof dropped");
            Assert.That(await DroppedByHost(Bytes($$"""{"t":"claim","PeerId":"{{peerId}}","Role":1}"""), WebSocketMessageType.Text),
                "peer identity spoof dropped");
            Assert.That(await DroppedByHost(Bytes("{\"t\":\"snapshot\",\"Enemies\":[],\"EventObjects\":[],\"Tethers\":[]}"), WebSocketMessageType.Text),
                "forged host snapshot dropped");
            Assert.That(await DroppedByHost(Bytes("{\"t\":\"pose\","), WebSocketMessageType.Text), "malformed peer message leaves the room up");
            Assert.That(await DroppedByHost(Zip(Bytes("{\"t\":\"pose\",\"X\":\"" + new string('x', 1_000_000) + "\"}")), WebSocketMessageType.Binary),
                "relay forwards a peer's compression bomb unread and the host stops it");
            Assert.That(await TryReadFrame(observer, TimeSpan.FromMilliseconds(500)), Is.Null, "peer traffic reaches only the host");

            var replacementSecret = RelayWire.NewSecret();
            var replacementId = RelayWire.PeerId(replacementSecret);
            using var oldPeer = await ConnectRaw(url, code!, replacementSecret);
            using var replacement = await ConnectRaw(url, code!, replacementSecret);
            var resumedHello = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.MessageReceived += (message, fromHost, connection, sender) => { if (message is HelloMessage && sender == replacementId) resumedHello.TrySetResult(true); };
            await SendRaw(replacement, $$"""{"t":"hello","PeerId":"{{replacementId}}","DisplayName":"Reconnected","Version":"1","Checksum":"test"}""");
            Assert.That(await Within(resumedHello.Task, "resumed hello"), "credential resumes on a new connection");
            Assert.That(await ClosedByRelay(oldPeer), "resumed credential replaces the old connection");

            host.Dispose();
            await Task.Delay(300);
            Assert.That(peer.IsConnected, "room outlives the host's connection");
            var reconnectedHost = new RelayClient(hostSecret);
            await reconnectedHost.ConnectAsync(url, code!);
            Assert.That(reconnectedHost.IsConnected, "host reconnects to its room");
            await reconnectedHost.SendAsync(new PingMessage(456));
            Assert.That(peerPings.TryTake(out var restored, 5000) && restored.FromHost && restored.Sender == hostId, "reconnected host is the host again");
            host = reconnectedHost;

            using var otherNetwork = await ConnectRaw(url, code!, RelayWire.NewSecret(), address: "192.0.2.20");
            var kicked = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var removedNotice = new TaskCompletionSource<IReadOnlyList<Guid>>(TaskCreationOptions.RunContinuationsAsynchronously);
            peer.Disconnected += e => kicked.TrySetResult(e);
            host.PeersRemoved += ids => removedNotice.TrySetResult(ids);
            await host.ModerateAsync("ban", peerId);
            await Within(kicked.Task, "ban disconnect");
            Assert.That(peer.IsConnected, Is.False, "ban disconnects the peer");
            Assert.That(await ClosedByRelay(replacement), "ban closes other identities on the same address");
            var removedIds = await Within(removedNotice.Task, "removal notice");
            Assert.That(removedIds.Contains(replacementId) && !removedIds.Contains(peerId), "host told who else the ban removed");
            Assert.That(host.IsConnected, "ban leaves the host connected");
            Assert.That(otherNetwork.State == WebSocketState.Open && await TryReadFrame(otherNetwork, TimeSpan.FromMilliseconds(500)) is null,
                "ban leaves players on another network alone");
            using (var rotated = await ConnectRaw(url, code!, RelayWire.NewSecret(), expectGreeting: false))
                Assert.That((await ReadFrame(rotated)).Type, Is.EqualTo(WebSocketMessageType.Close), "room ban survives identity rotation on the same address");
            var hostAgain = new RelayClient(hostSecret);
            await hostAgain.ConnectAsync(url, code!);
            Assert.That(hostAgain.IsConnected, "a ban on the host's own network doesn't lock the host out");
            host = hostAgain;
            await host.ModerateAsync("unban", peerId);
            await Task.Delay(200);
            using var resumed = new RelayClient(peerSecret);
            await resumed.ConnectAsync(url, code!);
            Assert.That(resumed.IsConnected, "unban permits reconnect with the same credential");

            var absentSecret = RelayWire.NewSecret();
            var roommateSecret = RelayWire.NewSecret();
            using var roommate = await ConnectRaw(url, code!, roommateSecret, address: "192.0.2.30");
            var laterNotice = new TaskCompletionSource<IReadOnlyList<Guid>>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.PeersRemoved += ids => laterNotice.TrySetResult(ids);
            await host.ModerateAsync("ban", RelayWire.PeerId(absentSecret));
            await Task.Delay(200);
            Assert.That(roommate.State == WebSocketState.Open && resumed.IsConnected, "banning an absent identity removes nobody yet");
            using (var absent = await ConnectRaw(url, code!, absentSecret, expectGreeting: false, address: "192.0.2.30"))
                Assert.That((await ReadFrame(absent)).Type, Is.EqualTo(WebSocketMessageType.Close), "ban issued while the target is away blocks its return");
            Assert.That(await ClosedByRelay(roommate), "the returning banned player's network is cleared like a live ban");
            Assert.That((await Within(laterNotice.Task, "later removal notice")).Contains(RelayWire.PeerId(roommateSecret)), "host told who that removed");
            Assert.That(resumed.IsConnected, "players on other networks stay");
            await host.ModerateAsync("unban", RelayWire.PeerId(absentSecret));

            using (var impostor = await ConnectRaw(url, code!, RelayWire.NewSecret()))
            {
                await SendRaw(impostor, $$"""{"t":"relayControl","Operation":"kick","PeerId":"{{RelayWire.PeerId(peerSecret)}}"}""");
                await Task.Delay(300);
                Assert.That(resumed.IsConnected, "only the host can moderate");
            }

            host.Dispose();
            resumed.Dispose();
            await Task.Delay(300);
            using var late = await ConnectRaw(url, code!, RelayWire.NewSecret(), expectGreeting: false);
            Assert.That((await ReadFrame(late)).Type, Is.EqualTo(WebSocketMessageType.Close), "empty room is gone");
            completed = true;
        }
        finally
        {
            if (!completed) TestContext.Out.WriteLine(string.Join(Environment.NewLine, log.Lines));
        }
    }

    [Test]
    public async Task StoppingTheRelayDropsItsConnections()
    {
        await using var server = new RelayServer(new RelayOptions { BindAddress = IPAddress.Loopback, Port = 0 }, new QuietLog());
        server.Start();
        var port = server.LocalEndPoint!.Port;
        using var host = new RelayClient(RelayWire.NewSecret());
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Disconnected += _ => disconnected.TrySetResult();
        Assert.That(await host.ConnectAndHostAsync($"ws://127.0.0.1:{port}"), Is.Not.Null, "hosts over the managed listener");
        await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(host.IsConnected, Is.False);
    }

    private static async Task<T> Within<T>(Task<T> task, string what)
    {
        try { return await task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { throw new TimeoutException($"Timed out: {what}"); }
    }

    private static async Task<ClientWebSocket> ConnectRaw(string url, string code, string secret, bool expectGreeting = true, string? address = null)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("X-AnoMech-Protocol", RelayWire.Version.ToString());
        socket.Options.SetRequestHeader("X-AnoMech-Peer-Secret", secret);
        if (address != null) socket.Options.SetRequestHeader("X-Test-Address", address);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(new Uri($"{url}/session/{code}"), timeout.Token);
        if (expectGreeting)
        {
            var frame = await ReadFrame(socket);
            Assert.That(frame.Type, Is.EqualTo(WebSocketMessageType.Text));
            Assert.That(JsonDocument.Parse(frame.Bytes).RootElement.GetProperty("peerId").GetGuid(), Is.EqualTo(RelayWire.PeerId(secret)),
                "greeting names the authenticated identity");
        }
        return socket;
    }

    private static Task SendRaw(ClientWebSocket socket, string text) => SendRaw(socket, Bytes(text), WebSocketMessageType.Text);

    private static async Task SendRaw(ClientWebSocket socket, byte[] body, WebSocketMessageType type)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.SendAsync(body, type, true, timeout.Token);
    }

    private static async Task<(byte[] Bytes, WebSocketMessageType Type)> ReadFrame(ClientWebSocket socket, TimeSpan? wait = null)
    {
        using var timeout = new CancellationTokenSource(wait ?? TimeSpan.FromSeconds(5));
        using var output = new MemoryStream(); var buffer = new byte[65536];
        WebSocketReceiveResult result;
        do { result = await socket.ReceiveAsync(buffer, timeout.Token); output.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
        return (output.ToArray(), result.MessageType);
    }

    private static async Task<(byte[] Bytes, WebSocketMessageType Type)?> TryReadFrame(ClientWebSocket socket, TimeSpan wait)
    {
        try { return await ReadFrame(socket, wait); }
        catch (OperationCanceledException) { return null; }
    }

    // A close frame or an abort, after any room traffic already on its way; silence is a failure.
    private static async Task<bool> ClosedByRelay(ClientWebSocket socket)
    {
        try
        {
            for (var frames = 0; frames < 64; frames++)
                if ((await ReadFrame(socket)).Type == WebSocketMessageType.Close) return true;
            return false;
        }
        catch (WebSocketException) { return true; }
        catch (OperationCanceledException) { return false; }
    }
}
