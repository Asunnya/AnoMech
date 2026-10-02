namespace AnoMech.Core.Native.Interfaces;

// The limit break gauge the HUD shows and the client gates presses on.
public interface ILimitBreakController
{
    // Null when the controller isn't available.
    LimitBreakBars? Read();
    void Write(LimitBreakBars bars);
}

public readonly record struct LimitBreakBars(byte BarCount, ushort CurrentUnits, ushort BarUnits);
