using System.Net.WebSockets;

namespace AnoMech.Relay.Tests;

// Always open until aborted; each send yields, so two sends not serialized by the caller overlap.
internal sealed class StubSocket : WebSocket
{
    private int concurrent;
    public int PeakSends;
    public int Sends;
    public bool Aborted;
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override WebSocketState State => Aborted ? WebSocketState.Aborted : WebSocketState.Open;
    public override string? SubProtocol => null;
    public override void Abort() => Aborted = true;
    public override void Dispose() { }
    public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
    public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token) => throw new NotSupportedException();
    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
    {
        var count = Interlocked.Increment(ref concurrent);
        PeakSends = Math.Max(count, PeakSends);
        await Task.Yield();
        Interlocked.Decrement(ref concurrent);
        Interlocked.Increment(ref Sends);
    }
}
