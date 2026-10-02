using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

// Never delivered, so callers take their synthetic fallback.
internal sealed class FakeRawActionEffect : IRawActionEffect
{
    public bool TryInject(uint entityId, float rotation, byte[] capture, ushort opcode, string capturedGameVersion, string what) => false;
}
