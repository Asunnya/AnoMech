using System.Diagnostics;

namespace AnoMech.Multiplayer;

// What this client is spending against the relay's per-connection abuse caps, so a run that is
// creeping toward one is visible before the relay cuts the socket. Rates are measured over a
// rolling window of about a second; the maxima are for the whole connection.
//
// The caps mirror Relay/AnoMech.Relay/Program.cs. A relay started with different flags will
// enforce different numbers, so these are what the readout compares against, not a promise.
internal static class RelayStats
{
    public const int MaxMessagesPerSecond = 5000;
    public const long MaxBytesPerSecond = 10L * 1024 * 1024;
    public const long MaxMessageBytes = 1 * 1024 * 1024;
    public const int MaxPeersPerSession = 8;

    public readonly record struct Snapshot(
        int MessagesPerSecond, int MaxMessagesPerSecond,
        long SentBytesPerSecond, long MaxSentBytesPerSecond,
        long ReceivedBytesPerSecond, long MaxReceivedBytesPerSecond,
        long LastMessageBytes, long LargestMessageBytes,
        int Peers, int MaxPeers,
        long TotalSentBytes, long TotalReceivedBytes);

    private static readonly object Gate = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static double windowStart;
    private static int windowMessages;
    private static long windowSentBytes;
    private static long windowReceivedBytes;

    private static int messagesPerSecond;
    private static long sentBytesPerSecond;
    private static long receivedBytesPerSecond;

    private static int peakMessagesPerSecond;
    private static long peakSentBytesPerSecond;
    private static long peakReceivedBytesPerSecond;
    private static long lastMessageBytes;
    private static long largestMessageBytes;
    private static int peers;
    private static int peakPeers;
    private static long totalSentBytes;
    private static long totalReceivedBytes;

    public static void Reset()
    {
        lock (Gate)
        {
            windowStart = Clock.Elapsed.TotalSeconds;
            windowMessages = 0;
            windowSentBytes = 0;
            windowReceivedBytes = 0;
            messagesPerSecond = 0;
            sentBytesPerSecond = 0;
            receivedBytesPerSecond = 0;
            peakMessagesPerSecond = 0;
            peakSentBytesPerSecond = 0;
            peakReceivedBytesPerSecond = 0;
            lastMessageBytes = 0;
            largestMessageBytes = 0;
            peers = 0;
            peakPeers = 0;
            totalSentBytes = 0;
            totalReceivedBytes = 0;
        }
    }

    // Roster size isn't traffic, so it's reported in rather than counted here.
    public static void ObservePeers(int count)
    {
        lock (Gate)
        {
            peers = count;
            if (count > peakPeers) peakPeers = count;
        }
    }

    public static void RecordSent(int bytes)
    {
        lock (Gate)
        {
            windowMessages++;
            windowSentBytes += bytes;
            totalSentBytes += bytes;
            lastMessageBytes = bytes;
            if (bytes > largestMessageBytes) largestMessageBytes = bytes;
            Roll();
        }
    }

    public static void RecordReceived(long bytes)
    {
        lock (Gate)
        {
            windowReceivedBytes += bytes;
            totalReceivedBytes += bytes;
            Roll();
        }
    }

    public static Snapshot Current
    {
        get
        {
            lock (Gate)
            {
                Roll();
                return new Snapshot(
                    messagesPerSecond, peakMessagesPerSecond,
                    sentBytesPerSecond, peakSentBytesPerSecond,
                    receivedBytesPerSecond, peakReceivedBytesPerSecond,
                    lastMessageBytes, largestMessageBytes,
                    peers, peakPeers,
                    totalSentBytes, totalReceivedBytes);
            }
        }
    }

    // Divided by the window's real length rather than assumed to be one second, so a quiet
    // stretch reads as a low rate instead of holding the last busy second's number.
    private static void Roll()
    {
        var now = Clock.Elapsed.TotalSeconds;
        var elapsed = now - windowStart;
        if (elapsed < 1.0) return;

        messagesPerSecond = (int)(windowMessages / elapsed);
        sentBytesPerSecond = (long)(windowSentBytes / elapsed);
        receivedBytesPerSecond = (long)(windowReceivedBytes / elapsed);
        if (messagesPerSecond > peakMessagesPerSecond) peakMessagesPerSecond = messagesPerSecond;
        if (sentBytesPerSecond > peakSentBytesPerSecond) peakSentBytesPerSecond = sentBytesPerSecond;
        if (receivedBytesPerSecond > peakReceivedBytesPerSecond) peakReceivedBytesPerSecond = receivedBytesPerSecond;

        windowMessages = 0;
        windowSentBytes = 0;
        windowReceivedBytes = 0;
        windowStart = now;
    }
}
