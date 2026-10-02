namespace AnoMech.Core.Native.Interfaces;

public interface IRawActionEffect
{
    // Replays a captured ActionEffect packet body on the actor, patched to it and its rotation.
    // False when it wasn't delivered (wrong game version, no actor); the caller falls back to a
    // synthetic effect.
    bool TryInject(uint entityId, float rotation, byte[] capture, ushort opcode, string capturedGameVersion, string what);
}
