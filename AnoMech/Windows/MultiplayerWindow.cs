using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using static AnoMech.Core.Game.Game;

namespace AnoMech.Windows;

// Multiplayer lobby. The hosted scenario is whatever MainWindow has selected; unclaimed roles
// stay bots. The host can seat, kick and ban people and configure the scenario here; everyone
// else sees the config read-only.
public class MultiplayerWindow : Window, IDisposable
{
    private static readonly string[] RoleLabels = ["MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2"];

    private readonly Plugin plugin;
    private readonly MultiplayerManager mp;
    private string relayUrl;
    private string relayToken;
    private string joinCode = "";
    private string displayName = "Player";
    private bool namePrefilled;
    // null = unknown (not checked yet, or the relay is unreachable), treated as "no token
    // needed". Re-checked whenever relayUrl changes.
    private bool? relayRequiresToken;
    private string? relayInfoCheckedForUrl;

    public MultiplayerWindow(Plugin plugin) : base("AnoMech Multiplayer###AnoMechMultiplayer")
    {
        this.plugin = plugin;
        mp = plugin.Multiplayer;
        // Grows and shrinks with its content: a settings panel opening, a roster filling up.
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 0),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Flags |= ImGuiWindowFlags.AlwaysAutoResize;
        compactFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(
            e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(UiBuilder.DefaultFontSizePx * CompactFontFraction)));
        IsOpen = false;
        relayUrl = plugin.Configuration.RelayServerUrl;
        relayToken = plugin.Configuration.TokenForRelay(relayUrl);
        // ObjectTable.LocalPlayer is main-thread-only and plugins are constructed off-thread,
        // so the name is prefilled in Draw().
    }

    public void Dispose() => compactFont.Dispose();

    // The settings panels are the bulk of the window, so they get smaller text and tighter
    // widgets than the rest of it.
    private const float CompactFontFraction = 0.85f;
    private const float CompactWidthScale = 0.8f;
    private readonly IFontHandle compactFont;

    private readonly struct CompactScope : IDisposable
    {
        private readonly IDisposable font;

        public CompactScope(IFontHandle handle)
        {
            font = handle.Push();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 2f));
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(2f, 1f));
            SettingsGrid.WidthScale = CompactWidthScale;
        }

        public void Dispose()
        {
            SettingsGrid.WidthScale = 1f;
            ImGui.PopStyleVar(3);
            font.Dispose();
        }
    }

    // An auto-resizing window widens to fit unwrapped text, so paragraphs wrap at a fixed width.
    private const float WrapWidth = 400f;

    private static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + WrapWidth);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    private static void WrappedColored(Vector4 color, string text)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + WrapWidth);
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }

    // Hidden while the fake-zone instance is loaded -- see MainWindow's PreOpenCheck.
    private bool hiddenByUs;

    public override void PreOpenCheck()
    {
        if (plugin.Game.World.Map.IsInInstance)
        {
            if (IsOpen) hiddenByUs = true;
            IsOpen = false;
        }
        else if (hiddenByUs)
        {
            hiddenByUs = false;
            IsOpen = true;
        }
    }

    // The host reads its own main-window selection; everyone else the index the host mirrored
    // into the lobby.
    private string CurrentScenarioLabel()
    {
        if (mp.IsHost && !mp.Session.Started && Plugin.MainWindow.SelectedScenario is { } scenario) return DisplayName(scenario);
        return mp.TryResolveScenario() is { } chosen ? DisplayName(chosen) : "not chosen yet";
    }

    public override void Draw()
    {
        if (!namePrefilled)
        {
            if (Plugin.ObjectTable.LocalPlayer?.Name.TextValue is { Length: > 0 } name)
                displayName = name;
            namePrefilled = true;
        }

        WindowName = $"AnoMech Multiplayer ({CurrentScenarioLabel()})###AnoMechMultiplayer";

        Wrapped($"Vertical-slice multiplayer for {CurrentScenarioLabel()}. One host runs the real " +
                "simulation; up to 7 others join and take over bot slots.");
        if (mp.SessionCode == null
            && (Plugin.MainWindow.SelectedScenario is not { } sel || !sel.SupportsMultiplayer))
        {
            // IsHost is never reset on leave, so this isn't gated on it.
            WrappedColored(new Vector4(1f, 0.6f, 0.4f, 1f),
                "Select a multiplayer-supported scenario in the main window before hosting.");
        }
        ImGui.Separator();

        // SessionCode rather than IsConnected: a brief relay drop must keep the roster (with a
        // Reconnecting indicator).
        if (mp.SessionCode == null)
            DrawConnectPanel();
        // SessionCode is set synchronously on Join, before any host confirmation.
        else if (!mp.IsHost && !mp.EverHeardFromHost)
            DrawJoiningPanel();
        else
            DrawConnectedPanel();
    }

    private void DrawJoiningPanel()
    {
        ImGui.TextUnformatted($"Connecting to session {mp.SessionCode}...");
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Waiting for the host to respond.");
        ImGui.Spacing();
        if (ImGui.Button("Cancel"))
            mp.LeaveSession();
    }

    // Fails fast on a blank/garbled field, not reachability. A bare "host:port" parses as an
    // absolute URI on its own (scheme "host"), so "://" is checked first.
    private static bool IsPlausibleRelayUrl(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Contains("://", StringComparison.Ordinal))
            return Uri.TryCreate(trimmed, UriKind.Absolute, out var explicitUri)
                && explicitUri.Scheme is "ws" or "wss" or "http" or "https";
        return Uri.TryCreate($"ws://{trimmed}", UriKind.Absolute, out var probe) && !string.IsNullOrEmpty(probe.Host);
    }

    private void DrawConnectPanel()
    {
        Wrapped("Point this at a relay server you or someone in your group is running " +
                "-- there is no default/public one. See Relay/README.md for how to stand " +
                "one up.");

        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("Relay URL##relayUrl", ref relayUrl, 256))
        {
            // Shows the password saved for this relay, if any. Hidden rather than cleared while
            // the address points elsewhere, so a typo doesn't lose it.
            relayToken = plugin.Configuration.TokenForRelay(relayUrl);
            plugin.Configuration.RelayServerUrl = relayUrl;
            plugin.Configuration.Save();
        }
        var validUrl = IsPlausibleRelayUrl(relayUrl);
        if (!validUrl)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.4f, 1f),
                string.IsNullOrWhiteSpace(relayUrl)
                    ? "Enter your relay's address, e.g. relay.example.com or 203.0.113.5:7890"
                    : "Doesn't look like a valid relay address.");
        }
        // Re-checked once per distinct valid URL; unreachable/old-relay failures default to
        // "no token needed".
        else if (relayInfoCheckedForUrl != relayUrl)
        {
            relayInfoCheckedForUrl = relayUrl;
            relayRequiresToken = null;
            var urlSnapshot = relayUrl;
            _ = RelayClient.FetchInfoAsync(urlSnapshot).ContinueWith(t =>
            {
                if (t.Result is { } info)
                    Plugin.Framework.Run(() => { if (relayInfoCheckedForUrl == urlSnapshot) relayRequiresToken = info.RequiresToken; });
            });
        }

        if (relayRequiresToken == true)
        {
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("Relay password##relayToken", ref relayToken, 128, ImGuiInputTextFlags.Password))
            {
                plugin.Configuration.RelayAccessToken = relayToken;
                plugin.Configuration.RelayTokenOrigin = Configuration.OriginOf(relayUrl);
                plugin.Configuration.Save();
            }
        }

        ImGui.SetNextItemWidth(200);
        ImGui.InputText("Display name", ref displayName, 64);

        if (mp.ConnectionError is { } err)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), $"Connection failed: {err}");
        }
        else if (mp.SessionEndReason is { } endReason)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), endReason);
        }

        var missingRequiredToken = relayRequiresToken == true && string.IsNullOrEmpty(relayToken);

        ImGui.Spacing();
        ImGui.BeginDisabled(!validUrl || missingRequiredToken);
        if (ImGui.Button("Host new session"))
        {
            mp.DisplayName = displayName;
            mp.HostSession(relayUrl.Trim());
        }
        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.SetNextItemWidth(140);
        ImGui.InputText("##joincode", ref joinCode, 16, ImGuiInputTextFlags.CharsUppercase);
        ImGui.SameLine();
        ImGui.BeginDisabled(!validUrl || missingRequiredToken || string.IsNullOrWhiteSpace(joinCode));
        if (ImGui.Button("Join session"))
        {
            mp.DisplayName = displayName;
            mp.JoinSession(relayUrl.Trim(), joinCode);
        }
        ImGui.EndDisabled();
    }

    private void DrawConnectedPanel()
    {
        var stable = mp.IsConnected;
        if (stable)
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), "● Connected to relay");
        else if (mp.IsReconnecting)
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), $"● Reconnecting to relay... (attempt {mp.ReconnectAttempt})");
        else
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "● Not connected to relay");
        if (!stable && mp.ConnectionError is { } connErr)
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.4f, 1f), $"Last error: {connErr}");

        if (stable)
        {
            ImGui.SameLine();
            ImGui.TextColored(mp.IsEncrypted ? new Vector4(0.4f, 0.9f, 0.4f, 1f) : new Vector4(1f, 0.7f, 0.3f, 1f),
                mp.IsEncrypted ? "(encrypted)"
                : mp.FellBackToUnencrypted ? "(NOT encrypted -- this relay doesn't support wss://)"
                : "(NOT encrypted)");
        }
        if (stable && !mp.SupportsCompression)
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "This relay does not support compression.");
        if (stable && !mp.RelayAttestsSender)
        {
            WrappedColored(new Vector4(1f, 0.55f, 0.15f, 1f), "⚠ This relay can't tell who sent a message -- anyone in the session could act as the host.");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Only join sessions of people you trust on this relay, or update the relay (senderIdentity support).");
        }

        if (mp.IsHost && mp.SessionCode == null)
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Requesting a session code from the relay...");
        else if (mp.IsHost && mp.SessionCode != null)
        {
            ImGui.TextUnformatted("Session code:");
            ImGui.SetWindowFontScale(2f);
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), mp.SessionCode);
            ImGui.SetWindowFontScale(1f);
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy")) ImGui.SetClipboardText(mp.SessionCode);
            Wrapped("Share this code and your relay URL with whoever is joining.");
        }
        else
        {
            ImGui.TextUnformatted(mp.Session.Started ? "Running." : "Connected -- waiting for the host to start.");
        }

        // Shown with both checksums rather than as a silently rejected Claim.
        var myMismatchVsHost = !mp.IsHost && mp.IsVersionMismatched(mp.Session.HostId);
        if (myMismatchVsHost)
        {
            var hostBuild = mp.Session.Builds.GetValueOrDefault(mp.Session.HostId);
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.15f, 1f),
                "⚠ Different AnoMech build than the host -- update before claiming a role.");
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.15f, 1f),
                $"Yours: {PluginBuildInfo.Version} ({PluginBuildInfo.ShortChecksum})   Host: {hostBuild?.Version ?? "?"} ({hostBuild?.ShortChecksum ?? "?"})");
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Roles:");
        for (var i = 0; i < 8; i++)
        {
            var role = (PartyRole)i;
            var claimed = mp.Session.ClaimedBy.TryGetValue(role, out var peerId);
            var mine = claimed && peerId == mp.MyPeerId;
            // The host doesn't ping itself, so its row tracks time since its last broadcast.
            var isHostRow = claimed && !mine && peerId == mp.Session.HostId;
            var stale = claimed && !mine && (isHostRow ? mp.IsHostStale : mp.IsPeerStale(peerId));
            var mismatched = claimed && !mine && mp.IsVersionMismatched(peerId);
            var label = claimed
                ? mp.Session.NameOf(peerId) + (mine ? " (you)" : "") + (stale ? " (disconnected?)" : "") + (mismatched ? " (version mismatch!)" : "")
                : "(open, bot)";

            ImGui.TextUnformatted(RoleLabels[i]);
            ImGui.SameLine(50);
            if (claimed && !mine)
            {
                if (isHostRow)
                    DrawHostStatusDot(mp.IsHostStale, mp.SecondsSinceHostMessage);
                else
                    DrawStatusDot(mp.GetPeerStatus(peerId), stale);
                ImGui.SameLine();
            }
            if (mismatched)
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.15f, 1f), label);
            else if (stale)
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), label);
            else
                ImGui.TextUnformatted(label);
            if (mismatched && ImGui.IsItemHovered())
            {
                var theirs = mp.Session.Builds.GetValueOrDefault(peerId);
                ImGui.SetTooltip($"Different plugin build than yours.\nYours: {PluginBuildInfo.Version} ({PluginBuildInfo.ShortChecksum})\nTheirs: {theirs?.Version ?? "?"} ({theirs?.ShortChecksum ?? "?"})\nUpdate to matching versions before starting.");
            }
            ImGui.SameLine(240);

            ImGui.PushID(i);
            ImGui.BeginDisabled(mp.Session.Started || (claimed && !mine) || (!claimed && myMismatchVsHost));
            if (mine)
            {
                if (ImGui.SmallButton("Release")) mp.ReleaseRole();
            }
            else if (ImGui.SmallButton("Claim"))
            {
                mp.ClaimRole(role);
            }
            ImGui.EndDisabled();
            if (mp.IsHost && claimed && !mine)
            {
                ImGui.SameLine();
                DrawKickButton(peerId);
            }
            ImGui.PopID();
        }

        // Names has everyone who said Hello, seated or not.
        var unclaimed = mp.Session.Names.Keys
            .Where(id => id != mp.MyPeerId && !mp.Session.ClaimedBy.ContainsValue(id))
            .ToList();
        if (unclaimed.Count > 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Connected, no role yet:");
            foreach (var id in unclaimed)
            {
                ImGui.PushID(id.ToString());
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextUnformatted(mp.Session.NameOf(id) + (mp.IsVersionMismatched(id) ? " (version mismatch)" : ""));
                if (mp.IsHost)
                {
                    ImGui.SameLine();
                    DrawKickButton(id);
                }
                ImGui.PopID();
            }
        }

        DrawBannedList();

        ImGui.Separator();
        DrawScenarioSettings();

        // Locked once Started: the choreography only makes sense replayed from a fresh Start.
        {
            var botControlled = mp.DebugBotControlled;
            ImGui.BeginDisabled(mp.Session.Started);
            if (ImGui.Checkbox("Debug AI bot", ref botControlled))
                mp.SetDebugBotControlled(botControlled);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Testing aid -- your own claimed role gets driven locally by the " +
                                  "same AI a host-side bot in that role would use, instead of you. " +
                                  "Entirely client-side; the host sees no difference. Locked once the " +
                                  "fight starts.");
        }

        ImGui.Separator();
        if (!mp.Session.Started)
        {
            DrawStartButton();
            ImGui.SameLine();
        }

        DrawLeaveSessionButton();
    }

    private const string MechanicsPopupId = "Mechanics###AnoMechAssignMechanics";

    // The mechanic a given player carries (a number, an Accretion, a tether) is a different
    // question from the fight-wide rolls, so it gets its own dialog rather than another section
    // inside the scenario settings panel.
    internal static void DrawAssignMechanicsButton(IScenario? scenario, bool locked, string? lockedReason)
    {
        if (scenario is not { HasPerPlayerSettings: true }) return;
        ImGui.BeginDisabled(locked);
        if (ImGui.Button("Assign specific mechanics to players")) ImGui.OpenPopup(MechanicsPopupId);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(locked && lockedReason != null
                ? lockedReason
                : $"Choose which mechanic each player gets in {scenario.Name}. Anyone left on Auto gets the fight's own roll.");

        if (!ImGui.BeginPopup(MechanicsPopupId)) return;
        ImGui.TextUnformatted($"{scenario.Name}: mechanics per player");
        ImGui.TextDisabled(PerRole.SeatsActive
            ? "Pick a seat, then what that player gets. Anyone left on Auto gets the fight's own roll."
            : "Yours only. Anything left on Auto gets the fight's own roll.");
        ImGui.Separator();
        scenario.DrawPerPlayerSettings();
        ImGui.Separator();
        if (ImGui.Button("Done")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawKickButton(Guid peerId) => DrawKickBanButtons(mp, peerId);

    // Both sit a few pixels from Claim, and neither is something to do by accident, so each
    // asks first. Shared with RunningSimWindow so the wording can't drift.
    internal static void DrawKickBanButtons(MultiplayerManager mp, Guid peerId)
    {
        ImGui.PushID(peerId.ToString());
        var who = mp.Session.NameOf(peerId);

        if (ImGui.SmallButton("Kick")) ImGui.OpenPopup("##confirmkick");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Remove {who} from the session; mid-fight it ends the run for everyone. Asks first.");
        if (ImGui.BeginPopup("##confirmkick"))
        {
            ImGui.TextUnformatted($"Kick {who}?");
            ImGui.TextDisabled("They can come back with the session code.");
            if (ImGui.Button("Kick them")) { mp.KickPeer(peerId); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##kick")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Ban")) ImGui.OpenPopup("##confirmban");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Remove {who} and keep them out of this session until you unban them. Asks first.");
        if (ImGui.BeginPopup("##confirmban"))
        {
            ImGui.TextUnformatted($"Ban {who}?");
            ImGui.TextDisabled("They stay out until you unban them in the Multiplayer window.");
            if (ImGui.Button("Ban them")) { mp.BanPeer(peerId); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##ban")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    // A ban lasts the session.
    private void DrawBannedList()
    {
        if (!mp.IsHost || mp.BannedPeers.Count == 0) return;
        if (!ImGui.CollapsingHeader($"Banned players ({mp.BannedPeers.Count})##banned")) return;
        foreach (var (id, name) in mp.BannedPeers.ToList())
        {
            ImGui.PushID(id.ToString());
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextUnformatted(name);
            ImGui.SameLine();
            if (ImGui.SmallButton("Unban")) mp.UnbanPeer(id);
            ImGui.PopID();
        }
    }

    // Host-editable here (the main window's copy is disabled while connected), mirrored
    // read-only to everyone else. A panel's per-player rows get their own seat picker
    // (SettingsGrid.SeatRow). Speed is deliberately absent: a session always runs at 1x.
    private void DrawScenarioSettings()
    {
        if (mp.IsHost)
        {
            var scenario = Plugin.MainWindow.SelectedScenario;
            mp.PublishSelectedScenario(scenario);
            if (ImGui.CollapsingHeader("Scenario settings##mpsettings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                using var compact = new CompactScope(compactFont);
                if (scenario is not { SupportsMultiplayer: true })
                {
                    ImGui.TextDisabled("Select a multiplayer-supported scenario in the main window.");
                }
                else
                {
                    ImGui.BeginGroup();
                    ImGui.BeginDisabled(mp.Session.Started);
                    scenario.DrawSettings();
                    ImGui.EndDisabled();
                    ImGui.EndGroup();
                    if (mp.Session.Started && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip("Locked while the fight is running -- changes apply to the next start.");
                    DrawAssignMechanicsButton(scenario, mp.Session.Started,
                                              "Locked while the fight is running -- changes apply to the next start.");
                    scenario.DrawMultiplayerSettings();
                }
            }
            mp.PublishScenarioSettings(scenario);
            return;
        }

        if (!ImGui.CollapsingHeader("Scenario settings (set by the host)##mpsettings", ImGuiTreeNodeFlags.DefaultOpen)) return;
        using var compactSummary = new CompactScope(compactFont);
        var lines = mp.Session.ScenarioSettings;
        if (lines.Count == 0) ImGui.TextDisabled("Everything random -- the host hasn't forced anything.");
        foreach (var line in lines) ImGui.BulletText(line);
    }

    // Self-contained (no params) so RunningSimWindow can call this directly while running.
    internal void DrawStartButton()
    {
        var stable = mp.IsConnected;
        if (mp.RunEndReason is { } runEnd)
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.35f, 1f), $"Last run ended: {runEnd}");
        if (mp.IsHost)
        {
            if (mp.IsStartCheckPending)
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), "Checking everyone's ready...");
            else if (mp.StartWaitingOn is { } waitingOn)
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), $"Waiting for {waitingOn} to settle before starting...");
            else if (mp.StartCheckFailureReason is { } startFail)
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), startFail);

            var anyMismatch = mp.Session.ClaimedBy.Values.Any(mp.IsVersionMismatched);
            var hasSupportedScenario = Plugin.MainWindow.SelectedScenario is { } sel2
                && sel2.SupportsMultiplayer;
            var hasStrat = hasSupportedScenario && Plugin.MainWindow.HasStartableStrat();
            var conflicts = Plugin.MainWindow.SelectedScenario?.SettingsConflicts ?? [];
            // A connected person without a role would be left behind at Start.
            var claimedPeerIds = mp.Session.ClaimedBy.Values.ToHashSet();
            var everyoneHasClaimed = mp.Session.Names.Keys.All(claimedPeerIds.Contains);
            var canStart = stable && mp.MyClaimedRole != null && !anyMismatch && !mp.IsStartCheckPending
                           && mp.StartWaitingOn == null && hasStrat && everyoneHasClaimed && conflicts.Count == 0;
            if (conflicts.Count > 0)
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f),
                                  $"Can't start: {conflicts.Count} impossible setting{(conflicts.Count == 1 ? "" : "s")} in Scenario settings.");
            ImGui.BeginDisabled(!canStart);
            if (ImGui.Button("Start")) mp.StartScenario();
            ImGui.EndDisabled();
            if (!canStart && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(!stable
                    ? "Not connected to the relay."
                    : !hasSupportedScenario
                        ? "Select a multiplayer-supported scenario in the main window first."
                        : !hasStrat
                            ? "No strat available for the selected scenario/region."
                            : conflicts.Count > 0
                            ? $"The fight can't produce these settings together:\n{string.Join("\n", conflicts)}"
                            : anyMismatch
                            ? "One or more players are on a different plugin build -- everyone needs to match before starting."
                            : mp.StartWaitingOn is { } waiting
                            ? $"Waiting for {waiting} to settle before starting..."
                            : mp.IsStartCheckPending
                                ? "Waiting for players to confirm they're ready..."
                                : mp.MyClaimedRole == null
                                    ? "Claim a role for yourself first."
                                    : "Everyone connected needs to claim a role first.");
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("Start (controlled by host)");
            ImGui.EndDisabled();
        }
    }

    internal void DrawLeaveSessionButton()
    {
        if (ImGui.Button("Leave session"))
        {
            mp.LeaveSession();
            // Leave() assumes a zone was entered.
            if (plugin.Game.World.Map.IsInInstance) plugin.Game.Leave();
        }
    }

    // Time since last broadcast, not latency, so no "fair" band.
    private static void DrawHostStatusDot(bool stale, float secondsSince)
    {
        var color = stale ? new Vector4(1f, 0.35f, 0.35f, 1f) : new Vector4(0.4f, 0.9f, 0.4f, 1f);
        ImGui.TextColored(color, "●");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(stale
                ? $"No message from the host in {secondsSince:F0}s -- likely disconnected."
                : $"Host -- last message {secondsSince:F0}s ago.");
    }

    // Grey for "no number yet", so the dot never reads as a suspicious 0ms.
    private static void DrawStatusDot(PeerStatusEntry? status, bool stale)
    {
        Vector4 color;
        string tooltip;
        if (stale)
        {
            color = new Vector4(1f, 0.35f, 0.35f, 1f);
            tooltip = "No message received in a while -- likely disconnected.";
        }
        else if (status is not { } s)
        {
            color = new Vector4(0.6f, 0.6f, 0.6f, 1f);
            tooltip = "Connected -- waiting for a status update...";
        }
        else if (s.LatencyMs is not { } ms)
        {
            color = new Vector4(0.6f, 0.6f, 0.6f, 1f);
            tooltip = "Connected -- measuring ping...";
        }
        else if (ms < 100f)
        {
            color = new Vector4(0.4f, 0.9f, 0.4f, 1f);
            tooltip = $"Ping: {ms:F0}ms (good)";
        }
        else if (ms <= 300f)
        {
            color = new Vector4(0.95f, 0.85f, 0.3f, 1f);
            tooltip = $"Ping: {ms:F0}ms (fair)";
        }
        else
        {
            color = new Vector4(1f, 0.4f, 0.4f, 1f);
            tooltip = $"Ping: {ms:F0}ms (poor)";
        }
        if (status is { } shown)
            tooltip += $"\nLast message: {shown.SecondsSinceLastSeen:F0}s ago";

        ImGui.TextColored(color, "●");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }
}
