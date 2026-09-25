using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.M9s.VampStomp2;

public sealed class M9sVampStomp2SettingsWindow
{
    public M9sVampStomp2StateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto"))
        {
            Overrides.Spin = null;
            Overrides.Order = null;
        }

        if (SettingsGrid.Begin("##m9svampstomp2"))
        {
            SettingsGrid.Row("Bat spin:");
            if (ImGui.RadioButton("Auto##batspin2", Overrides.Spin == null)) Overrides.Spin = null;
            ImGui.SameLine();
            if (ImGui.RadioButton("CW##batspin2", Overrides.Spin == BatSpin.Clockwise)) Overrides.Spin = BatSpin.Clockwise;
            ImGui.SameLine();
            if (ImGui.RadioButton("CCW##batspin2", Overrides.Spin == BatSpin.CounterClockwise)) Overrides.Spin = BatSpin.CounterClockwise;

            SettingsGrid.Row("Half Moon:");
            if (ImGui.RadioButton("Auto##halfmoon2", Overrides.Order == null)) Overrides.Order = null;
            ImGui.SameLine();
            if (ImGui.RadioButton("Left first##halfmoon2", Overrides.Order == CleaveOrder.LeftFirst)) Overrides.Order = CleaveOrder.LeftFirst;
            ImGui.SameLine();
            if (ImGui.RadioButton("Right first##halfmoon2", Overrides.Order == CleaveOrder.RightFirst)) Overrides.Order = CleaveOrder.RightFirst;
            SettingsGrid.End();
        }
    }
}
