using System;
using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Umad.P5Flood;

// Settings panel for the P5 Flood scenario: pick or randomize each diagonal's march direction.
// Mirrors UmadP5ExaflaresSettingsWindow.
public sealed class UmadP5FloodSettingsWindow
{
    public UmadP5FloodStateOverrides Overrides { get; } = new();

    // Index-aligned with the FloodDirection enum.
    private static readonly string[] DirectionLabels = ["Random", "Forward", "Reversed"];

    private static readonly string[] FirstLabels = ["Random", "NE-SW", "NW-SE"];

    // Index-aligned with AnchorRoles below (0 = a fresh random non-tank every tick).
    private static readonly string[] AnchorLabels =
        ["Random per tick", "Regen healer", "Shield healer", "Melee A", "Melee B", "Phys ranged", "Caster"];
    private static readonly PartyRole[] AnchorRoles =
    [
        PartyRole.RegenHealer, PartyRole.ShieldHealer,
        PartyRole.MeleeDpsA, PartyRole.MeleeDpsB, PartyRole.PhysRangedDps, PartyRole.CasterDps,
    ];

    // Index-aligned with UmadP5FloodState.StartQuadrant; "Random" = derive it from the line rolls.
    private static readonly string[] QuadrantLabels = ["Random", "North", "East", "South", "West"];
    private static readonly string[] RotationLabels = ["Random", "Clockwise", "Counter-clockwise"];

#if DEBUG
    // Index-aligned with FloodCarrierMode.
    private static readonly string[] CarrierLabels =
        ["Chaos, draw hidden", "Chaos, model hidden", "Chaos, visible", "Empty human (480)", "Real packet (9020)"];

    // Index-aligned with FloodWaveDelivery.
    private static readonly string[] DeliveryLabels =
        ["Native effect", "Raw packet replay", "Effect + loop hold", "Effect + base hold", "Direct timeline"];
#endif

    public void Draw()
    {
        if (ImGui.Button("Auto"))
        {
            Overrides.LineNeSw = FloodDirection.Random;
            Overrides.LineNwSe = FloodDirection.Random;
            Overrides.NeSwFirst = null;
            Overrides.AnchorRole = null;
            Overrides.StartQuadrant = null;
            Overrides.RotationClockwise = null;
            Overrides.CarrierMode = FloodCarrierMode.RealPacket;
            Overrides.WaveAnimLock = false;
            Overrides.WaveDelivery = FloodWaveDelivery.NativeEffect;
            Overrides.WaveOnKefka = false;
            Overrides.PreloadWaveTimelines = true;
            Overrides.WaveForceLoad = false;
            Overrides.VfxRenderLog = false;
        }

        if (SettingsGrid.Begin("##p5flood"))
        {
            SettingsGrid.FightOnlyNote();
            DrawDirectionRow("NE-SW line:", "##nesw", Overrides.LineNeSw, v => Overrides.LineNeSw = v);
            DrawDirectionRow("NW-SE line:", "##nwse", Overrides.LineNwSe, v => Overrides.LineNwSe = v);

            SettingsGrid.Row("Leads first:");
            var idx = Overrides.NeSwFirst switch { true => 1, false => 2, null => 0 };
            SettingsGrid.ItemWidth(140);
            if (ImGui.Combo("##firstline", ref idx, FirstLabels, FirstLabels.Length))
                Overrides.NeSwFirst = idx switch { 1 => true, 2 => false, _ => null };

            SettingsGrid.Row("Stack target:");
            var anchorIdx = Overrides.AnchorRole is { } r ? Array.IndexOf(AnchorRoles, r) + 1 : 0;
            SettingsGrid.ItemWidth(140);
            if (ImGui.Combo("##anchorrole", ref anchorIdx, AnchorLabels, AnchorLabels.Length))
                Overrides.AnchorRole = anchorIdx == 0 ? null : AnchorRoles[anchorIdx - 1];

            SettingsGrid.Row("Starting quadrant:");
            var quadIdx = (Overrides.StartQuadrant ?? -1) + 1;
            SettingsGrid.ItemWidth(140);
            if (ImGui.Combo("##startquadrant", ref quadIdx, QuadrantLabels, QuadrantLabels.Length))
                Overrides.StartQuadrant = quadIdx == 0 ? null : quadIdx - 1;

            SettingsGrid.Row("Rotation:");
            var rotIdx = Overrides.RotationClockwise switch { true => 1, false => 2, null => 0 };
            SettingsGrid.ItemWidth(160);
            if (ImGui.Combo("##rotationdir", ref rotIdx, RotationLabels, RotationLabels.Length))
                Overrides.RotationClockwise = rotIdx switch { 1 => true, 2 => false, _ => null };

#if DEBUG
            SettingsGrid.Row("Wave carrier (debug):");
            var carrierIdx = (int)Overrides.CarrierMode;
            SettingsGrid.ItemWidth(160);
            if (ImGui.Combo("##carriermode", ref carrierIdx, CarrierLabels, CarrierLabels.Length))
                Overrides.CarrierMode = (FloodCarrierMode)carrierIdx;

            SettingsGrid.Row("Wave delivery (debug):");
            var deliveryIdx = (int)Overrides.WaveDelivery;
            SettingsGrid.ItemWidth(160);
            if (ImGui.Combo("##wavedelivery", ref deliveryIdx, DeliveryLabels, DeliveryLabels.Length))
                Overrides.WaveDelivery = (FloodWaveDelivery)deliveryIdx;

            SettingsGrid.Row("Wave AnimLock (debug):");
            var animLock = Overrides.WaveAnimLock;
            if (ImGui.Checkbox("Hold carrier in AnimLock##waveanimlock", ref animLock)) Overrides.WaveAnimLock = animLock;

            SettingsGrid.Row("Wave on Kefka (debug):");
            var onKefka = Overrides.WaveOnKefka;
            if (ImGui.Checkbox("Also fire each wave from Kefka##waveonkefka", ref onKefka)) Overrides.WaveOnKefka = onKefka;

            SettingsGrid.Row("Wave preload (debug):");
            var preload = Overrides.PreloadWaveTimelines;
            if (ImGui.Checkbox("Preload wave timelines at start##wavepreload", ref preload)) Overrides.PreloadWaveTimelines = preload;

            SettingsGrid.Row("Wave force-load (debug):");
            var forceLoad = Overrides.WaveForceLoad;
            if (ImGui.Checkbox("LoadTimelineResources after each wave##waveforceload", ref forceLoad)) Overrides.WaveForceLoad = forceLoad;

            SettingsGrid.Row("VFX render log (debug):");
            var vfxLog = Overrides.VfxRenderLog;
            if (ImGui.Checkbox("Trace every VFX create and destroy##vfxrenderlog", ref vfxLog)) Overrides.VfxRenderLog = vfxLog;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hooks a game destructor that runs for every VFX in the world. Only for investigating what did or didn't render.");
#endif

            SettingsGrid.End();
        }
    }

    private static void DrawDirectionRow(string label, string id, FloodDirection current, Action<FloodDirection> set)
    {
        SettingsGrid.Row(label);
        var idx = (int)current;
        SettingsGrid.ItemWidth(140);
        if (ImGui.Combo(id, ref idx, DirectionLabels, DirectionLabels.Length))
            set((FloodDirection)idx);
    }
}
