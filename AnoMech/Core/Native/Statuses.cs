using System;
using System.Runtime.InteropServices;
using AnoMech.Pointers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace AnoMech.Core.Native;

// Status helpers for sim-only buffs. Apply writes directly into the
// StatusManager.Status array — bypasses bc->StatusManager.AddStatus, which
// drives _flags / ExtraFlags from the Status sheet AND auto-prunes any status
// whose sheet MaxDuration is 0 (most sim-only ids). Direct-slot insertion lets
// any statusId stick at the duration we ask for, with no sheet dependency.
//
// Trade-off: _flags / ExtraFlags don't get the sheet-driven bit set. That bit
// vector controls "is this entity stunned/silenced/etc" gameplay flags; for
// purely visual debuffs (tether markers, raid buffs we just want shown) this
// doesn't matter. If a future caller needs gameplay-effective flags, route
// that one through bc->StatusManager.AddStatus instead.
internal static unsafe class Statuses
{
    private static readonly ExcelSheet<LuminaStatus> Sheet = Plugin.DataManager.GetExcelSheet<LuminaStatus>();

    // Ids without a sheet row are never written (the party list and nameplates resolve the
    // row by id); ids reach this from the network.
    public static bool Exists(ushort statusId) => statusId != 0 && Sheet.HasRow(statusId);

    // Control-taking rows are never fed through the engine's gain path on the local player;
    // any control loss the sim wants is SimPlayer.SyncInputLock's, which every Despawn clears.
    public static bool LocksControl(ushort statusId) =>
        Sheet.TryGetRow(statusId, out var row) && (row.LockMovement || row.LockActions || row.LockControl || row.Transfiguration);

    private static bool IsLocalPlayer(Character* chara) =>
        Plugin.ObjectTable.LocalPlayer is { Address: var address } && address == (nint)chara;

    public static void Apply(Character* chara, ushort statusId, float duration, ushort param = 0, GameObjectId sourceObject = default)
    {
        if (chara == null || !Exists(statusId)) return;
        var bc = (BattleChara*)chara;
        var slots = bc->StatusManager.Status;

        // Refresh in place when the same (statusId, sourceObject) pair exists, so two independent
        // SimStatus instances can share an id. A default sourceObject matches by id alone: the
        // native AddStatus path writes whatever source the engine chooses, so requiring zero
        // missed engine-written slots.
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].StatusId != statusId) continue;
            if (sourceObject != default && slots[i].SourceObject != sourceObject) continue;
            slots[i].Param = param;
            slots[i].RemainingTime = duration == 0 ? 20: duration;
            if (sourceObject != default) slots[i].SourceObject = sourceObject;
            return;
        }

        // Otherwise drop into the first empty slot. Keep the array packed at
        // low indices — see Remove's comment for why that matters to PartyHud.
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].StatusId != 0) continue;
            slots[i].StatusId = statusId;
            slots[i].Param = param;
            slots[i].RemainingTime = duration == 0 ? 20: duration;
            slots[i].SourceObject = sourceObject;
            if (bc->StatusManager.NumValidStatuses <= i)
                bc->StatusManager.NumValidStatuses = (byte)(i + 1);
            return;
        }
    }

    // Applies a status through the engine's native StatusManager::AddStatus.
    // Triggers the sheet-driven side effects our direct-slot Apply skips:
    // _flags / ExtraFlags bits, StatusLoopVFX, and (most importantly for our
    // boss form swaps) the Param → CharacterData.TransformationId derivation
    // that runs inline inside the engine's apply path. Use for real game
    // statuses like Superfluid / OmegaM / OmegaF where that visual is wanted.
    //
    // Caveats:
    // - Duration comes from the sheet's MaxDuration. Sim-only ids with
    //   MaxDuration=0 will be auto-pruned by the engine immediately, so this
    //   helper is only safe for real game statuses today.
    // - All sheet-driven side effects fire (StatusLoopVFX, Flags etc.). For
    //   the transformation buffs that's exactly the canonical visual; for
    //   purely-visual sim debuffs you still want plain Apply.
    public static void AddStatusInit(Character* chara, ushort statusId, ushort param, GameObjectId sourceObject = default)
    {
        if (chara == null || !Exists(statusId)) return;
        var bc = (BattleChara*)chara;
        bc->StatusManager.AddStatus(statusId, param);

        // AddStatus no-ops on the local player (its IsValidClientObject guard rejects it), so
        // replicate its body there: write the slot, then OnGainStatus for the loop VFX. A default
        // source matches by id alone (see Apply).
        var slots = bc->StatusManager.Status;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].StatusId != statusId) continue;
            if (sourceObject != default && slots[i].SourceObject != sourceObject) continue;
            return; // AddStatus took (doppel) — leave it alone
        }

        Apply(chara, statusId, 0f, param, sourceObject);
        if (IsLocalPlayer(chara) && LocksControl(statusId)) return;
        StatusManagerPointers.OnGainStatus(&bc->StatusManager, statusId, 0f, param, 0, 0);
    }

    // Source-aware so a status shared by two SimStatus instances can be removed without
    // clearing its twin's slot.
    public static void Remove(Character* chara, ushort statusId, GameObjectId sourceObject = default)
    {
        if (chara == null || statusId == 0) return;
        var bc = (BattleChara*)chara;
        var slots = bc->StatusManager.Status;
        for (int i = 0; i <= bc->StatusManager.NumValidStatuses && i < slots.Length; i++)
        {
            if (slots[i].StatusId != statusId) continue;
            if (sourceObject != default && slots[i].SourceObject != sourceObject) continue;
            bc->StatusManager.RemoveStatus(i);
            return;
        }
    }
}
