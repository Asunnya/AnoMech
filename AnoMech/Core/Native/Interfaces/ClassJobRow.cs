namespace AnoMech.Core.Native.Interfaces;

// Limit break action ids; 0 where the job has none at that level.
public sealed record ClassJobRow(uint Id, uint LimitBreak1, uint LimitBreak2, uint LimitBreak3);
