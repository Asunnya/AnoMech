using AnoMech.Network;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace AnoMech.Relay;

// Session-scoped relay. Authenticates each connection's identity, tags every forwarded frame
// with it, routes peer traffic only to the host, and enforces the host's kicks and bans. Message
// bodies pass through unread and are never decompressed; all app-level meaning lives in the
// plugin, which also does every check on message content.
//
// Exists because a Dalamud client is firewalled off from FFXIV's own server traffic during
// a scenario (see ZoneSession), and most players sit behind NAT, so direct P2P isn't viable.
//
// Meant to be runnable as a public service (anyone can point a plugin at it, not just people
// you've personally shared a URL with) -- see Relay/README.md's Security notes for the full
// threat model this is designed against.
internal static class Program
{
    // ---- Tunables (defaults; all overridable via CLI flags, see Main) -------------------

    // Hard cap per session so one room can't be griefed into an unbounded fan-out.
    private static int MaxPeersPerSession = 8;

    // Hard cap on live rooms process-wide -- without this, spamming /host costs nothing and
    // grows Sessions unbounded.
    private static int MaxTotalSessions = 500;

    // One logical message can't exceed this once reassembled from fragments -- otherwise a
    // client that never sends EndOfMessage (or sends gigabytes of it) can OOM the process.
    // ~100x the largest frame a full 8-peer run produces (see MaxMessagesPerSecond).
    private static long MaxMessageBytes = 1 * 1024 * 1024;

    // Live sockets allowed from one source address at once, across every room. Sized with
    // slack for legitimate NAT/CGNAT sharing (mobile carriers, corporate networks) -- a
    // public relay sees much more of this than a friend-only one, so don't set this too tight.
    private static int MaxConnectionsPerIp = 64;

    // Brute-force guard shared by every kind of guessable secret (session codes, --token):
    // this many failures from one address inside the window trips a lockout, so guessing at
    // scale isn't free. --admin-token failures use their own bucket (AdminLockoutUntilByIp)
    // so an admin-endpoint scan can never lock players out of joining.
    private static int MaxFailedJoinsPerWindow = 10;
    private static readonly TimeSpan FailedJoinWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan JoinLockoutDuration = TimeSpan.FromMinutes(5);

    // Per-connection send caps. Budgeted from the worst case a legitimate full session
    // produces, then multiplied by ~10:
    //   A host broadcasts one WorldSnapshot + one RolesSnapshot per frame (backpressure-gated
    //   in MultiplayerManager.Tick), so an uncapped 200fps client tops out near 450 msg/s and,
    //   with a ~40-enemy scenario compressing to <10 KB a snapshot, near 2 MB/s. A peer sends
    //   one SelfPose per frame plus occasional state, so roughly half that.
    // Anything past these is far outside what any scenario can generate.
    private static int MaxMessagesPerSecond = 5000;
    private static long MaxBytesPerSecond = 10L * 1024 * 1024;

    // Fraction of any per-connection cap that triggers a one-shot [NEAR-LIMIT] log line.
    // Purely advisory: it is how an operator finds out a real scenario is creeping toward a
    // cap before anyone actually gets cut off.
    private static double UsageWarnFraction = 0.5;

    // Fragments allowed while assembling ONE message, independent of MaxMessageBytes -- a
    // real client's sends arrive as whatever chunk size the OS socket buffer gives, nowhere
    // near this many frames even for a large message; this only bounds someone deliberately
    // sending many tiny frames to burn CPU on ReceiveAsync round-trips while staying under
    // the byte cap.
    private static int MaxFragmentsPerMessage = 2000;

    // A stalled WS handshake or a message that takes too long to fully arrive gets abandoned
    // instead of held open indefinitely (slowloris-style).
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MessageAssemblyTimeout = TimeSpan.FromSeconds(30);

    // Bounds every close handshake. CloseAsync waits for the peer's own close frame, so
    // without this one unresponsive socket stalls whoever is closing it -- which for ReapLoop
    // means every timed control in the process stops with it.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    // How many rejected connections inside one ReapInterval trips an [ALERT] log line and
    // shows as elevated in the admin dashboard. Not a hard block -- purely a "go look at this"
    // signal, since a real distributed attack won't be stopped by anything in this process
    // anyway (see README's Security notes on volumetric/botnet attacks).
    private const long AlertRejectionThresholdPerTick = 50;

    // Optional shared secret gating /host and /session/<code> -- null means anyone can connect
    // (the original friend-relay default). Set via --token; compared with FixedTimeEquals so
    // response timing can't leak how much of a guess was right.
    private static string? AccessToken;

    // Separate secret gating /admin/*. Deliberately independent from AccessToken -- the
    // people you hand the relay's join token to are not necessarily people who should see
    // live abuse counters and IP-level state. Endpoints are 404 (not "401 with an empty
    // check") when unset, so they don't even reveal they exist on a relay nobody enabled.
    private static string? AdminToken;

    // Independent of AccessToken/AdminToken -- a relay with no password at all still carries
    // session codes and full match state, which an operator may want encrypted end-to-end
    // regardless of whether anyone's protecting a secret.
    private static bool RequireTls;

    // Addresses allowed to speak for someone else: only a request arriving FROM one of these
    // has its X-Forwarded-For / X-Forwarded-Proto believed. Empty means every request is
    // judged purely on its transport address, which is correct for a directly-exposed relay
    // and is why this can't default to "trust the header".
    private static readonly List<IPNetwork> TrustedProxies = new();
    private static string ClientIpHeader = "X-Forwarded-For";

    // Sent once right after a socket joins so a client can detect a narrower relay before
    // relying on a behavior it doesn't have. Not an MpMessage: the relay reads no message
    // bodies beyond the host's relayControl frames.
    private const int RelayVersion = RelayWire.Version;
    private static readonly string[] RelayCapabilities = ["binaryCompression", "authenticatedIdentity", "roomModeration"];
    private static readonly string RelayCapabilitiesJson = string.Join(",", RelayCapabilities.Select(c => $"\"{c}\""));

    // The relay owns the room namespace, so it's the only party that can guarantee no
    // collision (vs. a client picking one locally and hoping). Hosting goes through /host
    // (no code in the URL); the relay picks a free one and hands it back in the greeting.
    // Cryptographically random (not System.Random) -- on a public relay, a stranger who's
    // observed a few issued codes must not be able to predict a future one and race the real
    // host into a session before they've even shared its code.
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I, 32 chars
    private const int CodeLength = 6;

    // A room idle this long is considered abandoned (crashed host, dead sockets that never
    // closed cleanly). Comfortably above MultiplayerManager's 2s ping, so any live host
    // keeps its room for free.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReapInterval = TimeSpan.FromSeconds(2);

    // How often ReapLoop also emits a summary line, independent of idle-session sweeps --
    // ambient "is this thing alive and how loaded" visibility in the console/journal without
    // needing the admin endpoint.
    private static readonly TimeSpan SummaryLogInterval = TimeSpan.FromMinutes(1);

    // ---- Live state ---------------------------------------------------------------------

    private sealed class PeerConn(WebSocket socket, uint id, IPAddress ip, Guid peerId)
    {
        public readonly WebSocket Socket = socket;
        public readonly Guid PeerId = peerId;
        // A WebSocket allows one send at a time, and a broadcast, a greeting and a close can all
        // target the same socket at once.
        public readonly SemaphoreSlim SendGate = new(1, 1);
        // Set once the greeting is out, so no room traffic can arrive ahead of it.
        public bool Ready;
        public readonly uint Id = id;
        public readonly IPAddress Ip = ip;
        public readonly DateTime JoinedUtc = DateTime.UtcNow;
        public long MessagesIn;
        public long BytesIn;
        public int PeakMessagesPerSecond;
        public long PeakBytesPerSecond;
        public bool NearLimitWarned;
    }

    private sealed class Room
    {
        public readonly List<PeerConn> Peers = new();
        // Guards this room's own Peers/LastActivityUtc only -- NOT the Sessions table below.
        // One lock per room (not one relay-wide lock) so unrelated sessions never contend with
        // each other on join/leave/broadcast; only two operations touching the SAME session
        // ever serialize against one another. See TryJoin/Leave for why Sessions removal also
        // has to happen while holding this lock, not the table's own (lock-free) operations.
        public readonly object Lock = new();
        // Whoever created the room, by authenticated identity rather than socket, so a host
        // that reconnects is the host again. Tagged onto every broadcast from them (see
        // BroadcastAsync) so a receiving client can tell a real host message from a forgery.
        public Guid HostPeerId;
        // Null address: banned while not connected. Filled in the next time that identity tries.
        public readonly Dictionary<Guid, IPAddress?> Bans = new();
        public DateTime LastActivityUtc = DateTime.UtcNow;
        public readonly DateTime CreatedUtc = DateTime.UtcNow;
    }

    private static readonly ConcurrentDictionary<string, Room> Sessions = new();
    private static int nextConnectionId;

    // Matches the hand-written camelCase of /info and the WS greeting, so every response this
    // relay serves is shaped the same way.
    internal static readonly JsonSerializerOptions AdminJson =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    // Per-IP abuse tracking, separate lock since it's touched on a different cadence
    // (every connection/join attempt) than the session table. Keys are AbuseKey(ip), not the
    // raw address -- see there.
    private static readonly Dictionary<IPAddress, int> ConnectionsByIp = new();
    private static readonly Dictionary<IPAddress, Queue<DateTime>> FailedJoinsByIp = new();
    private static readonly Dictionary<IPAddress, DateTime> JoinLockoutUntilByIp = new();
    private static readonly Dictionary<IPAddress, Queue<DateTime>> FailedAdminByIp = new();
    private static readonly Dictionary<IPAddress, DateTime> AdminLockoutUntilByIp = new();
    private static readonly HashSet<IPAddress> BannedIps = new();
    private static readonly object AbuseLock = new();

    // Admin kill switch: existing sessions keep running, nothing new is accepted.
    private static volatile bool acceptingConnections = true;

    // ---- Abuse-relevant counters, all lifetime totals exposed via /admin/stats. Interlocked,
    // not lock-guarded -- each is an independent running total, no cross-field consistency
    // needed. recentRejections resets every ReapInterval (see ReapLoop) and drives the alert
    // threshold above.
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;
    private static long totalConnectionsAccepted;
    private static long totalMessagesBroadcast;
    private static long totalBytesBroadcast;
    private static long recentRejections;
    private static long peakMessagesPerSecond, peakBytesPerSecond, nearLimitWarnings;
    private static long rejectedOrigin, rejectedIpCap, rejectedJoinLockout, rejectedRelayFull,
        rejectedSessionFull, rejectedSessionNotFound, rejectedBadToken, rejectedHandshakeTimeout,
        rejectedMessageTooLarge, rejectedMessageTimeout, rejectedMessageRate, rejectedByteRate,
        rejectedUnencrypted, rejectedTooManyFragments, rejectedBanned, rejectedPaused, rejectedBadProtocol;

    private static void CountRejection(ref long counter)
    {
        Interlocked.Increment(ref counter);
        Interlocked.Increment(ref recentRejections);
    }

