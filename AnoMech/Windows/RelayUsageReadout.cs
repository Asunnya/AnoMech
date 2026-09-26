#if DEBUG
using System;
using System.Numerics;
using AnoMech.Multiplayer;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Windows;

// What this client is spending against the relay's per-connection caps, plus the traffic either
// way. The relay enforces these; the plugin only reports them.
internal static class RelayUsageReadout
{
    internal static void Draw(MultiplayerManager mp)
    {
        if (!ImGui.CollapsingHeader("Relay usage (debug)")) return;
        RelayStats.ObservePeers(mp.Session.Names.Count);
        var s = RelayStats.Current;

        if (ImGui.BeginTable("##relayusage", 3, ImGuiTableFlags.SizingFixedFit))
        {
            Header();
            Capped("Messages/sec", s.MessagesPerSecond, RelayStats.MaxMessagesPerSecond, s.MaxMessagesPerSecond, v => v.ToString());
            Capped("Sent/sec", s.SentBytesPerSecond, RelayStats.MaxBytesPerSecond, s.MaxSentBytesPerSecond, Bytes);
            Capped("Message size", s.LastMessageBytes, RelayStats.MaxMessageBytes, s.LargestMessageBytes, Bytes);
            Capped("Players in session", s.Peers, RelayStats.MaxPeersPerSession, s.MaxPeers, v => v.ToString());
            Row("Received/sec", Bytes(s.ReceivedBytesPerSecond), Bytes(s.MaxReceivedBytesPerSecond));
            Row("Sent total", Bytes(s.TotalSentBytes), "");
            Row("Received total", Bytes(s.TotalReceivedBytes), "");
            ImGui.EndTable();
        }
        ImGui.TextDisabled("Caps are the relay's defaults; one started with other flags differs.");
    }

    private static void Header()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextDisabled("Value");
        ImGui.TableSetColumnIndex(1);
        ImGui.TextDisabled("Now / cap");
        ImGui.TableSetColumnIndex(2);
        ImGui.TextDisabled("Max this session");
    }

    private static void Row(string label, string now, string max)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(now);
        ImGui.TableSetColumnIndex(2);
        ImGui.TextUnformatted(max);
    }

    // Amber past half the cap (the relay's own [NEAR-LIMIT] point), red at it.
    private static void Capped(string label, long now, long cap, long max, Func<long, string> format)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);
        ImGui.TableSetColumnIndex(1);
        var fraction = cap <= 0 ? 0d : (double)now / cap;
        var color = fraction >= 1d ? new Vector4(1f, 0.4f, 0.4f, 1f)
            : fraction >= 0.5d ? new Vector4(1f, 0.85f, 0.3f, 1f)
            : new Vector4(0.8f, 0.8f, 0.8f, 1f);
        ImGui.TextColored(color, $"{format(now)} / {format(cap)}");
        ImGui.TableSetColumnIndex(2);
        ImGui.TextUnformatted(format(max));
    }

    private static string Bytes(long value)
        => value >= 1024 * 1024 ? $"{value / (1024f * 1024f):F2} MB"
            : value >= 1024 ? $"{value / 1024f:F1} KB"
            : $"{value} B";
}
#endif
