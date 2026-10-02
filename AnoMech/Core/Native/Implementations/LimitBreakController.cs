using AnoMech.Core.Native.Interfaces;
using NativeLimitBreakController = FFXIVClientStructs.FFXIV.Client.Game.UI.LimitBreakController;

namespace AnoMech.Core.Native.Implementations;

internal sealed unsafe class LimitBreakController : ILimitBreakController
{
    public LimitBreakBars? Read()
    {
        var lb = NativeLimitBreakController.Instance();
        return lb == null ? null : new LimitBreakBars(lb->BarCount, lb->CurrentUnits, lb->BarUnits);
    }

    public void Write(LimitBreakBars bars)
    {
        var lb = NativeLimitBreakController.Instance();
        if (lb == null) return;
        lb->BarCount = bars.BarCount;
        lb->BarUnits = bars.BarUnits;
        lb->CurrentUnits = bars.CurrentUnits;
    }
}
