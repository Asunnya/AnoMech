using System.Collections.Generic;
using AnoMech.Core;
using AnoMech.Pointers;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace AnoMech.Helpers;

public static unsafe class CharacterManagerHelper
{
    // Slots a packet spawn is in flight for (SimEnemy.SpawnFromPacket): the engine fills the
    // packet's SpawnIndex a few frames after HandleSpawnNpcPacket returns and replaces whatever
    // sits there by then (build 29497D: a fallback actor created in the slot meanwhile was gone
    // by +5 frames), so nothing else may be created in such a slot until it resolves.
    private static readonly HashSet<int> reserved = new();
    public static void Reserve(int index) => reserved.Add(index);
    public static void Release(int index) => reserved.Remove(index);

    // First free, unreserved BattleChara slot, or -1.
    public static int FindFreeIndex()
    {
        var characterManager = CharacterManager.Instance();
        if (characterManager == null) return -1;
        for (int i = 0; i < characterManager->BattleCharas.Length; i++)
            if (characterManager->BattleCharas[i] == null && !reserved.Contains(i)) return i;
        return -1;
    }

    public static bool CreateCharacter(out int characterIndex, out Character* characterPtr, int preferredIndex = -1, uint? entityId = null, uint? layoutId = null)
    {
        var characterManager = CharacterManager.Instance();
        if (characterManager == null)
        {
            Plugin.Log.Warning("[BattleCharaSpawn.CreateCharacter] CharacterManager.Instance() was null.");
            characterPtr = null;
            characterIndex = -1;
            return false;
        }

        if (preferredIndex == -1)
        {
            preferredIndex = FindFreeIndex();
            if (preferredIndex == -1)
            {
                Plugin.Log.Warning("[BattleCharaSpawn.CreateCharacter] Unable to find a free index.");
                characterPtr = null;
                characterIndex = -1;
                return false;
            }
        }

        entityId ??= 0xE0000000 + (uint)preferredIndex;
        layoutId ??= 0;

        characterPtr = CharacterManagerPointers.CreateCharacterAtIndex(characterManager, entityId.Value, preferredIndex, layoutId.Value);
        characterIndex = preferredIndex;
        return characterPtr != null;
    }

    // A packet-spawned wrapper despawned while its actor was still in flight: the actor arrives
    // anyway, so it is despawned here the moment it shows up (or forgotten after a while if it
    // never does). Polled from SimWorld.Tick.
    private static readonly List<(int Index, uint EntityId, int Frames)> orphans = new();

    public static void NoteOrphan(int index, uint entityId)
    {
        orphans.Add((index, entityId, 0));
        DiagnosticLog.Info($"[CharacterManagerHelper] slot {index} (entity 0x{entityId:X}) despawned while its packet spawn was in flight -- the actor is despawned when it arrives.");
    }

    public static void SweepOrphans()
    {
        if (orphans.Count == 0) return;
        var characterManager = CharacterManager.Instance();
        if (characterManager == null) return;
        for (var i = orphans.Count - 1; i >= 0; i--)
        {
            var (index, entityId, frames) = orphans[i];
            var obj = (BattleChara*)characterManager->BattleCharas[index];
            if (obj != null && obj->EntityId == entityId)
            {
                obj->DisableDraw();
                var packet = new DespawnCharacterPacket { Index = (byte)index };
                PacketDispatcherPointers.HandleDespawnCharacterPacket(0, &packet);
                DiagnosticLog.Info($"[CharacterManagerHelper] orphaned packet actor 0x{entityId:X} at slot {index} despawned after {frames} frames.");
                orphans.RemoveAt(i);
                Release(index);
            }
            else if (frames >= 300)
            {
                DiagnosticLog.Info($"[CharacterManagerHelper] orphaned packet actor 0x{entityId:X} never arrived at slot {index} -- forgotten.");
                orphans.RemoveAt(i);
                Release(index);
            }
            else
            {
                orphans[i] = (index, entityId, frames + 1);
            }
        }
    }
}
