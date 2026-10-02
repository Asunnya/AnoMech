using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

// Solo in the inn the real gauge is empty.
internal sealed class FakeLimitBreakController : ILimitBreakController
{
    private LimitBreakBars bars;

    public LimitBreakBars? Read() => bars;
    public void Write(LimitBreakBars value) => bars = value;
}
