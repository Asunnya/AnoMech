using AnoMech.Core.Game;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;

namespace AnoMech.Tests;

// EventObjectManager's 40-slot pool.
internal sealed class FakeEventObjects : IEventObjects
{
    private const int SlotCount = 40;
    private const uint EntityIdBase = 0x40100000u;

    private readonly FakeEventObject?[] slots = new FakeEventObject?[SlotCount];

    public IEventObjectProxy? Spawn(EventObjectSpawnConfig config, Placement placement)
    {
        var slot = Array.FindIndex(slots, o => o is not { Exists: true });
        if (slot < 0) return null;
        var entityId = config.EntityId != 0 ? config.EntityId : EntityIdBase + (uint)slot;
        var obj = new FakeEventObject(slot, entityId, placement.Position, placement.Rotation, config.TimelineState);
        slots[slot] = obj;
        return obj;
    }

    internal void Tick(float deltaSeconds)
    {
        foreach (var obj in slots)
            if (obj is { Exists: true }) obj.Tick(deltaSeconds);
    }
}
