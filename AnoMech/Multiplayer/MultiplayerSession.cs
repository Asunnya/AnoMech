using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Multiplayer;

// Lobby roster, mirrored on every client from the host's LobbyStateMessage; the host's copy
// is the authoritative one.
public sealed class MultiplayerSession
{
    public Guid HostId { get; set; }
    public Dictionary<PartyRole, Guid> ClaimedBy { get; private set; } = new();
    public Dictionary<Guid, string> Names { get; private set; } = new();
    public Dictionary<Guid, PeerBuildInfo> Builds { get; private set; } = new();
    // Each player's real job; their puppet carries it instead of the seat preset's.
    public Dictionary<Guid, byte> Jobs { get; private set; } = new();
    public bool Started { get; set; }

    // Indices into Game.Scenarios and that scenario's AiStrats/WaymarkPresets; -1 = the host
    // hasn't picked a scenario yet.
    public int ScenarioIndex { get; set; } = -1;
    public int SelectedAi { get; set; }
    public int SelectedWaymark { get; set; }

    // Keyed by TankBusterCastInfo.Id; 0/missing = no mitigation planned for that cast.
    public Dictionary<string, ushort> TankBusterPlan { get; private set; } = new();

    // Display lines from ScenarioSettingsSummary.
    public List<string> ScenarioSettings { get; set; } = new();
    private const int MaxScenarioSettingLines = 64;

    // The same overrides as JSON, for a peer to apply (see ScenarioSettingsSync). Length-capped
    // rather than Cleaned: stripping characters would only produce unparsable JSON.
    public string? ScenarioSettingsJson { get; set; }
    private const int MaxScenarioSettingsJson = 4096;

    // Sanitised once here. Indices stay as sent and are validated where used
    // (TryResolveScenario): clamping would silently run the wrong scenario.
    public void ApplyLobbyState(LobbyStateMessage msg)
    {
        HostId = msg.HostId;
        ClaimedBy = new Dictionary<PartyRole, Guid>(msg.ClaimedBy.Where(kv => Enum.IsDefined(kv.Key)));
        Names = msg.Names.Take(NetGuard.MaxSessionPeers).ToDictionary(kv => kv.Key, kv => NetGuard.Clean(kv.Value));
        Builds = msg.Builds.Take(NetGuard.MaxSessionPeers).ToDictionary(kv => kv.Key,
            kv => new PeerBuildInfo(NetGuard.Clean(kv.Value.Version), NetGuard.Clean(kv.Value.Checksum)));
        Jobs = msg.Jobs.Take(NetGuard.MaxSessionPeers).ToDictionary(kv => kv.Key, kv => NetGuard.ClassJob(kv.Value));
        Started = msg.Started;
        ScenarioIndex = msg.ScenarioIndex;
        SelectedAi = msg.SelectedAi;
        SelectedWaymark = msg.SelectedWaymark;
        TankBusterPlan = msg.TankBusterPlan
            .Where(kv => !string.IsNullOrEmpty(kv.Key))
            .Take(MaxScenarioSettingLines)
            .ToDictionary(kv => NetGuard.Clean(kv.Key), kv => kv.Value);
        ScenarioSettings = NetGuard.Cap(msg.ScenarioSettings, MaxScenarioSettingLines).Select(NetGuard.Clean).ToList();
        ScenarioSettingsJson = msg.ScenarioSettingsJson is { Length: > 0 and <= MaxScenarioSettingsJson } json ? json : null;
    }

    public LobbyStateMessage ToMessage() => new(
        HostId, new Dictionary<PartyRole, Guid>(ClaimedBy), new Dictionary<Guid, string>(Names),
        new Dictionary<Guid, PeerBuildInfo>(Builds), new Dictionary<Guid, byte>(Jobs), Started, ScenarioIndex, SelectedAi, SelectedWaymark,
        new Dictionary<string, ushort>(TankBusterPlan), new List<string>(ScenarioSettings), ScenarioSettingsJson);

    public PartyRole? RoleOf(Guid peerId) =>
        ClaimedBy.Where(kv => kv.Value == peerId).Select(kv => (PartyRole?)kv.Key).FirstOrDefault();

    // 0 = unknown, leaving the seat preset's job.
    public byte JobOf(Guid peerId) => Jobs.GetValueOrDefault(peerId);

    public string NameOf(Guid peerId) => Names.GetValueOrDefault(peerId, "Player") is { Length: > 0 } name ? name : "Player";
}
