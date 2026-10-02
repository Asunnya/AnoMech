using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

internal sealed class FakeMapEffects : IMapEffects
{
    public bool Loaded { get; set; }

    public bool Apply(uint packetFlags, byte index) => Loaded;
    public bool SuppressSlot(byte index) => Loaded;
    public void KeepSlotSuppressed(byte index) { }
    public void RestoreSlot(byte index) { }
    public IReadOnlyCollection<byte> SuppressedSlots => [];
    public void ForgetSuppressions() { }
    public void LogAllSlots(string label) { }
}
