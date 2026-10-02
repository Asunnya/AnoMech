using System;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace AnoMech.Core.Game.Party;

// The party's faked limit break gauge. Solo in the inn the real gauge is empty, so a scenario
// that grants one has it written into LimitBreakController every tick, for display only: the
// real values are saved once and written back when the party despawns. The client gates presses
// on it; the request packet is eaten by the firewall.
public sealed unsafe class LimitBreakGauge
{
    private const ushort UnitsPerBar = 10000;
    private const byte Bars = 3;

    // Null until a scenario grants a gauge; the native one is then left alone.
    private ushort? units;
    private (byte BarCount, ushort CurrentUnits, ushort BarUnits)? saved;

    public int FilledBars => (units ?? 0) / UnitsPerBar;

    public void Set(float bars)
        => units = (ushort)(Math.Clamp(bars, 0f, Bars) * UnitsPerBar);

    public void Spend()
    {
        if (units != null) units = 0;
    }

    internal void Tick()
    {
        if (units is not { } current) return;
        var lb = LimitBreakController.Instance();
        if (lb == null) return;
        saved ??= (lb->BarCount, lb->CurrentUnits, lb->BarUnits);
        lb->BarCount = Bars;
        lb->BarUnits = UnitsPerBar;
        lb->CurrentUnits = current;
    }

    internal void Restore()
    {
        units = null;
        if (saved is not { } s) return;
        saved = null;
        var lb = LimitBreakController.Instance();
        if (lb == null) return;
        lb->BarCount = s.BarCount;
        lb->CurrentUnits = s.CurrentUnits;
        lb->BarUnits = s.BarUnits;
    }
}
