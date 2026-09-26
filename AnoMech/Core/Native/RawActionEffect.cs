using System;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace AnoMech.Core.Native;

// Replays a captured ActionEffect packet on one actor through the client's own dispatcher, the
// way the real server delivers it. Host and peer both call this against their own local
// carrier, so a raw-packet delivery renders on every client; only the per-instance header
// fields are patched. Refused when the running client isn't the build the capture came from,
// since opcodes are reassigned every patch.
public static unsafe class RawActionEffect
{
    // Header fields every ActionEffect body starts with.
    private const int AnimationTargetIdOffset = 0x00;
    private const int AnimationTargetTypeOffset = 0x04;
    private const int GlobalSequenceOffset = 0x0C;
    private const int RotationOffset = 0x1A;
    private const int MinimumBodyLength = RotationOffset + 2;

    // Climbs monotonically from above anything the real server has sent this session: a stale
    // counter is the one plausible reason the dispatcher would drop a replayed effect.
    private static uint counter = 40000;
    private static bool versionWarned;

    public static bool TryInject(SimWorld world, SimEnemy carrier, byte[] capture, ushort opcode, string capturedGameVersion, string what)
    {
        if (capture.Length < MinimumBodyLength)
        {
            DiagnosticLog.Warn($"[RawActionEffect] {what}: capture is {capture.Length} bytes, too short to patch.");
            return false;
        }
        var entityId = carrier.EntityId;
        if (entityId == 0)
        {
            DiagnosticLog.Warn($"[RawActionEffect] {what}: carrier at {carrier.Position} has no native actor -- native effect instead.");
            return false;
        }
        var version = new string(Framework.Instance()->GameVersionString);
        if (version != capturedGameVersion)
        {
            if (!versionWarned)
                DiagnosticLog.Warn($"[RawActionEffect] {what}: client is {version}, capture is {capturedGameVersion} -- opcode 0x{opcode:X4} can't be trusted, native effect instead.");
            versionWarned = true;
            return false;
        }

        var body = (byte[])capture.Clone();
        BitConverter.TryWriteBytes(body.AsSpan(AnimationTargetIdOffset, 4), entityId);
        BitConverter.TryWriteBytes(body.AsSpan(AnimationTargetTypeOffset, 4), 0u);
        var seen = world.Map.MaxSeenActionEffectCounter;
        if (counter <= seen) counter = seen;   // the next ++ makes it max+1, then +2, ...
        BitConverter.TryWriteBytes(body.AsSpan(GlobalSequenceOffset, 4), ++counter);
        BitConverter.TryWriteBytes(body.AsSpan(RotationOffset, 2), MathUtil.QuantizeRotation(carrier.Rotation));
        return world.Map.InjectIncomingPacket(entityId, opcode, body, $"{what}, carrier {carrier.DisplayName} at {carrier.Position}");
    }
}
