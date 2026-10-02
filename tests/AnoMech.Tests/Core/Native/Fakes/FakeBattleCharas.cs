using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;

namespace AnoMech.Tests;

// CharacterManager's slots and the local player, with BattleCharas' spawn recipes reduced to the
// fields the sim reads back.
internal sealed class FakeBattleCharas : IBattleCharas
{
    private const int SlotCount = 100;
    private const uint CreatedEntityIdBase = 0xE0000000u;
    private const uint PacketSpawnEntityIdBase = 0x4000FE00u;
    private const uint DoppelMaxHealth = 100_000;
    private const uint EnemyMaxHealth = 1_000_000;
    private const float DoppelHitboxRadius = 0.5f;
    private const byte IsTargetable = 0x02;
    private const int OrphanForgetFrames = 300;

    // UNVERIFIED: the engine builds a packet-spawned actor "a few frames" later.
    private const int PacketSpawnFrames = 3;

    private readonly FakeCharacter?[] slots = new FakeCharacter?[SlotCount];
    private readonly HashSet<int> reserved = [];
    private readonly List<(int Slot, FakeCharacter Actor, int FramesLeft)> packetSpawns = [];
    private readonly List<(int Slot, uint EntityId, int Frames)> orphans = [];

    public FakeCharacter Player { get; } = new(0x10000001u, "Player") { MaxHealth = 100_000, Health = 100_000, HitboxRadius = 0.5f, HasDrawObject = true, IsDrawObjectVisible = true };

    public IBattleCharaProxy LocalPlayer { get; }

    public FakeBattleCharas() => LocalPlayer = new FakeBattleChara(this, -1);

    internal FakeCharacter? ActorAt(int slot) => slot < 0 ? Player : slots[slot];

    internal void Free(int slot) => slots[slot] = null;

    public IBattleCharaProxy? SpawnBattleNpc(EnemySpawnConfig config, Placement placement)
    {
        var slot = FindFreeSlot();
        if (slot < 0) return null;
        var name = Natives.Data.BNpcName(config.NameId) ?? $"BNpc {config.BNpcBaseId:X}";
        slots[slot] = new FakeCharacter(CreatedEntityIdBase + (uint)slot, name)
        {
            Position = placement.Position,
            Rotation = placement.Rotation,
            // The native radius comes from the model (CalculateUnscaledRadius); without it only an
            // explicit config value is known.
            HitboxRadius = config.HitboxRadius,
            MaxHealth = EnemyMaxHealth,
            Health = EnemyMaxHealth,
        };
        return new FakeBattleChara(this, slot);
    }

    public IBattleCharaProxy? SpawnBattleNpcFromPacket(EnemySpawnConfig config, Placement placement, out uint entityId)
    {
        entityId = 0;
        if (config.NpcSpawnTemplate is null) return null;
        var slot = FindFreeSlot();
        if (slot < 0) return null;
        entityId = PacketSpawnEntityIdBase + (uint)slot;
        var name = Natives.Data.BNpcName(config.NameId) ?? $"BNpc {config.BNpcBaseId:X}";
        var actor = new FakeCharacter(entityId, name)
        {
            Position = placement.Position,
            Rotation = placement.Rotation,
            HitboxRadius = config.HitboxRadius,
            MaxHealth = EnemyMaxHealth,
            Health = EnemyMaxHealth,
        };
        reserved.Add(slot);
        packetSpawns.Add((slot, actor, PacketSpawnFrames));
        return new FakeBattleChara(this, slot);
    }

    public IBattleCharaProxy? SpawnDoppel(PartyMemberPreset preset, Placement placement)
    {
        var slot = FindFreeSlot();
        if (slot < 0) return null;
        slots[slot] = new FakeCharacter(CreatedEntityIdBase + (uint)slot, preset.Name)
        {
            ClassJob = preset.ClassJob,
            Position = placement.Position,
            Rotation = placement.Rotation,
            HitboxRadius = DoppelHitboxRadius,
            MaxHealth = DoppelMaxHealth,
            Health = DoppelMaxHealth,
            TargetableStatus = IsTargetable,
        };
        return new FakeBattleChara(this, slot);
    }

    public bool IsInCharacterManager(uint entityId)
        => Player.EntityId == entityId || slots.Any(a => a?.EntityId == entityId);

    public void ReleaseSlot(int slot) => reserved.Remove(slot);

    public void NoteOrphan(int slot, uint entityId) => orphans.Add((slot, entityId, 0));

    public void SweepOrphans()
    {
        for (var i = orphans.Count - 1; i >= 0; i--)
        {
            var (slot, entityId, frames) = orphans[i];
            if (slots[slot]?.EntityId == entityId)
            {
                slots[slot] = null;
                reserved.Remove(slot);
                orphans.RemoveAt(i);
            }
            else if (frames >= OrphanForgetFrames)
            {
                reserved.Remove(slot);
                orphans.RemoveAt(i);
            }
            else
            {
                orphans[i] = (slot, entityId, frames + 1);
            }
        }
    }

    internal void Tick(float deltaSeconds)
    {
        for (var i = packetSpawns.Count - 1; i >= 0; i--)
        {
            var (slot, actor, framesLeft) = packetSpawns[i];
            if (framesLeft > 1)
            {
                packetSpawns[i] = (slot, actor, framesLeft - 1);
                continue;
            }
            slots[slot] = actor;
            packetSpawns.RemoveAt(i);
        }

        Player.Tick(deltaSeconds);
        foreach (var actor in slots) actor?.Tick(deltaSeconds);
    }

    private int FindFreeSlot()
    {
        for (var i = 0; i < slots.Length; i++)
            if (slots[i] == null && !reserved.Contains(i)) return i;
        return -1;
    }
}
