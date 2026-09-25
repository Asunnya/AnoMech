using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.M9s.Aetherletting;

public sealed class M9sAetherlettingSettingsWindow
{
    public M9sAetherlettingStateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto")) Overrides.Spin = null;

        if (SettingsGrid.Begin("##m9saetherletting"))
        {
            SettingsGrid.Row("Cone spin:");
            if (ImGui.RadioButton("Auto##conespin", Overrides.Spin == null)) Overrides.Spin = null;
            ImGui.SameLine();
            if (ImGui.RadioButton("CW##conespin", Overrides.Spin == ConeSpin.Clockwise)) Overrides.Spin = ConeSpin.Clockwise;
            ImGui.SameLine();
            if (ImGui.RadioButton("CCW##conespin", Overrides.Spin == ConeSpin.CounterClockwise)) Overrides.Spin = ConeSpin.CounterClockwise;
            SettingsGrid.End();
        }
    }
}
