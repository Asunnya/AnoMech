using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Native.Interfaces;

// Creates and finds BattleCharas. Placements are world space.
public interface IBattleCharas
{
    IBattleCharaProxy LocalPlayer { get; }

    // Null when there is no local player, a sheet row is missing, or no slot is free.
    IBattleCharaProxy? SpawnBattleNpc(EnemySpawnConfig config, Placement placement);

    // The engine's own NpcSpawn handler builds the actor from config.NpcSpawnTemplate a few
    // frames later; until then the proxy doesn't exist. Its slot stays reserved until ReleaseSlot.
    IBattleCharaProxy? SpawnBattleNpcFromPacket(EnemySpawnConfig config, Placement placement, out uint entityId);

    // A Lalafell party doppel wearing the preset's gear, registered in CharacterManager.
    IBattleCharaProxy? SpawnDoppel(PartyMemberPreset preset, Placement placement);

    bool IsInCharacterManager(uint entityId);

    void ReleaseSlot(int slot);

    // A packet spawn abandoned before its actor arrived: SweepOrphans despawns it once it does.
    void NoteOrphan(int slot, uint entityId);
    void SweepOrphans();
}