    // For rejection reasons that otherwise leave no individual trace anywhere (only the
    // aggregate counter) -- logs one Detail line (file only; see RelayLog) per rejection so
    // "what happened to this one connection attempt" is answerable later, without spamming the
    // console for high-volume abuse (the [ALERT] summary in ReapLoop covers that).
    private static void CountRejection(ref long counter, string reason, IPAddress ip, string sessionTag)
    {
        CountRejection(ref counter);
        RelayLog.Detail($"[{sessionTag}] rejected ({reason}) from {ip}");
    }

    private static void RecordMax(ref long target, long value)
    {
        long seen;
        while (value > (seen = Interlocked.Read(ref target)))
            if (Interlocked.CompareExchange(ref target, value, seen) == seen) return;
    }

    private static async Task Main(string[] args)
    {
        if (args.Contains("--admin"))
        {
            await AdminConsole.RunAsync(args);
            return;
        }

        if (args.Contains("--session-log"))
        {
            var code = GetArg(args, "--session-log");
            var dir = GetArg(args, "--log-dir") ?? Path.Combine(AppContext.BaseDirectory, "logs");
            if (string.IsNullOrEmpty(code))
            {
                Console.Error.WriteLine("--session-log requires a session code, e.g. --session-log ABCD23");
                return;
            }
            RelayLog.PrintSessionLog(dir, code);
            return;
        }

        var port = int.TryParse(GetArg(args, "--port", "-p"), out var p) ? p : 7890;
        var bind = GetArg(args, "--bind") ?? "*";
        // Env var fallback, CLI flag wins if both are set -- a CLI arg is visible to any other
        // local user via a process listing (ps/tasklist) and often ends up in shell history;
        // an env var isn't immune to a sufficiently privileged local reader either, but doesn't
        // leak through either of those two common paths.
        AccessToken = NullIfEmpty(GetArg(args, "--token")) ?? NullIfEmpty(Environment.GetEnvironmentVariable("ANOMECH_RELAY_TOKEN"));
        AdminToken = NullIfEmpty(GetArg(args, "--admin-token")) ?? NullIfEmpty(Environment.GetEnvironmentVariable("ANOMECH_RELAY_ADMIN_TOKEN"));
        if (int.TryParse(GetArg(args, "--max-sessions"), out var mts)) MaxTotalSessions = mts;
        if (int.TryParse(GetArg(args, "--max-connections-per-ip"), out var mcpi)) MaxConnectionsPerIp = mcpi;
        if (long.TryParse(GetArg(args, "--max-message-bytes"), out var mmb)) MaxMessageBytes = mmb;
        if (int.TryParse(GetArg(args, "--max-failed-joins"), out var mfj)) MaxFailedJoinsPerWindow = mfj;
        if (int.TryParse(GetArg(args, "--max-peers-per-session"), out var mpps)) MaxPeersPerSession = mpps;
        if (int.TryParse(GetArg(args, "--max-messages-per-second"), out var mmps)) MaxMessagesPerSecond = mmps;
        if (long.TryParse(GetArg(args, "--max-bytes-per-second"), out var mbps)) MaxBytesPerSecond = mbps;
        if (int.TryParse(GetArg(args, "--max-fragments-per-message"), out var mfpm)) MaxFragmentsPerMessage = mfpm;
        if (double.TryParse(GetArg(args, "--usage-warn-fraction"), out var uwf) && uwf is > 0 and <= 1) UsageWarnFraction = uwf;
        if (GetArg(args, "--client-ip-header") is { Length: > 0 } cih) ClientIpHeader = cih;
        RequireTls = args.Contains("--require-tls");
        foreach (var cidr in GetArgs(args, "--trusted-proxy"))
        {
            if (TryParseNetwork(cidr, out var network)) TrustedProxies.Add(network);
            else { Console.Error.WriteLine($"--trusted-proxy: '{cidr}' isn't a valid CIDR (e.g. 127.0.0.1/32 or ::1/128)."); return; }
        }

        // Refuse to start rather than just warn -- same "enforce, don't just recommend"
        // stance as the TLS requirement below. A short token is still guessable within the
        // lockout's own budget given enough patience or rotating IPs; there's no usability
        // cost to requiring length here since this is a generated secret, not a memorized one.
        const int minTokenLength = 16;
        if (AccessToken is { Length: < minTokenLength } || AdminToken is { Length: < minTokenLength })
        {
            Console.Error.WriteLine($"--token/--admin-token must be at least {minTokenLength} characters -- " +
                                     "a short shared secret is still guessable over time even with the lockout in place. " +
                                     "Generate one with e.g. `openssl rand -hex 16`.");
            return;
        }

        // TLS here always means a reverse proxy terminating it, so "is this encrypted" is only
        // ever answerable from a header -- and a header is only evidence if the request came
        // from a proxy we were told to trust. Starting without that mapping would mean either
        // believing the header from anyone (spoofable) or rejecting every connection.
        if ((RequireTls || AccessToken != null || AdminToken != null) && TrustedProxies.Count == 0)
        {
            Console.Error.WriteLine("A token or --require-tls is set, so TLS is enforced -- which needs --trusted-proxy " +
                                     "<cidr> naming the reverse proxy that terminates it (e.g. --trusted-proxy 127.0.0.1/32 " +
                                     "for a local Caddy/nginx). Without it, X-Forwarded-Proto could be spoofed by anyone " +
                                     "who can reach this port directly. See Relay/README.md.");
            return;
        }

        // Configured before the listener even tries to bind, so a bind failure still gets a
        // file record -- useful under systemd/journald where the console output of a crashed
        // service is easy to lose. Defaults to a directory next to the executable (not the
        // working directory, which varies by how the process was launched).
        if (!args.Contains("--no-file-log"))
        {
            var logDir = GetArg(args, "--log-dir") ?? Path.Combine(AppContext.BaseDirectory, "logs");
            var maxLogBytes = long.TryParse(GetArg(args, "--log-max-bytes"), out var mlb) ? mlb : 5L * 1024 * 1024 * 1024;
            RelayLog.Configure(logDir, maxLogBytes);
            // Log writes are buffered and flushed roughly once a second (see RelayLog) -- catch
            // a graceful shutdown (systemd stop, Ctrl+C) so the last stretch isn't silently lost.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RelayLog.FlushOnShutdown();
            RelayLog.Info($"[AnoMech.Relay] Logging to {logDir} (compressed, capped at {maxLogBytes / (1024.0 * 1024 * 1024):F1} GB). " +
                           "Use --session-log <code> to read back one session's lines.");
        }

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://{bind}:{port}/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException e)
        {
            RelayLog.Warn($"Failed to bind {bind}:{port}: {e.Message}");
            RelayLog.Warn("On Windows, binding a non-loopback prefix needs either an admin " +
                           "process or a URL ACL grant: netsh http add urlacl url=http://+:" +
                           $"{port}/ user=Everyone");
            return;
        }

        RelayLog.Info($"[AnoMech.Relay] Listening on {bind}:{port}. Host a session at /host, join one at /session/<code>.");
        RelayLog.Info($"[AnoMech.Relay] Access token: {(AccessToken != null ? "required" : "not set -- anyone can connect")}. " +
                      $"Admin endpoint: {(AdminToken != null ? "enabled" : "disabled (no --admin-token)")}.");
        RelayLog.Info(TrustedProxies.Count == 0
            ? "[AnoMech.Relay] No --trusted-proxy set: client addresses are read from the transport only, and forwarded headers are ignored."
            : $"[AnoMech.Relay] Trusting {ClientIpHeader} / X-Forwarded-Proto from: {string.Join(", ", TrustedProxies)}.");
        RelayLog.Info($"[AnoMech.Relay] Per-connection caps: {MaxMessagesPerSecond} msg/s, " +
                      $"{MaxBytesPerSecond / (1024.0 * 1024):F1} MB/s, {MaxMessageBytes / 1024} KB/message. " +
                      $"Logging a [NEAR-LIMIT] line past {UsageWarnFraction:P0} of either rate.");
        if (RequireTls || AccessToken != null || AdminToken != null)
            RelayLog.Info($"[AnoMech.Relay] {(RequireTls ? "--require-tls is set" : "A token is set")}, so every connection now " +
                          "REQUIRES a TLS-terminating reverse proxy in front (X-Forwarded-Proto: https) -- see " +
                          "Relay/README.md. Unencrypted connections will be rejected with 426, including plain local testing.");
        _ = ReapLoop();

        while (true)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch (Exception e)
            {
                // Any exception here used to be assumed to mean "listener.Stop() was called" --
                // but that's only true for HttpListenerException specifically. Anything else
                // (an unexpected edge case triggered by one malformed request) would otherwise
                // propagate out of Main uncaught and take the whole process down for every
                // connected session at once. IsListening is the actual "was this a real
                // shutdown" signal.
                if (!listener.IsListening) break;
                RelayLog.Warn($"[AnoMech.Relay] Unexpected error accepting a connection: {e.Message} -- continuing.");
                continue;
            }
            _ = HandleConnectionAsync(ctx);
        }
    }

    private static string? GetArg(string[] args, params string[] names)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (names.Contains(args[i]))
                return args[i + 1];
        return null;
    }

    private static IEnumerable<string> GetArgs(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                yield return args[i + 1];
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static bool TryParseNetwork(string cidr, out IPNetwork network)
    {
        if (IPNetwork.TryParse(cidr, out network))
        {
            // Stored in the form NormalizeIp gives the addresses it is matched against.
            if (!network.BaseAddress.IsIPv4MappedToIPv6) return true;
            if (network.PrefixLength < 96) return false;
            network = new IPNetwork(network.BaseAddress.MapToIPv4(), network.PrefixLength - 96);
            return true;
        }
        // A bare address is the common case for a local proxy; treat it as a single host.
        if (IPAddress.TryParse(cidr, out var single))
        {
            single = NormalizeIp(single);
            network = new IPNetwork(single, single.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32);
            return true;
        }
        return false;
    }

    // ---- Client identity ----------------------------------------------------------------

    // A dual-stack listener reports IPv4 clients as ::ffff:a.b.c.d, which AbuseKey's /64 would
    // otherwise lump into one bucket shared by every IPv4 client.
    private static IPAddress NormalizeIp(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    private static bool IsTrustedProxy(IPAddress ip) => TrustedProxies.Any(n => n.Contains(NormalizeIp(ip)));

    // The address every abuse control is keyed on. Behind a reverse proxy the transport
    // address is the proxy's, which would collapse every per-IP cap and lockout into one
    // shared bucket -- so a forwarded header is consulted, but ONLY when the request actually
    // came from a configured proxy. The rightmost untrusted entry is the one the trusted hop
    // observed directly; anything further left was written by the client and is forgeable.
    private static IPAddress ResolveClientIp(HttpListenerContext ctx)
    {
        var transport = NormalizeIp(ctx.Request.RemoteEndPoint?.Address ?? IPAddress.None);
        if (!IsTrustedProxy(transport)) return transport;
        var header = ctx.Request.Headers[ClientIpHeader];
        if (string.IsNullOrEmpty(header)) return transport;
        var parts = header.Split(',');
        for (var i = parts.Length - 1; i >= 0; i--)
            if (TryParseForwardedAddress(parts[i], out var candidate) && !IsTrustedProxy(candidate))
                return NormalizeIp(candidate);
        return transport;
    }

    private static bool TryParseForwardedAddress(string raw, out IPAddress address)
    {
        var text = raw.Trim();
        if (IPAddress.TryParse(text, out address!)) return true;
        // "[::1]:1234" and "10.0.0.1:1234" both appear in the wild.
        if (text.StartsWith('[') && text.IndexOf(']') is var close && close > 0)
            return IPAddress.TryParse(text[1..close], out address!);
        var colon = text.LastIndexOf(':');
        return colon > 0 && IPAddress.TryParse(text[..colon], out address!);
    }

    // IPv6 is handed out in blocks, so a per-address key would let anyone with a /64 (the
    // standard VPS allocation) sidestep every cap by rotating the low bits.
    private static IPAddress AbuseKey(IPAddress ip)
    {
        ip = NormalizeIp(ip);
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip;
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes);
    }

    // The relay has no TLS of its own -- wss:// is always a reverse proxy terminating TLS in
    // front of us (see README), so X-Forwarded-Proto is the only signal we have for "was the
    // real client connection actually encrypted", and it counts as a signal only when the
    // request reached us from a proxy we were configured to trust.
    private static bool IsRequestEncrypted(HttpListenerContext ctx)
    {
        var transport = NormalizeIp(ctx.Request.RemoteEndPoint?.Address ?? IPAddress.None);
        var forwardedProto = ctx.Request.Headers["X-Forwarded-Proto"];
        // A loopback request that wasn't forwarded never left the machine, so there is nothing
        // for TLS to protect -- this is what lets the admin CLI reach a local relay. An
        // attacker can't produce a loopback transport address, and a proxy (which is itself
        // usually on loopback) always sets the header, so it falls through to the check below.
        if (string.IsNullOrEmpty(forwardedProto) && IPAddress.IsLoopback(transport)) return true;
        return IsTrustedProxy(transport)
               && string.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase);
    }

    // ---- Connection handling -------------------------------------------------------------

    private static async Task HandleConnectionAsync(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath.Trim('/') ?? "";

        // Plain-HTTP endpoints, no WebSocket upgrade -- handled entirely separately from the
        // relay/join flow below.
        if (!ctx.Request.IsWebSocketRequest)
        {
            switch (path)
            {
                case "info": await ServeInfoAsync(ctx); return;
                case "admin/stats": await ServeAdminAsync(ctx, _ => JsonSerializer.Serialize(BuildAdminStats(), AdminJson)); return;
                case "admin/sessions": await ServeAdminAsync(ctx, _ => JsonSerializer.Serialize(BuildSessionList(), AdminJson)); return;
                case "admin/action": await ServeAdminAsync(ctx, ReadAndApplyAdminAction); return;
            }
        }

        var isHostRequest = path == "host";
        var joinCode = isHostRequest ? null : ExtractSessionCode(path);
        if ((!isHostRequest && joinCode is null) || !ctx.Request.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        var ip = ResolveClientIp(ctx);
        var key = AbuseKey(ip);
        // Best available session identity before a room necessarily exists yet -- lets a
        // rejection that happens pre-join (bad token, IP cap, lockout...) still show up under
        // --session-log for the code someone was trying to reach. "host" requests don't have
        // a real code to attach to until TryCreateSession succeeds.
        var sessionTag = isHostRequest ? "host" : joinCode!;

        if (!acceptingConnections)
        {
            CountRejection(ref rejectedPaused, "relay paused by admin", ip, sessionTag);
            ctx.Response.StatusCode = 503;
            ctx.Response.Close();
            return;
        }

        if (IsBanned(key))
        {
            CountRejection(ref rejectedBanned, "banned", ip, sessionTag);
            ctx.Response.StatusCode = 403;
            ctx.Response.Close();
            return;
        }

        // A password is worthless if it's sent in the clear -- once one is set, every
        // connection must be TLS-terminated in front of us (see IsRequestEncrypted).
        // --require-tls forces the same regardless, even with no token at all.
        if ((RequireTls || AccessToken != null) && !IsRequestEncrypted(ctx))
        {
            CountRejection(ref rejectedUnencrypted, "unencrypted", ip, sessionTag);
            ctx.Response.StatusCode = 426; // Upgrade Required
            ctx.Response.Close();
            return;
        }

        // Real Dalamud clients never send Origin; a browser tab always does. Rejecting it
        // outright closes off drive-by abuse from an arbitrary webpage with no allow-list to maintain.
        if (!string.IsNullOrEmpty(ctx.Request.Headers["Origin"]))
        {
            CountRejection(ref rejectedOrigin, "origin header present", ip, sessionTag);
            ctx.Response.StatusCode = 403;
            ctx.Response.Close();
            return;
        }

        // One shared lockout bucket for session-code guesses and --token guesses -- both cost
        // the same budget. Checked before the token comparison itself so a locked-out address
        // can't keep spending CPU on repeated guesses in the meantime.
        if (IsLockedOut(JoinLockoutUntilByIp, key))
        {
            CountRejection(ref rejectedJoinLockout, "auth locked out", ip, sessionTag);
            ctx.Response.StatusCode = 429;
            ctx.Response.Close();
            return;
        }

        if (AccessToken != null && !IsValidToken(ctx.Request.Headers["X-AnoMech-Relay-Token"], AccessToken))
        {
            CountRejection(ref rejectedBadToken, "bad token", ip, sessionTag);
            RecordFailedAuth(FailedJoinsByIp, JoinLockoutUntilByIp, key, "join");
            ctx.Response.StatusCode = 401;
            ctx.Response.Close();
            return;
        }

        if (AuthenticatePeer(ctx.Request.Headers["X-AnoMech-Protocol"], ctx.Request.Headers["X-AnoMech-Peer-Secret"]) is not { } authenticatedPeerId)
        {
            // File-only like other per-request refusals: anything can send a bare upgrade request.
            CountRejection(ref rejectedBadProtocol, "missing or wrong protocol version or peer credential; the plugin and relay need to be the same version", ip, sessionTag);
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        if (!TryReserveConnectionSlot(key))
        {
            CountRejection(ref rejectedIpCap, "ip connection cap", ip, sessionTag);
            ctx.Response.StatusCode = 429;
            ctx.Response.Close();
            return;
        }

        WebSocket socket;
        try
        {
            var acceptTask = ctx.AcceptWebSocketAsync(subProtocol: null);
            if (await Task.WhenAny(acceptTask, Task.Delay(HandshakeTimeout)) != acceptTask)
            {
                CountRejection(ref rejectedHandshakeTimeout);
                RelayLog.Warn($"[{sessionTag}] WebSocket handshake from {ip} stalled past {HandshakeTimeout.TotalSeconds:F0}s -- abandoning.");
                ReleaseConnectionSlot(key);
                try { ctx.Response.Abort(); } catch { /* best effort */ }
                return;
            }
            socket = (await acceptTask).WebSocket;
        }
        catch (Exception e)
        {
            RelayLog.Warn($"[{sessionTag}] WebSocket handshake from {ip} failed: {e.Message}");
            ReleaseConnectionSlot(key);
            return;
        }

        Interlocked.Increment(ref totalConnectionsAccepted);
        var peer = new PeerConn(socket, (uint)Interlocked.Increment(ref nextConnectionId), ip, authenticatedPeerId);

        // The slot is released here and nowhere else, so no path between "reserved" and
        // "socket finished" can leak it -- including the rejection closes below, which each
        // wait on a close handshake the peer is free never to answer.
        try
        {
            string sessionCode;
            if (isHostRequest)
            {
                if (!TryCreateSession(peer, out sessionCode!))
                {
                    CountRejection(ref rejectedRelayFull, "relay full", ip, sessionTag);
                    await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, "relay full");
                    return;
                }
            }
            else
            {
                sessionCode = joinCode!;
                if (!TryJoin(sessionCode, peer, out var reason))
                {
                    // Only "not found" is a guessing signal. A full room or a room ban both mean
                    // the code was right, and charging those toward the lockout would lock a
                    // whole household out of the relay over a banned player's retries.
                    if (reason == "session not found")
                    {
                        CountRejection(ref rejectedSessionNotFound, reason, ip, sessionTag);
                        RecordFailedAuth(FailedJoinsByIp, JoinLockoutUntilByIp, key, "join");
                    }
                    else CountRejection(ref reason == "banned from room" ? ref rejectedBanned : ref rejectedSessionFull, reason, ip, sessionTag);
                    await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, reason);
                    return;
                }
            }

            try
            {
                await RunPeerAsync(peer, sessionCode, isHostRequest);
            }
            finally
            {
                Leave(sessionCode, peer);
                RelayLog.Info($"[{sessionCode}] peer #{peer.Id} left ({CountPeers(sessionCode)} connected, " +
                              $"{peer.MessagesIn} msgs / {peer.BytesIn / 1024} KB in, peak {peer.PeakMessagesPerSecond} msg/s " +
                              $"/ {peer.PeakBytesPerSecond / 1024} KB/s)");
            }
        }
        finally
        {
            socket.Dispose();
            ReleaseConnectionSlot(key);
        }
    }

    private static async Task RunPeerAsync(PeerConn peer, string sessionCode, bool isHostRequest)
    {
        var socket = peer.Socket;
        try
        {
            RelayLog.Info($"[{sessionCode}] {(isHostRequest ? "session created" : "peer joined")} as #{peer.Id} from {peer.Ip} ({CountPeers(sessionCode)} connected)");
            await SendGreetingAsync(peer, isHostRequest ? sessionCode : null);
            if (!Sessions.TryGetValue(sessionCode, out var joinedRoom)) return;
            lock (joinedRoom.Lock)
            {
                if (!Sessions.TryGetValue(sessionCode, out var live) || !ReferenceEquals(live, joinedRoom)
                    || !joinedRoom.Peers.Contains(peer) || socket.State != WebSocketState.Open) return;
                peer.Ready = true;
            }

            // A large message (e.g. WorldSnapshotMessage) can arrive split across several
            // frames; buffer until EndOfMessage before forwarding, or fragments get broadcast
            // standalone and peers see truncated/corrupt JSON once a snapshot outgrows one frame.
            var readBuffer = new byte[16 * 1024];
            using var messageBuffer = new MemoryStream();
            // Per-connection, not per-IP -- a single connection sending far faster than any
            // legitimate client gets cut off regardless of which address it's coming from.
            var recentMessageTimes = new Queue<DateTime>();
            var recentBytes = new Queue<(DateTime At, int Bytes)>();
            long recentByteSum = 0;
            while (socket.State == WebSocketState.Open)
            {
                messageBuffer.SetLength(0);
                // Armed only once the first fragment lands: this bounds how long a message may
                // take to finish arriving, not how long a connection may sit idle between
                // messages (which is what IdleTimeout is for, at the room level).
                using var messageCts = new CancellationTokenSource();
                WebSocketReceiveResult result;
                var fragmentCount = 0;
                try
                {
                    do
                    {
                        result = await socket.ReceiveAsync(readBuffer, messageCts.Token);
                        if (fragmentCount == 0) messageCts.CancelAfter(MessageAssemblyTimeout);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ClosePeerAsync(peer, WebSocketCloseStatus.NormalClosure, null, linger: false);
                            return;
                        }
                        messageBuffer.Write(readBuffer, 0, result.Count);
                        if (messageBuffer.Length > MaxMessageBytes)
                        {
                            CountRejection(ref rejectedMessageTooLarge);
                            RelayLog.Warn($"[{sessionCode}] Message from {peer.Ip} exceeded {MaxMessageBytes} bytes -- aborting.");
                            socket.Abort();
                            return;
                        }
                        // Bounds fragment COUNT, not just total bytes -- a real client never
                        // sends anywhere near this many frames for one message; this only
                        // catches something deliberately splitting a message into many tiny
                        // frames to burn ReceiveAsync round-trips while staying under the size cap.
                        if (++fragmentCount > MaxFragmentsPerMessage)
                        {
                            CountRejection(ref rejectedTooManyFragments);
                            RelayLog.Warn($"[{sessionCode}] Message from {peer.Ip} exceeded {MaxFragmentsPerMessage} fragments -- aborting.");
                            socket.Abort();
                            return;
                        }
                    } while (!result.EndOfMessage);
                }
                // Only our own deadline is a slow message: an abort from elsewhere (a kick, a ban,
                // or the same identity reconnecting) cancels the receive too.
                catch (OperationCanceledException) when (messageCts.IsCancellationRequested)
                {
                    CountRejection(ref rejectedMessageTimeout);
                    RelayLog.Warn($"[{sessionCode}] Message from {peer.Ip} took over {MessageAssemblyTimeout.TotalSeconds:F0}s to arrive -- aborting.");
                    socket.Abort();
                    return;
                }

                var messageBytes = messageBuffer.ToArray();
                var now = DateTime.UtcNow;
                var cutoff = now - TimeSpan.FromSeconds(1);

                recentMessageTimes.Enqueue(now);
                while (recentMessageTimes.Count > 0 && recentMessageTimes.Peek() < cutoff)
                    recentMessageTimes.Dequeue();
                recentBytes.Enqueue((now, messageBytes.Length));
                recentByteSum += messageBytes.Length;
                while (recentBytes.Count > 0 && recentBytes.Peek().At < cutoff)
                    recentByteSum -= recentBytes.Dequeue().Bytes;

                peer.MessagesIn++;
                peer.BytesIn += messageBytes.Length;
                if (recentMessageTimes.Count > peer.PeakMessagesPerSecond) peer.PeakMessagesPerSecond = recentMessageTimes.Count;
                if (recentByteSum > peer.PeakBytesPerSecond) peer.PeakBytesPerSecond = recentByteSum;
                RecordMax(ref peakMessagesPerSecond, recentMessageTimes.Count);
                RecordMax(ref peakBytesPerSecond, recentByteSum);

                if (recentMessageTimes.Count > MaxMessagesPerSecond)
                {
                    CountRejection(ref rejectedMessageRate);
                    RelayLog.Warn($"[{sessionCode}] #{peer.Id} {peer.Ip} exceeded {MaxMessagesPerSecond} messages/sec -- aborting.");
                    socket.Abort();
                    return;
                }
                if (recentByteSum > MaxBytesPerSecond)
                {
                    CountRejection(ref rejectedByteRate);
                    RelayLog.Warn($"[{sessionCode}] #{peer.Id} {peer.Ip} exceeded {MaxBytesPerSecond / (1024 * 1024)} MB/sec -- aborting.");
                    socket.Abort();
                    return;
                }
                WarnIfNearLimit(peer, sessionCode, recentMessageTimes.Count, recentByteSum);

                // The one body the relay reads: a small, uncompressed control frame, obeyed only
                // from the room's host. Everything else is forwarded exactly as sent.
                if (result.MessageType == WebSocketMessageType.Text && RelayWire.IsControl(messageBytes))
                {
                    Moderate(sessionCode, peer, messageBytes);
                    continue;
                }
                var reachedPeers = await BroadcastAsync(sessionCode, peer, messageBytes, result.MessageType);
                // File-only (see RelayLog.Detail) -- this is the highest-volume event the relay
                // sees, and console-echoing it would drown out everything else. Never the
                // message body itself, only shape/size/routing, matching the relay's "we don't
                // log what you said" stance (see README's Security notes).
                RelayLog.Detail($"[{sessionCode}] broadcast from #{peer.Id} {peer.Ip} type={result.MessageType} bytes={messageBytes.Length} " +
                                 $"fragments={fragmentCount} rate={recentMessageTimes.Count}/s,{recentByteSum}B/s -> {reachedPeers} peer(s)");
            }
        }
        catch (WebSocketException)
        {
            // Peer dropped without a clean close handshake -- caller's finally runs Leave.
        }
        catch (OperationCanceledException)
        {
            // Aborted (kick, shutdown) -- same.
        }
    }

    // One line per connection per threshold crossing, not per message: the point is to notice
    // that a real scenario is creeping toward a cap, which a per-message line would bury.
    private static void WarnIfNearLimit(PeerConn peer, string sessionCode, int messagesPerSecond, long bytesPerSecond)
    {
        if (peer.NearLimitWarned) return;
        var msgFraction = messagesPerSecond / (double)MaxMessagesPerSecond;
        var byteFraction = bytesPerSecond / (double)MaxBytesPerSecond;
        if (msgFraction < UsageWarnFraction && byteFraction < UsageWarnFraction) return;
        peer.NearLimitWarned = true;
        Interlocked.Increment(ref nearLimitWarnings);
        RelayLog.Warn($"[NEAR-LIMIT] [{sessionCode}] #{peer.Id} {peer.Ip} reached {messagesPerSecond} msg/s ({msgFraction:P0} of cap) " +
                      $"and {bytesPerSecond / 1024} KB/s ({byteFraction:P0} of cap). Raise --max-messages-per-second / " +
                      "--max-bytes-per-second if this is a legitimate run.");
    }

    // Path is already trimmed of leading/trailing slashes -- see HandleConnectionAsync.
    // Validated against the real alphabet/length, not just a length ceiling, so scanner/bot
    // garbage gets rejected as a bad request instead of doing a session lookup at all.
    private static string? ExtractSessionCode(string path)
    {
        var parts = path.Split('/');
        if (parts.Length != 2 || parts[0] != "session") return null;
        var code = parts[1];
        return code.Length == CodeLength && code.All(CodeAlphabet.Contains) ? code : null;
    }

    // Timing-safe: a naive string comparison returns early on the first mismatched byte,
    // which lets a remote attacker recover the token one byte at a time from response timing.
    private static bool IsValidToken(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        // Compare a fixed-size hash of each instead of the raw (different-length) values --
        // FixedTimeEquals itself requires equal-length inputs, and short-circuiting on a
        // length check first would leak length the same way a naive compare leaks content.
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(a), SHA256.HashData(b));
    }

    // ---- Abuse bookkeeping ----------------------------------------------------------------

    private static bool TryReserveConnectionSlot(IPAddress key)
    {
        lock (AbuseLock)
        {
            var count = ConnectionsByIp.GetValueOrDefault(key);
            if (count >= MaxConnectionsPerIp) return false;
            ConnectionsByIp[key] = count + 1;
            return true;
        }
    }

    private static void ReleaseConnectionSlot(IPAddress key)
    {
        lock (AbuseLock)
        {
            if (!ConnectionsByIp.TryGetValue(key, out var count)) return;
            if (count <= 1) ConnectionsByIp.Remove(key);
            else ConnectionsByIp[key] = count - 1;
        }
    }

    private static bool IsBanned(IPAddress key)
    {
        lock (AbuseLock) return BannedIps.Contains(key);
    }

    private static bool IsLockedOut(Dictionary<IPAddress, DateTime> table, IPAddress key)
    {
        lock (AbuseLock)
            return table.TryGetValue(key, out var until) && until > DateTime.UtcNow;
    }

    // Sliding window of recent failures; crossing the threshold inside it trips a lockout.
    private static void RecordFailedAuth(Dictionary<IPAddress, Queue<DateTime>> attemptsTable,
                                         Dictionary<IPAddress, DateTime> lockoutTable, IPAddress key, string kind)
    {
        var justTripped = false;
        lock (AbuseLock)
        {
            if (!attemptsTable.TryGetValue(key, out var attempts))
                attemptsTable[key] = attempts = new Queue<DateTime>();
            var now = DateTime.UtcNow;
            attempts.Enqueue(now);
            while (attempts.Count > 0 && now - attempts.Peek() > FailedJoinWindow) attempts.Dequeue();
            if (attempts.Count >= MaxFailedJoinsPerWindow)
            {
                justTripped = !(lockoutTable.TryGetValue(key, out var until) && until > now);
                lockoutTable[key] = now + JoinLockoutDuration;
            }
        }
        // Logged once at the moment it trips, not on every renewal while already locked out --
        // a real lockout is rare and worth an operator's attention live; a locked-out address
        // still hammering the endpoint is not new information.
        if (justTripped)
            RelayLog.Warn($"[AnoMech.Relay] {key} locked out of {kind} for {JoinLockoutDuration.TotalMinutes:F0}m after " +
                          $"{MaxFailedJoinsPerWindow}+ failed attempts in {FailedJoinWindow.TotalSeconds:F0}s.");
    }

    // ---- Room membership -------------------------------------------------------------------

    // Peer-join only -- a code nobody actually hosted is rejected immediately instead of
    // silently vivifying an empty room. Loops rather than a single TryGetValue+lock because
    // Leave() can retire (empty + remove) this exact room between the lookup and acquiring its
    // lock; the re-check inside the lock catches that race and retries against whatever's
    // actually current instead of joining a room that's already been thrown away.
    // The connection's identity is derived from a secret only the client holds, so a public id
    // seen in room traffic can't be used to act as someone else.
    private static Guid? AuthenticatePeer(string? protocolHeader, string? secretHeader)
        => protocolHeader == RelayVersion.ToString() && RelayWire.IsValidSecret(secretHeader) ? RelayWire.PeerId(secretHeader!) : null;

    private static bool TryJoin(string sessionCode, PeerConn peer, out string reason)
    {
        while (true)
        {
            if (!Sessions.TryGetValue(sessionCode, out var room))
            {
                reason = "session not found";
                return false;
            }
            PeerConn? replaced = null;
            List<PeerConn>? evicted = null;
            PeerConn? host = null;
            var banned = false;
            lock (room.Lock)
            {
                if (!Sessions.TryGetValue(sessionCode, out var current) || !ReferenceEquals(current, room))
                    continue; // retired (or replaced) concurrently -- retry against the live one
                // The host is exempt so banning someone on its own network can't lock it out.
                if (peer.PeerId != room.HostPeerId
                    && (room.Bans.ContainsKey(peer.PeerId) || room.Bans.Values.Contains(AbuseKey(peer.Ip))))
                {
                    banned = true;
                    // A ban made while this identity was disconnected learns its address now, and
                    // clears that address the way a ban on a connected player would have.
                    if (room.Bans.TryGetValue(peer.PeerId, out var bannedAddress) && bannedAddress is null)
                    {
                        var address = AbuseKey(peer.Ip);
                        room.Bans[peer.PeerId] = address;
                        evicted = room.Peers.Where(p => p.PeerId != room.HostPeerId && AbuseKey(p.Ip).Equals(address)).ToList();
                        foreach (var other in evicted)
                        {
                            other.Ready = false;
                            room.Peers.Remove(other);
                        }
                        host = room.Peers.FirstOrDefault(p => p.PeerId == room.HostPeerId);
                    }
                }
                else
                {
                    // One connection per identity: a reconnect replaces a connection that hasn't
                    // noticed it's dead yet, the host's included.
                    replaced = room.Peers.FirstOrDefault(p => p.PeerId == peer.PeerId);
                    if (room.Peers.Count - (replaced == null ? 0 : 1) >= MaxPeersPerSession)
                    {
                        reason = "session full";
                        return false;
                    }
                    if (replaced != null)
                    {
                        replaced.Ready = false;
                        room.Peers.Remove(replaced);
                    }
                    room.Peers.Add(peer);
                    room.LastActivityUtc = DateTime.UtcNow;
                }
            }
            if (banned)
            {
                if (evicted is { Count: > 0 })
                {
                    foreach (var other in evicted)
                        _ = ClosePeerAsync(other, WebSocketCloseStatus.PolicyViolation, "banned from room");
                    if (host != null) _ = SendNoticeAsync(host, evicted.Select(p => p.PeerId).Distinct().ToArray());
                }
                reason = "banned from room";
                return false;
            }
            replaced?.Socket.Abort();
            reason = "";
            return true;
        }
    }

    private static bool TryCreateSession(PeerConn host, out string sessionCode)
    {
        // A soft cap now, not a hard one -- a burst of concurrent /host requests right at the
        // ceiling could transiently overshoot it by a few. MaxTotalSessions exists to bound
        // resource usage, not as a security invariant, so this is an acceptable trade for not
        // needing a relay-wide lock on every session creation.
        if (Sessions.Count >= MaxTotalSessions)
        {
            sessionCode = "";
            return false;
        }
        var room = new Room { HostPeerId = host.PeerId };
        room.Peers.Add(host);
        string code;
        do { code = GenerateCode(); } while (!Sessions.TryAdd(code, room));
        sessionCode = code;
        return true;
    }

    private static string GenerateCode()
    {
        var chars = new char[CodeLength];
        // CodeAlphabet.Length (32) divides 256 evenly, so byte % 32 is exactly uniform --
        // no rejection sampling needed.
        var bytes = RandomNumberGenerator.GetBytes(CodeLength);
        for (var i = 0; i < CodeLength; i++)
            chars[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        return new string(chars);
    }

    private static void Leave(string sessionCode, PeerConn peer)
    {
        if (!Sessions.TryGetValue(sessionCode, out var room)) return;
        // Removal from Sessions happens while still holding this room's own lock -- the one
        // point that has to agree with TryJoin's re-check above, so a peer can never be added
        // to a room in the instant between it going empty and being removed from the table.
        lock (room.Lock)
        {
            peer.Ready = false;
            room.Peers.Remove(peer);
            if (room.Peers.Count == 0) Sessions.TryRemove(new KeyValuePair<string, Room>(sessionCode, room));
        }
    }

    private static int CountPeers(string sessionCode)
    {
        if (!Sessions.TryGetValue(sessionCode, out var room)) return 0;
        lock (room.Lock) return room.Peers.Count;
    }

    // peerId tells the client which identity the relay derived from its secret.
    private static async Task SendGreetingAsync(PeerConn peer, string? assignedSessionCode)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            relayVersion = RelayVersion, capabilities = RelayCapabilities,
            sessionCode = assignedSessionCode, peerId = peer.PeerId,
        });
        using var timeout = new CancellationTokenSource(SendTimeout);
        await SendOneAsync(peer, bytes, WebSocketMessageType.Text, timeout.Token);
    }

    private const int MaxRoomBans = 1024;

    // A malformed command is ignored rather than closing the sender: the sender is the host.
    private static void Moderate(string sessionCode, PeerConn sender, byte[] message)
    {
        string? operation;
        var id = Guid.Empty;
        try
        {
            using var json = JsonDocument.Parse(message);
            if (!json.RootElement.TryGetProperty("Operation", out var action) || action.ValueKind != JsonValueKind.String
                || !json.RootElement.TryGetProperty("PeerId", out var identity) || identity.ValueKind != JsonValueKind.String
                || !identity.TryGetGuid(out id)) return;
            operation = action.GetString();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return; }
        if (!Sessions.TryGetValue(sessionCode, out var room)) return;
        List<PeerConn> removed;
        lock (room.Lock)
        {
            if (!sender.Ready || !room.Peers.Contains(sender) || sender.PeerId != room.HostPeerId) return;
            if (operation == "unban") { room.Bans.Remove(id); return; }
            if (operation is not ("kick" or "ban") || id == room.HostPeerId) return;
            var target = room.Peers.FirstOrDefault(p => p.PeerId == id);
            // Recorded even when they aren't connected: a ban issued between their reconnects
            // still has to stop the next one. A full ban list still kicks.
            if (operation == "ban" && (room.Bans.ContainsKey(id) || room.Bans.Count < MaxRoomBans))
                room.Bans[id] = target is null ? room.Bans.GetValueOrDefault(id) : AbuseKey(target.Ip);
            if (target == null) return;
            removed = room.Peers.Where(p => ReferenceEquals(p, target)
                || (operation == "ban" && p.PeerId != room.HostPeerId && AbuseKey(p.Ip).Equals(AbuseKey(target.Ip)))).ToList();
            foreach (var peer in removed)
            {
                peer.Ready = false;
                room.Peers.Remove(peer);
            }
        }
        RelayLog.Info($"[{sessionCode}] host {operation} of {id}: {removed.Count} connection(s) closed.");
        foreach (var peer in removed)
            _ = ClosePeerAsync(peer, WebSocketCloseStatus.PolicyViolation, operation == "ban" ? "banned from room" : "kicked from room");
        // The host removed only the id it named. Anyone else the ban took with them leaves
        // without a word of their own, so without this they'd stay seated in its roster.
        var others = removed.Where(p => p.PeerId != id).Select(p => p.PeerId).Distinct().ToArray();
        if (others.Length > 0) _ = SendNoticeAsync(sender, others);
    }

    private static async Task SendNoticeAsync(PeerConn host, Guid[] removed)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { t = RelayWire.NoticeType, Removed = removed });
        using var timeout = new CancellationTokenSource(SendTimeout);
        await SendOneAsync(host, RelayWire.Envelope(false, 0, Guid.Empty, false, body), WebSocketMessageType.Binary, timeout.Token);
    }

    // ---- Public / admin HTTP ---------------------------------------------------------------

    // Unauthenticated by design -- a client must learn whether a token is needed BEFORE it
    // has one, or the plugin's "only show the password box if required" UI has no way to
    // decide. Nothing here is sensitive (it's the same info the WS greeting already sends,
    // minus a live session code).
    private static async Task ServeInfoAsync(HttpListenerContext ctx)
    {
        // /info itself carries no secret, but --require-tls means no exceptions -- keeps the
        // policy simple (nothing talks to this relay unencrypted) rather than case-by-case.
        if (RequireTls && !IsRequestEncrypted(ctx))
        {
            CountRejection(ref rejectedUnencrypted, "unencrypted", ResolveClientIp(ctx), "info");
            ctx.Response.StatusCode = 426;
            ctx.Response.Close();
            return;
        }
        var json = $$"""{"relayVersion":{{RelayVersion}},"capabilities":[{{RelayCapabilitiesJson}}],"requiresToken":{{(AccessToken != null ? "true" : "false")}}}""";
        await WriteJsonResponseAsync(ctx, json);
    }

    private static async Task ServeAdminAsync(HttpListenerContext ctx, Func<HttpListenerContext, string> handler)
    {
        // 404, not 401 -- a relay with no --admin-token set shouldn't even reveal these exist.
        if (AdminToken is null)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }
        var ip = ResolveClientIp(ctx);
        var key = AbuseKey(ip);
        if (!IsRequestEncrypted(ctx))
        {
            CountRejection(ref rejectedUnencrypted, "unencrypted", ip, "admin");
            ctx.Response.StatusCode = 426;
            ctx.Response.Close();
            return;
        }
        if (IsLockedOut(AdminLockoutUntilByIp, key))
        {
            CountRejection(ref rejectedJoinLockout, "admin locked out", ip, "admin");
            ctx.Response.StatusCode = 429;
            ctx.Response.Close();
            return;
        }
        if (!IsValidToken(ctx.Request.Headers["X-AnoMech-Admin-Token"], AdminToken))
        {
            CountRejection(ref rejectedBadToken, "bad admin token", ip, "admin");
            RecordFailedAuth(FailedAdminByIp, AdminLockoutUntilByIp, key, "admin");
            ctx.Response.StatusCode = 401;
            ctx.Response.Close();
            return;
        }
        string json;
        try
        {
            json = handler(ctx);
        }
        catch (Exception e)
        {
            RelayLog.Warn($"[admin] request from {ip} failed: {e.Message}");
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }
        await WriteJsonResponseAsync(ctx, json);
    }

    private static async Task WriteJsonResponseAsync(HttpListenerContext ctx, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        try
        {
            await ctx.Response.OutputStream.WriteAsync(bytes);
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    // ---- Admin model ------------------------------------------------------------------------

    internal sealed record RejectionCounts(
        long Origin, long IpCap, long JoinLockout, long RelayFull, long SessionFull,
        long SessionNotFound, long BadToken, long HandshakeTimeout, long MessageTooLarge, long MessageTimeout,
        long MessageRate, long ByteRate, long Unencrypted, long TooManyFragments, long Banned, long Paused,
        long BadProtocol);

    internal sealed record LimitSettings(
        int MaxPeersPerSession, int MaxTotalSessions, long MaxMessageBytes, int MaxConnectionsPerIp,
        int MaxMessagesPerSecond, long MaxBytesPerSecond, int MaxFragmentsPerMessage, int MaxFailedJoinsPerWindow,
        double UsageWarnFraction);

    internal sealed record AdminStats(
        double UptimeSeconds, int RelayVersion, int Sessions, int TotalPeers, int ConnectionsByIpCount,
        int ActiveJoinLockouts, int BannedIpCount, bool AcceptingConnections,
        long TotalConnectionsAccepted, long TotalMessagesBroadcast, long TotalBytesBroadcast,
        RejectionCounts Rejections, long RecentRejections,
        long PeakMessagesPerSecond, long PeakBytesPerSecond, long NearLimitWarnings, LimitSettings Limits,
        long MemoryBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections);

    internal sealed record PeerInfo(uint Id, string Ip, bool IsHost, double AgeSeconds, long MessagesIn, long BytesIn,
        int PeakMessagesPerSecond, long PeakBytesPerSecond);

    internal sealed record SessionInfo(string Code, double AgeSeconds, double IdleSeconds, List<PeerInfo> Peers);

    internal sealed record AdminActionRequest(string Action, string? SessionCode, uint? ConnectionId, string? Ip, string? Name, double? Value);

    internal sealed record AdminActionResult(bool Ok, string Message);

    private static LimitSettings BuildLimits() => new(
        MaxPeersPerSession, MaxTotalSessions, MaxMessageBytes, MaxConnectionsPerIp,
        MaxMessagesPerSecond, MaxBytesPerSecond, MaxFragmentsPerMessage, MaxFailedJoinsPerWindow, UsageWarnFraction);

    private static AdminStats BuildAdminStats()
    {
        var sessionCount = Sessions.Count;
        var totalPeers = Sessions.Values.Sum(r => { lock (r.Lock) return r.Peers.Count; });
        int ipCount, lockoutCount, bannedCount;
        lock (AbuseLock)
        {
            ipCount = ConnectionsByIp.Count;
            lockoutCount = JoinLockoutUntilByIp.Count(kv => kv.Value > DateTime.UtcNow);
            bannedCount = BannedIps.Count;
        }
        return new AdminStats(
            (DateTime.UtcNow - StartedAtUtc).TotalSeconds, RelayVersion, sessionCount, totalPeers, ipCount,
            lockoutCount, bannedCount, acceptingConnections,
            Interlocked.Read(ref totalConnectionsAccepted), Interlocked.Read(ref totalMessagesBroadcast), Interlocked.Read(ref totalBytesBroadcast),
            new RejectionCounts(
                Interlocked.Read(ref rejectedOrigin), Interlocked.Read(ref rejectedIpCap), Interlocked.Read(ref rejectedJoinLockout),
                Interlocked.Read(ref rejectedRelayFull), Interlocked.Read(ref rejectedSessionFull), Interlocked.Read(ref rejectedSessionNotFound),
                Interlocked.Read(ref rejectedBadToken), Interlocked.Read(ref rejectedHandshakeTimeout), Interlocked.Read(ref rejectedMessageTooLarge),
                Interlocked.Read(ref rejectedMessageTimeout), Interlocked.Read(ref rejectedMessageRate), Interlocked.Read(ref rejectedByteRate),
                Interlocked.Read(ref rejectedUnencrypted), Interlocked.Read(ref rejectedTooManyFragments),
                Interlocked.Read(ref rejectedBanned), Interlocked.Read(ref rejectedPaused), Interlocked.Read(ref rejectedBadProtocol)),
            Interlocked.Read(ref recentRejections),
            Interlocked.Read(ref peakMessagesPerSecond), Interlocked.Read(ref peakBytesPerSecond),
            Interlocked.Read(ref nearLimitWarnings), BuildLimits(),
            GC.GetTotalMemory(false), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }

    private static List<SessionInfo> BuildSessionList()
    {
        var now = DateTime.UtcNow;
        var list = new List<SessionInfo>();
        foreach (var (code, room) in Sessions)
        {
            lock (room.Lock)
            {
                list.Add(new SessionInfo(code, (now - room.CreatedUtc).TotalSeconds, (now - room.LastActivityUtc).TotalSeconds,
                    room.Peers.Select(peer => new PeerInfo(peer.Id, peer.Ip.ToString(),
                        peer.PeerId == room.HostPeerId, (now - peer.JoinedUtc).TotalSeconds,
                        peer.MessagesIn, peer.BytesIn, peer.PeakMessagesPerSecond, peer.PeakBytesPerSecond)).ToList()));
            }
        }
        return list.OrderBy(s => s.Code).ToList();
    }

    private static string ReadAndApplyAdminAction(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        var body = reader.ReadToEnd();
        var request = JsonSerializer.Deserialize<AdminActionRequest>(body, AdminJson)
                      ?? throw new InvalidOperationException("empty action body");
        var result = ApplyAdminAction(request);
        RelayLog.Warn($"[admin] {request.Action} from {ResolveClientIp(ctx)}: {result.Message}");
        return JsonSerializer.Serialize(result, AdminJson);
    }

    private static AdminActionResult ApplyAdminAction(AdminActionRequest request)
    {
        switch (request.Action)
        {
            case "kick-peer":
            {
                if (request.ConnectionId is not { } id) return new AdminActionResult(false, "kick-peer needs a connectionId");
                foreach (var (code, room) in Sessions)
                {
                    PeerConn? target;
                    lock (room.Lock) target = room.Peers.FirstOrDefault(peer => peer.Id == id);
                    if (target == null) continue;
                    target.Socket.Abort();
                    return new AdminActionResult(true, $"kicked #{id} ({target.Ip}) from {code}");
                }
                return new AdminActionResult(false, $"no live connection #{id}");
            }
            case "disband-session":
            {
                if (request.SessionCode is not { Length: > 0 } code) return new AdminActionResult(false, "disband-session needs a sessionCode");
                if (!Sessions.TryGetValue(code, out var room)) return new AdminActionResult(false, $"no session {code}");
                List<PeerConn> peers;
                lock (room.Lock)
                {
                    peers = new List<PeerConn>(room.Peers);
                    Sessions.TryRemove(new KeyValuePair<string, Room>(code, room));
                }
                foreach (var peer in peers) peer.Socket.Abort();
                return new AdminActionResult(true, $"disbanded {code} ({peers.Count} peers)");
            }
            case "ban-ip":
            case "unban-ip":
            {
                if (!IPAddress.TryParse(request.Ip, out var address)) return new AdminActionResult(false, "ban-ip/unban-ip needs a valid ip");
                var key = AbuseKey(address);
                var banning = request.Action == "ban-ip";
                lock (AbuseLock)
                {
                    if (banning) BannedIps.Add(key);
                    else BannedIps.Remove(key);
                }
                if (!banning) return new AdminActionResult(true, $"unbanned {key}");
                var dropped = 0;
                foreach (var (_, room) in Sessions)
                {
                    List<PeerConn> matches;
                    lock (room.Lock) matches = room.Peers.Where(peer => AbuseKey(peer.Ip).Equals(key)).ToList();
                    foreach (var peer in matches) { peer.Socket.Abort(); dropped++; }
                }
                return new AdminActionResult(true, $"banned {key}, dropped {dropped} live connection(s)");
            }
            case "clear-lockouts":
                lock (AbuseLock)
                {
                    JoinLockoutUntilByIp.Clear();
                    AdminLockoutUntilByIp.Clear();
                    FailedJoinsByIp.Clear();
                    FailedAdminByIp.Clear();
                }
                return new AdminActionResult(true, "cleared all lockouts and failure counters");
            case "pause":
                acceptingConnections = false;
                return new AdminActionResult(true, "paused -- no new connections accepted");
            case "resume":
                acceptingConnections = true;
                return new AdminActionResult(true, "resumed");
            case "set-limit":
            {
                if (request.Name is not { Length: > 0 } name || request.Value is not { } value)
                    return new AdminActionResult(false, "set-limit needs name and value");
                switch (name)
                {
                    case "max-messages-per-second": MaxMessagesPerSecond = (int)value; break;
                    case "max-bytes-per-second": MaxBytesPerSecond = (long)value; break;
                    case "max-message-bytes": MaxMessageBytes = (long)value; break;
                    case "max-connections-per-ip": MaxConnectionsPerIp = (int)value; break;
                    case "max-peers-per-session": MaxPeersPerSession = (int)value; break;
                    case "max-sessions": MaxTotalSessions = (int)value; break;
                    case "max-failed-joins": MaxFailedJoinsPerWindow = (int)value; break;
                    case "max-fragments-per-message": MaxFragmentsPerMessage = (int)value; break;
                    case "usage-warn-fraction": UsageWarnFraction = value; break;
                    default: return new AdminActionResult(false, $"unknown limit '{name}'");
                }
                return new AdminActionResult(true, $"{name} = {value}");
            }
            default:
                return new AdminActionResult(false, $"unknown action '{request.Action}'");
        }
    }

    // ---- Background maintenance -------------------------------------------------------------

    // Runs for the process's whole lifetime, alongside the accept loop in Main. Also prunes
    // stale per-IP abuse-tracking entries so a long-running process doesn't accumulate one
    // dictionary entry per distinct attacker IP forever, and periodically logs a summary line
    // plus an [ALERT] if rejections spiked -- see AlertRejectionThresholdPerTick.
    //
    // Every control in here is timed, so the loop must never be able to die or stall: an
    // unguarded throw or one unresponsive socket would silently stop idle-session reaping,
    // abuse-table pruning and the alert signal all at once.
    private static async Task ReapLoop()
    {
        var sinceLastSummary = TimeSpan.Zero;
        while (true)
        {
            await Task.Delay(ReapInterval);
            try
            {
                await ReapOnceAsync();
                sinceLastSummary += ReapInterval;
                if (sinceLastSummary >= SummaryLogInterval)
                {
                    sinceLastSummary = TimeSpan.Zero;
                    LogSummary();
                }
            }
            catch (Exception e)
            {
                RelayLog.Warn($"[AnoMech.Relay] Reap tick failed: {e} -- continuing.");
            }
        }
    }

    private static async Task ReapOnceAsync()
    {
        List<(string Code, List<PeerConn> Peers)> dead = new();
        // Enumerating a ConcurrentDictionary while calling TryRemove on it is safe (unlike
        // a plain Dictionary) -- no snapshot copy needed first.
        var cutoff = DateTime.UtcNow - IdleTimeout;
        foreach (var (code, room) in Sessions)
        {
            lock (room.Lock)
            {
                if (room.LastActivityUtc >= cutoff) continue;
                dead.Add((code, new List<PeerConn>(room.Peers)));
                Sessions.TryRemove(new KeyValuePair<string, Room>(code, room));
            }
        }
        foreach (var (code, peers) in dead)
        {
            RelayLog.Info($"[{code}] idle for over {IdleTimeout.TotalSeconds:F0}s -- disbanding ({peers.Count} connected).");
            await Task.WhenAll(peers.Select(peer =>
                CloseQuietlyAsync(peer.Socket, WebSocketCloseStatus.EndpointUnavailable, "session idle timeout")));
        }

        lock (AbuseLock)
        {
            var now = DateTime.UtcNow;
            PruneLockouts(JoinLockoutUntilByIp, FailedJoinsByIp, now);
            PruneLockouts(AdminLockoutUntilByIp, FailedAdminByIp, now);
        }

        var recent = Interlocked.Exchange(ref recentRejections, 0);
        if (recent >= AlertRejectionThresholdPerTick)
            RelayLog.Warn($"[ALERT] {recent} rejected connections in the last {ReapInterval.TotalSeconds:F0}s -- possible abuse in progress.");
    }

    private static void PruneLockouts(Dictionary<IPAddress, DateTime> lockouts,
                                      Dictionary<IPAddress, Queue<DateTime>> attempts, DateTime now)
    {
        foreach (var ip in lockouts.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
            lockouts.Remove(ip);
        foreach (var (ip, queue) in attempts.ToList())
        {
            while (queue.Count > 0 && now - queue.Peek() > FailedJoinWindow) queue.Dequeue();
            if (queue.Count == 0) attempts.Remove(ip);
        }
    }

    private static void LogSummary()
    {
        var stats = BuildAdminStats();
        RelayLog.Info($"[Summary] {stats.Sessions} sessions, {stats.TotalPeers} peers, {stats.ConnectionsByIpCount} distinct IPs live, "
            + $"{stats.ActiveJoinLockouts} active lockouts, {stats.TotalConnectionsAccepted} connections accepted lifetime.");
        RelayLog.Info($"[Summary] Peak per-connection load: {stats.PeakMessagesPerSecond} msg/s "
            + $"({stats.PeakMessagesPerSecond / (double)MaxMessagesPerSecond:P0} of cap), "
            + $"{stats.PeakBytesPerSecond / 1024} KB/s ({stats.PeakBytesPerSecond / (double)MaxBytesPerSecond:P0} of cap), "
            + $"{stats.NearLimitWarnings} near-limit warning(s).");
        foreach (var session in BuildSessionList())
            RelayLog.Detail($"[{session.Code}] usage: {session.Peers.Count} peers, "
                + string.Join("; ", session.Peers.Select(peer =>
                    $"#{peer.Id} {peer.MessagesIn}msg/{peer.BytesIn / 1024}KB peak {peer.PeakMessagesPerSecond}/s,{peer.PeakBytesPerSecond / 1024}KB/s")));
    }

    // ---- Fan-out -----------------------------------------------------------------------------

    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    // Returns how many peers it actually reached -- the caller logs that alongside the message
    // shape (see the Detail call in RunPeerAsync) without needing its own session lookup.
    private static async Task<int> BroadcastAsync(string sessionCode, PeerConn sender, byte[] bytes, WebSocketMessageType type)
    {
        if (!Sessions.TryGetValue(sessionCode, out var room)) return 0;
        List<PeerConn> targets;
        bool isFromHost;
        // Only THIS room's lock, not a relay-wide one -- unrelated sessions broadcasting at the
        // same time never contend with each other here, only two sends to the same session would.
        lock (room.Lock)
        {
            if (!Sessions.TryGetValue(sessionCode, out var current) || !ReferenceEquals(current, room)) return 0;
            if (!sender.Ready || !room.Peers.Contains(sender)) return 0;
            room.LastActivityUtc = DateTime.UtcNow;
            isFromHost = sender.PeerId == room.HostPeerId;
            // A peer's traffic goes to the host alone; only the host speaks to everyone.
            targets = room.Peers.Where(peer => !ReferenceEquals(peer, sender) && peer.Ready && peer.Socket.State == WebSocketState.Open
                && (isFromHost || peer.PeerId == room.HostPeerId)).ToList();
        }

        // Prefix identifying the ORIGINAL sender (see RelayWire.Envelope): host flag, the
        // relay-assigned connection id, the authenticated identity, and whether the body is
        // compressed. All of it is written here, never taken from the client, which is what makes
        // it unforgeable. Identity bytes need not be UTF-8, so the frame is always binary.
        var tagged = RelayWire.Envelope(isFromHost, sender.Id, sender.PeerId, type == WebSocketMessageType.Binary, bytes);

        Interlocked.Increment(ref totalMessagesBroadcast);
        Interlocked.Add(ref totalBytesBroadcast, tagged.Length * (long)targets.Count);

        // One shared CancellationTokenSource for the whole fan-out instead of one per target --
        // every send below starts at essentially the same instant (WhenAll launches them all
        // before awaiting), so a shared deadline is functionally identical to a per-target one
        // while costing O(1) timer/CTS allocations per broadcast instead of O(peers). At
        // hundreds of sessions this adds up fast otherwise (see README's Security notes).
        using var cts = new CancellationTokenSource(SendTimeout);
        // Parallel, not sequential -- one slow peer must not delay delivery to everyone else.
        await Task.WhenAll(targets.Select(target => SendOneAsync(target, tagged, WebSocketMessageType.Binary, cts.Token)));
        return targets.Count;
    }

    // A timed-out send is treated as fatal for that connection (aborted, not skipped): a
    // cancelled send can leave a half-written frame in the OS buffer, and reusing the
    // connection risks interleaving a fresh frame with that leftover -- corrupting the
    // stream from then on. Abort lets both sides' own receive loops notice and clean up.
    private static async Task SendOneAsync(PeerConn target, byte[] bytes, WebSocketMessageType type, CancellationToken timeout)
    {
        try
        {
            await target.SendGate.WaitAsync(timeout);
            try { await target.Socket.SendAsync(bytes, type, endOfMessage: true, timeout); }
            finally { target.SendGate.Release(); }
        }
        catch (OperationCanceledException)
        {
            RelayLog.Warn($"[Relay] Send to #{target.Id} timed out after {SendTimeout.TotalSeconds:F0}s -- aborting that connection.");
            target.Socket.Abort();
        }
        catch (WebSocketException)
        {
            // Dead socket -- its own receive loop will observe the failure and Leave().
        }
        catch (ObjectDisposedException)
        {
            // Raced with a kick/teardown -- same.
        }
    }

    // CloseAsync waits for the peer's own close frame, which a hostile or wedged client is
    // free never to send. Every close in this process goes through here so none of them can
    // block on that.
    // A close is a send too, so it waits its turn behind any broadcast to the same socket. A
    // close the relay starts (kick, ban) lingers so the client can read why before the abort;
    // answering the client's own close has nothing left to wait for.
    private static async Task ClosePeerAsync(PeerConn peer, WebSocketCloseStatus status, string? description, bool linger = true)
    {
        using var timeout = new CancellationTokenSource(CloseTimeout);
        try
        {
            await peer.SendGate.WaitAsync(timeout.Token);
            try { await peer.Socket.CloseOutputAsync(status, description, timeout.Token); }
            finally { peer.SendGate.Release(); }
            if (linger) await Task.Delay(CloseTimeout);
        }
        catch (Exception) { }
        finally { peer.Socket.Abort(); }
    }

    private static async Task CloseQuietlyAsync(WebSocket socket, WebSocketCloseStatus status, string? description)
    {
        try
        {
            using var cts = new CancellationTokenSource(CloseTimeout);
            await socket.CloseAsync(status, description, cts.Token);
        }
        catch (Exception)
        {
            try { socket.Abort(); } catch { /* best effort */ }
        }
    }
}

// Polls a running relay's admin endpoints and renders a live text dashboard with actions.
// No TUI dependency -- periodic clear + rewrite plus single-key commands is enough here.
internal static class AdminConsole
{
    private static readonly JsonSerializerOptions Json = Program.AdminJson;
    private static HttpClient http = null!;
    private static string host = "";
    private static string? lastMessage;
    // Redirected output (a pipe, systemd) has no console buffer, so Clear/KeyAvailable throw.
    // The dashboard still works as a plain append-only feed there.
    private static bool interactive = true;

    public static async Task RunAsync(string[] args)
    {
        var port = 7890;
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] is "--port" or "-p" && int.TryParse(args[i + 1], out var parsed)) port = parsed;
        host = (ValueOf(args, "--host") ?? $"http://localhost:{port}").TrimEnd('/');
        var adminToken = ValueOf(args, "--admin-token") ?? Environment.GetEnvironmentVariable("ANOMECH_RELAY_ADMIN_TOKEN");
        if (string.IsNullOrEmpty(adminToken))
        {
            Console.Error.WriteLine("--admin-token (or ANOMECH_RELAY_ADMIN_TOKEN) is required in --admin mode.");
            return;
        }

        // The admin token goes out on every request, so it must not leave in the clear or be
        // redirected to another server.
        if (!IsSafeAdminUri(host))
        {
            Console.Error.WriteLine("Admin connections require HTTPS, except HTTP on loopback.");
            return;
        }
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.Add("X-AnoMech-Admin-Token", adminToken);

        var showSessions = false;
        while (true)
        {
            try
            {
                var stats = JsonSerializer.Deserialize<Program.AdminStats>(await http.GetStringAsync($"{host}/admin/stats"), Json);
                var sessions = showSessions
                    ? JsonSerializer.Deserialize<List<Program.SessionInfo>>(await http.GetStringAsync($"{host}/admin/sessions"), Json)
                    : null;
                Render(stats, sessions, null);
            }
            catch (Exception e)
            {
                Render(null, null, e.Message);
            }

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                if (!interactive) { await Task.Delay(200); continue; }
                bool pressed;
                try { pressed = Console.KeyAvailable; }
                catch (Exception) { interactive = false; continue; }
                if (!pressed) { await Task.Delay(50); continue; }
                var key = Console.ReadKey(intercept: true).KeyChar;
                if (key == 'q') return;
                if (key == 's') { showSessions = !showSessions; break; }
                await HandleCommandAsync(key);
                break;
            }
        }
    }

    internal static bool IsSafeAdminUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        return uri.Scheme == "https" || (uri.Scheme == "http" &&
            (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
             || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip)
                 && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip))));
    }

    private static string? ValueOf(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    private static async Task HandleCommandAsync(char key)
    {
        switch (key)
        {
            case 'k': await PostAsync(new Program.AdminActionRequest("kick-peer", null, AskUint("Connection id"), null, null, null)); break;
            case 'd': await PostAsync(new Program.AdminActionRequest("disband-session", Ask("Session code")?.ToUpperInvariant(), null, null, null, null)); break;
            case 'b': await PostAsync(new Program.AdminActionRequest("ban-ip", null, null, Ask("IP to ban"), null, null)); break;
            case 'u': await PostAsync(new Program.AdminActionRequest("unban-ip", null, null, Ask("IP to unban"), null, null)); break;
            case 'c': await PostAsync(new Program.AdminActionRequest("clear-lockouts", null, null, null, null, null)); break;
            case 'p': await PostAsync(new Program.AdminActionRequest("pause", null, null, null, null, null)); break;
            case 'r': await PostAsync(new Program.AdminActionRequest("resume", null, null, null, null, null)); break;
            case 'l':
            {
                var name = Ask("Limit name (max-messages-per-second, max-bytes-per-second, max-message-bytes, " +
                               "max-connections-per-ip, max-peers-per-session, max-sessions, max-failed-joins, " +
                               "max-fragments-per-message, usage-warn-fraction)");
                var value = AskDouble("New value");
                await PostAsync(new Program.AdminActionRequest("set-limit", null, null, null, name, value));
                break;
            }
        }
    }

    private static string? Ask(string prompt)
    {
        Console.Write($"{prompt}: ");
        var line = Console.ReadLine();
        return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
    }

    private static uint? AskUint(string prompt) => uint.TryParse(Ask(prompt), out var value) ? value : null;
    private static double? AskDouble(string prompt) => double.TryParse(Ask(prompt), out var value) ? value : null;

    private static async Task PostAsync(Program.AdminActionRequest request)
    {
        try
        {
            var response = await http.PostAsync($"{host}/admin/action",
                new StringContent(JsonSerializer.Serialize(request, Json), Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Program.AdminActionResult>(body, Json);
            lastMessage = result is null ? $"HTTP {(int)response.StatusCode}" : $"{(result.Ok ? "OK" : "FAILED")}: {result.Message}";
        }
        catch (Exception e)
        {
            lastMessage = $"FAILED: {e.Message}";
        }
    }

    private static void Render(Program.AdminStats? s, List<Program.SessionInfo>? sessions, string? error)
    {
        if (interactive)
        {
            try { Console.Clear(); }
            catch (Exception) { interactive = false; }
        }
        Console.WriteLine();
        Console.WriteLine($"AnoMech.Relay admin -- {host}   (refreshes every 2s)");
        if (interactive)
            Console.WriteLine("[s] sessions  [k] kick peer  [d] disband  [b] ban ip  [u] unban  [c] clear lockouts  [p] pause  [r] resume  [l] set limit  [q] quit");
        Console.WriteLine(new string('-', 110));
        if (error != null) { Console.WriteLine($"Failed to fetch stats: {error}"); return; }
        if (s == null) { Console.WriteLine("No data."); return; }

        var uptime = TimeSpan.FromSeconds(s.UptimeSeconds);
        Console.WriteLine($"Uptime {(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s   relay v{s.RelayVersion}   " +
                          $"{(s.AcceptingConnections ? "ACCEPTING" : "PAUSED")}");
        Console.WriteLine($"Sessions {s.Sessions}   peers {s.TotalPeers}   distinct IPs {s.ConnectionsByIpCount}   " +
                          $"lockouts {s.ActiveJoinLockouts}   banned {s.BannedIpCount}");
        Console.WriteLine($"Lifetime: {s.TotalConnectionsAccepted} connections, {s.TotalMessagesBroadcast} messages, {s.TotalBytesBroadcast:N0} bytes");
        Console.WriteLine();
        Console.WriteLine("Load vs caps:");
        Console.WriteLine($"  Messages/sec peak:  {s.PeakMessagesPerSecond,10:N0} / {s.Limits.MaxMessagesPerSecond,-10:N0} {Bar(s.PeakMessagesPerSecond, s.Limits.MaxMessagesPerSecond)}");
        Console.WriteLine($"  Bytes/sec peak:     {s.PeakBytesPerSecond,10:N0} / {s.Limits.MaxBytesPerSecond,-10:N0} {Bar(s.PeakBytesPerSecond, s.Limits.MaxBytesPerSecond)}");
        Console.WriteLine($"  Near-limit warnings:{s.NearLimitWarnings,10:N0}  (logged past {s.Limits.UsageWarnFraction:P0} of a cap)");
        Console.WriteLine();
        Console.WriteLine("Rejections (lifetime):");
        Console.WriteLine($"  origin {s.Rejections.Origin}   ipCap {s.Rejections.IpCap}   lockout {s.Rejections.JoinLockout}   " +
                          $"relayFull {s.Rejections.RelayFull}   sessionFull {s.Rejections.SessionFull}   notFound {s.Rejections.SessionNotFound}");
        Console.WriteLine($"  badToken {s.Rejections.BadToken}   handshake {s.Rejections.HandshakeTimeout}   tooLarge {s.Rejections.MessageTooLarge}   " +
                          $"msgTimeout {s.Rejections.MessageTimeout}   msgRate {s.Rejections.MessageRate}   byteRate {s.Rejections.ByteRate}");
        Console.WriteLine($"  unencrypted {s.Rejections.Unencrypted}   fragments {s.Rejections.TooManyFragments}   banned {s.Rejections.Banned}   paused {s.Rejections.Paused}   protocol {s.Rejections.BadProtocol}");
        Console.WriteLine($"  last ~2s: {s.RecentRejections}{(s.RecentRejections >= 50 ? "   [ELEVATED -- possible abuse]" : "")}");
        Console.WriteLine();
        Console.WriteLine($"Memory {s.MemoryBytes / 1024 / 1024:N0} MB   GC {s.Gen0Collections}/{s.Gen1Collections}/{s.Gen2Collections}");

        if (sessions != null)
        {
            Console.WriteLine();
            Console.WriteLine("Sessions:");
            if (sessions.Count == 0) Console.WriteLine("  (none)");
            foreach (var session in sessions)
            {
                Console.WriteLine($"  {session.Code}  age {session.AgeSeconds:F0}s  idle {session.IdleSeconds:F1}s  {session.Peers.Count} peer(s)");
                foreach (var peer in session.Peers)
                    Console.WriteLine($"    #{peer.Id,-6} {peer.Ip,-40} {(peer.IsHost ? "HOST" : "peer")}  " +
                                      $"{peer.MessagesIn,8:N0} msg  {peer.BytesIn / 1024,8:N0} KB  " +
                                      $"peak {peer.PeakMessagesPerSecond}/s {peer.PeakBytesPerSecond / 1024}KB/s");
            }
        }

        if (lastMessage != null)
        {
            Console.WriteLine();
            Console.WriteLine($"> {lastMessage}");
        }
    }

    private static string Bar(long value, double limit)
    {
        var fraction = limit <= 0 ? 0 : Math.Clamp(value / limit, 0, 1);
        var filled = (int)Math.Round(fraction * 30);
        return $"[{new string('#', filled)}{new string('.', 30 - filled)}] {fraction:P0}";
    }
}

// File logging: mirrors console-worthy events to disk plus much higher-volume per-message
// detail, rotated and gzip-compressed, held under a total size cap. Never logs message
// CONTENTS -- only metadata (who, when, which session, how big, how many peers it reached),
// same stance as the README's existing "the relay doesn't log message contents" note.
//
// A plain rotating/compressed log directory rather than a database: this is meant to run as a
// single, dependency-free binary an operator can drop on a box (see Program's own header
// comment on that goal) -- a database would mean standing up and separately securing another
// service just to hold logs, for a data volume this design already keeps well within one
// process's own housekeeping.
internal static class RelayLog
{
    private static string? logDir;
    private static long maxTotalBytes;
    private static StreamWriter? activeWriter;
    private static string? activeFilePath;
    private static long activeBytesWritten;

    // Segment size before a file is compressed and a new one started. Well under the total
    // cap -- keeps the always-uncompressed "live" segment a small, bounded slice of the
    // budget, and keeps each rotation's compression work modest.
    private const long RotateThresholdBytes = 64 * 1024 * 1024;

    // Producers (Info/Warn/Detail, called from every connection's own async flow) only ever
    // enqueue a string -- no lock, no disk I/O, on that path. A single background task is the
    // only thing that ever touches the file, so it needs no locking either. At hundreds of
    // sessions all broadcasting, this is what keeps logging from becoming the actual bottleneck
    // (it used to be a synchronous, AutoFlush=true write under one relay-wide lock, shared by
    // literally every connection).
    // Bounded, not unbounded: a stuck/full disk should drop log lines rather than let the queue
    // grow without limit and eventually pressure the process's own memory. Capacity is generous
    // relative to realistic burst rates -- dropping is the rare, "something's already wrong" case.
    private static readonly Channel<string> Queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(20_000) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite });
    private static long droppedLines;

    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static DateTime lastFlushUtc = DateTime.UtcNow;

    public static void Configure(string directory, long maxBytes)
    {
        logDir = directory;
        maxTotalBytes = maxBytes;
        Directory.CreateDirectory(logDir);
        OpenNewActiveFile();
        _ = SuperviseWriterAsync();
    }

    // Logging is an abuse-forensics control, so a transient disk failure must not silently
    // retire it for the rest of the process's life -- the writer is restarted instead, and the
    // failure is reported through the console, which doesn't depend on the file being writable.
    private static async Task SuperviseWriterAsync()
    {
        while (true)
        {
            try
            {
                await RunWriterLoopAsync();
                return; // channel completed
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[AnoMech.Relay] Log writer failed: {e.Message} -- restarting in 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5));
                try { OpenNewActiveFile(); }
                catch (Exception reopen) { Console.Error.WriteLine($"[AnoMech.Relay] Could not reopen the log: {reopen.Message}"); }
            }
        }
    }

    // The only place that touches activeWriter/activeBytesWritten/rotation/compression --
    // single-reader by construction (see Queue above), so none of that needs its own lock.
    private static async Task RunWriterLoopAsync()
    {
        await foreach (var line in Queue.Reader.ReadAllAsync())
        {
            activeWriter!.WriteLine(line);
            activeBytesWritten += line.Length + 2;

            // Batches flushes instead of one disk write per line (what AutoFlush did) --
            // still bounds how stale the file can be to ~1s, without paying a syscall per
            // message broadcast under real load.
            if (DateTime.UtcNow - lastFlushUtc >= FlushInterval)
            {
                activeWriter.Flush();
                lastFlushUtc = DateTime.UtcNow;
            }

            if (activeBytesWritten >= RotateThresholdBytes)
                Rotate();
        }
    }

    private static void OpenNewActiveFile()
    {
        activeFilePath = Path.Combine(logDir!, $"relay-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.log");
        // FileShare.ReadWrite (not StreamWriter's own default of Read-only sharing) so
        // --session-log can open and read the still-active segment while the relay keeps
        // writing to it, instead of hitting a sharing-violation IOException.
        var stream = new FileStream(activeFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        activeWriter = new StreamWriter(stream, Encoding.UTF8);
        activeBytesWritten = 0;
    }

    // Console + file. Events an operator should see live: startup, session lifecycle,
    // rejections severe enough to already carry their own explicit message, alerts, summaries.
    public static void Info(string message)
    {
        Console.WriteLine(message);
        Write("INFO", message);
    }

    public static void Warn(string message)
    {
        Console.Error.WriteLine(message);
        Write("WARN", message);
    }

    // File only. For anything high-volume enough that echoing it to the console would drown
    // out the events actually worth watching live: individual rejection reasons, every broadcast.
    public static void Detail(string message) => Write("DETAIL", message);

    private static void Write(string level, string message)
    {
        if (logDir == null) return; // file logging disabled (--no-file-log) -- console-only.
        var line = $"{DateTime.UtcNow:O} [{level}] {message}";
        if (!Queue.Writer.TryWrite(line))
            Interlocked.Increment(ref droppedLines);
    }

    // Called from the writer loop only.
    private static void Rotate()
    {
        activeWriter!.Dispose();
        var finished = activeFilePath!;
        OpenNewActiveFile();
        var dropped = Interlocked.Exchange(ref droppedLines, 0);
        if (dropped > 0) activeWriter!.WriteLine($"{DateTime.UtcNow:O} [WARN] {dropped} log line(s) dropped -- write queue was full.");
        CompressAndDelete(finished);
        EnforceCap();
    }

    // Best-effort flush for a graceful shutdown (see Main's ProcessExit hook) -- anything still
    // sitting in the queue at the instant of a hard kill is lost either way, same tradeoff any
    // buffered logger makes.
    public static void FlushOnShutdown()
    {
        try { activeWriter?.Flush(); } catch { /* best effort */ }
    }

    private static void CompressAndDelete(string path)
    {
        try
        {
            using (var input = File.OpenRead(path))
            using (var output = File.Create(path + ".gz"))
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                input.CopyTo(gzip);
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort -- an uncompressed leftover segment still counts toward EnforceCap's
            // total below, so it still ages out via oldest-first deletion even if compression
            // itself failed for some reason (e.g. disk full).
        }
    }

    // Deletes the oldest completed (.gz) segments until the directory is back under the cap.
    // Never touches the currently-active segment.
    private static void EnforceCap()
    {
        try
        {
            var completed = new DirectoryInfo(logDir!).GetFiles("relay-*.log.gz").OrderBy(f => f.Name).ToList();
            var activeSize = File.Exists(activeFilePath) ? new FileInfo(activeFilePath!).Length : 0;
            var total = activeSize + completed.Sum(f => f.Length);
            foreach (var file in completed)
            {
                if (total <= maxTotalBytes) break;
                total -= file.Length;
                try { file.Delete(); } catch (IOException) { /* best effort */ }
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[AnoMech.Relay] Log cap enforcement failed: {e.Message}");
        }
    }

    // `AnoMech.Relay --session-log <CODE> --log-dir <dir>` -- scans every segment (live and
    // compressed), oldest first, for lines tagged with that session code. The tag format
    // ("[CODE] ...") is the same one every session-scoped log line already uses, so this needs
    // no separate structured format to stay useful.
    public static void PrintSessionLog(string directory, string sessionCode)
    {
        var tag = $"[{sessionCode}]";
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"No log directory at {directory}.");
            return;
        }
        var files = new DirectoryInfo(directory).GetFiles("relay-*.log*").OrderBy(f => f.Name).ToList();
        var found = 0;
        foreach (var file in files)
        {
            // ReadWrite sharing -- the currently-active segment is still open for writing by
            // a live relay process (see OpenNewActiveFile) when this runs alongside it.
            var raw = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = file.Extension == ".gz"
                ? new StreamReader(new GZipStream(raw, CompressionMode.Decompress))
                : new StreamReader(raw);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.Contains(tag)) continue;
                Console.WriteLine(line);
                found++;
            }
        }
        if (found == 0) Console.WriteLine($"No log lines found for session {sessionCode} under {directory}.");
    }
}
