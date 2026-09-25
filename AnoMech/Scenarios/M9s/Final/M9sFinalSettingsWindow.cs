using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.M9s.Final;

public sealed class M9sFinalSettingsWindow
{
    public M9sFinalStateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto"))
        {
            Overrides.Spin = null;
            Overrides.Order = null;
            Overrides.Enrage = false;
        }

        if (SettingsGrid.Begin("##m9sfinal"))
        {
            SettingsGrid.Row("Bat spin:");
            if (ImGui.RadioButton("Auto##batspin3", Overrides.Spin == null)) Overrides.Spin = null;
            ImGui.SameLine();
            if (ImGui.RadioButton("CW##batspin3", Overrides.Spin == BatSpin.Clockwise)) Overrides.Spin = BatSpin.Clockwise;
            ImGui.SameLine();
            if (ImGui.RadioButton("CCW##batspin3", Overrides.Spin == BatSpin.CounterClockwise)) Overrides.Spin = BatSpin.CounterClockwise;

            SettingsGrid.Row("Half Moon:");
            if (ImGui.RadioButton("Auto##halfmoon3", Overrides.Order == null)) Overrides.Order = null;
            ImGui.SameLine();
            if (ImGui.RadioButton("Left first##halfmoon3", Overrides.Order == CleaveOrder.LeftFirst)) Overrides.Order = CleaveOrder.LeftFirst;
            ImGui.SameLine();
            if (ImGui.RadioButton("Right first##halfmoon3", Overrides.Order == CleaveOrder.RightFirst)) Overrides.Order = CleaveOrder.RightFirst;

            SettingsGrid.Row("Ending:");
            var enrage = Overrides.Enrage;
            if (ImGui.Checkbox("Play out the enrage##enrage", ref enrage)) Overrides.Enrage = enrage;
            SettingsGrid.End();
        }
    }
}
