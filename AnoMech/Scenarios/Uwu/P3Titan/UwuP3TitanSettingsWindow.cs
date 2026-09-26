using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Uwu.P3Titan;

public sealed class UwuP3TitanSettingsWindow
{
    public UwuP3TitanStateOverrides Overrides { get; } = new();

    public void Draw()
    {
        var always = Overrides.PlayerAlwaysInFirstGaols;
        if (ImGui.Checkbox("Always one of the three first gaols##titangaols", ref always))
            Overrides.PlayerAlwaysInFirstGaols = always;
    }
}
