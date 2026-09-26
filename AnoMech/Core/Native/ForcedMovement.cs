using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Network;

namespace AnoMech.Core.Native;

// Push is the sim's own slide (Movement.Carry).
public enum CarryMode
{
    Native,
    NativeSelfTarget,
    Push,
}

// ActorControl 241 is the server's carry of an actor to a point, facing held.
internal static class ForcedMovement
{
    private const uint Category = 241;
    private const uint Pacing = 0x00011830u;
    private const uint NoTarget = 0xE0000000u;
    private static uint sequence;

    public static void CarryTo(uint entityId, Vector3 worldDestination, float rotation, bool selfTarget)
    {
        var arg1 = ((uint)QuantizePosition(worldDestination.X) << 16) | QuantizePosition(worldDestination.Y);
        var arg2 = ((uint)QuantizePosition(worldDestination.Z) << 16) | QuantizeRotation(rotation);
        var seq = ++sequence;
        DiagnosticLog.Info($"[ForcedMovement] ActorControl {Category} -> 0x{entityId:X}: dest=({worldDestination.X:F2},{worldDestination.Y:F2},{worldDestination.Z:F2}) rot={rotation:F2} target={(selfTarget ? "self" : "none")} seq={seq}.");
        PacketDispatcher.HandleActorControlPacket(entityId, Category, arg1, arg2, Pacing, seq, 0, 0, 0, 0, selfTarget ? entityId : NoTarget, false);
    }

    private static ushort QuantizePosition(float v) => (ushort)Math.Clamp(MathF.Round((v + 1000f) / 2000f * 65535f), 0f, 65535f);

    private static ushort QuantizeRotation(float r) => (ushort)Math.Clamp(MathF.Round((r + MathF.PI) / MathF.Tau * 65535f), 0f, 65535f);
}
