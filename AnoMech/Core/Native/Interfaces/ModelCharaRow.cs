namespace AnoMech.Core.Native.Interfaces;

// Radius is the unscaled hitbox radius; 0 defers to the skeleton's ModelSkeletonRow.Radius.
public sealed record ModelCharaRow(uint Id, byte Type, ushort Model, float Radius);
