using System;

namespace AnoMech.Multiplayer;

// A relay run inside the plugin, owned by the host: started by "Start server & host" and stopped
// when the host leaves the session.
// TODO: back this with AnoMech.Relay's RelayServer.
public sealed class EmbeddedRelay : IDisposable
{
    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    public string LocalUrl => $"ws://127.0.0.1:{Port}";

    // Null on success, otherwise why it couldn't start.
    public string? Start(int port)
    {
        if (IsRunning) return null;
        if (port is < 1 or > 65535) return $"Port {port} is out of range.";
        return "The embedded server isn't implemented yet.";
    }

    public void Stop()
    {
        IsRunning = false;
    }

    public void Dispose() => Stop();
}
