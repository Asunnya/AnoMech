using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace AnoMech.Core.Native;

// Hooks the native action and movement input paths so a mechanic can stun the local player.
// Status-row writes don't enforce anything (the server overwrites them every packet); the real
// lockout is the flags below, which SimPlayer reconciles each tick.
//
// Signatures and detour shapes lifted from FFXIV-RaidsRewritten's PlayerMovementOverride.cs /
// ActionManagerEx.cs (which credit awgil's vnavmesh + bossmod).
public sealed unsafe class LocalPlayerInputHooks : IDisposable
{
    public bool DisableAllActions { get; set; }
    public bool ZeroMovement { get; set; }
    // A knockback slide freezes translation but not turning; Sleep, Confuse and KO freeze
    // rotation too. LockedRotation is re-stamped every frame from the ActionManager::Update
    // hook, after camera-follow rotation.
    public bool ZeroRotation { get; set; }
    public float? LockedRotation { get; set; }

    // Raised after the local player successfully executes a real action (the
    // auto-attack-cancel general action is filtered out). The UserActions module
    // subscribes to resolve effects the sim firewall blocks; nothing here depends
    // on a subscriber.
    public event Action<ActionType, uint>? ActionExecuted;

    // --- Player activity signals (read by SimPlayer to drive Party.Player.IsMoving/IsActing) ---
    // The engine's own per-frame movement sample (the signal bossmod reads), captured before
    // stun-zeroing: input intent, not a position delta.
    public bool MovementInputActive { get; private set; }

    public bool IsAutoAttacking => UIState.Instance()->WeaponState.AutoAttackState.IsAutoAttacking;

    // State poll: a jump is always self-initiated.
    public bool IsJumping => Plugin.Condition[ConditionFlag.Jumping];

    // Latched on a real action press; drained once per frame by SimPlayer.
    private bool actionUsedSincePoll;
    public bool PollActionUsed()
    {
        var used = actionUsedSincePoll;
        actionUsedSincePoll = false;
        return used;
    }

    // Debug aid for pinning down a real action id; DebugMenu shows these.
    private const int RecentActionsCapacity = 50;
    private readonly Queue<(uint ActionId, ActionType Type)> recentActions = new();
    public IReadOnlyCollection<(uint ActionId, ActionType Type)> RecentActions => recentActions;

    private void RecordRecentAction(uint actionId, ActionType type)
    {
        // GeneralAction 1 is the auto-attack engage/re-engage action -- fires constantly, pure noise here.
        if (type == ActionType.GeneralAction && actionId == 1) return;
        Core.Map.ZoneSession.NoteActionPressed(type, actionId);
        recentActions.Enqueue((actionId, type));
        while (recentActions.Count > RecentActionsCapacity) recentActions.Dequeue();
        var jobId = Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId;
        Core.DiagnosticLog.Info($"[LocalPlayerInputHooks] Action pressed: {actionId} ({type}) -- {Core.ActionLookup.Name(actionId)} (job={jobId}).");
    }

    // Edge-triggered gain/loss logging; reads the local player directly so it works with no
    // scenario running.
    // Counted, not just present: the same id can be held twice.
    private readonly Dictionary<ushort, int> lastLoggedStatusCounts = new();

    private void ScanAndLogActiveStatuses()
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null) return;
        var bc = (BattleChara*)localPlayer.Address;
        if (bc == null) return;
        var jobId = localPlayer.ClassJob.RowId;
        var current = new Dictionary<ushort, (int Count, float Remaining)>();
        foreach (var status in bc->StatusManager.Status)
        {
            if (status.StatusId == 0) continue;
            var seen = current.GetValueOrDefault(status.StatusId);
            current[status.StatusId] = (seen.Count + 1, status.RemainingTime);
        }
        foreach (var (gained, (count, remaining)) in current)
            if (count > lastLoggedStatusCounts.GetValueOrDefault(gained))
                Core.DiagnosticLog.Info($"[LocalPlayerInputHooks] Status gained: {gained} -- {Core.StatusLookup.Name(gained)} (job={jobId}, duration={remaining:F1}, x{count}).");
        foreach (var lost in lastLoggedStatusCounts.Keys.Except(current.Keys))
            Core.DiagnosticLog.Info($"[LocalPlayerInputHooks] Status lost: {lost} -- {Core.StatusLookup.Name(lost)} (job={jobId}).");
        lastLoggedStatusCounts.Clear();
        foreach (var (id, (count, _)) in current) lastLoggedStatusCounts[id] = count;
    }

    // Latched whenever the game calls Hotbar.CancelCast — i.e. the player requested a
    // cast cancel (ESC / the cancel-cast keybind, however it's bound; also cancels
    // auto-attack). This is the outgoing intent the server would act on; the sim
    // firewall eats that round-trip, so UserActions drains this each frame and
    // synthesizes the interrupt itself.
    private bool cancelCastRequested;
    public bool PollCancelCast()
    {
        var requested = cancelCastRequested;
        cancelCastRequested = false;
        return requested;
    }

    private delegate void RMIWalkDelegate(void* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk);
    [Signature("E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D")]
    private Hook<RMIWalkDelegate> rmiWalkHook = null!;

    private enum KeybindType
    {
        StrafeLeft = 325,
        StrafeRight = 326,
    }

    [return: MarshalAs(UnmanagedType.U1)]
    private delegate bool CheckStrafeKeybindDelegate(IntPtr ptr, KeybindType keybind);
    [Signature("E8 ?? ?? ?? ?? 84 C0 74 04 41 C6 06 01 BA 44 01 00 00")]
    private Hook<CheckStrafeKeybindDelegate> checkStrafeKeybindHook = null!;

    private readonly Hook<InputData.Delegates.IsInputIdPressed> isInputIdPressedHook;
    private readonly Hook<ActionManager.Delegates.Update> updateHook;
    private readonly Hook<ActionManager.Delegates.UseAction> useActionHook;
    private readonly Hook<ActionManager.Delegates.UseActionLocation> useActionLocationHook;
    private readonly Hook<Hotbar.Delegates.CancelCast> cancelCastHook;

    public LocalPlayerInputHooks(IGameInteropProvider hook)
    {
        hook.InitializeFromAttributes(this);

        isInputIdPressedHook = hook.HookFromAddress<InputData.Delegates.IsInputIdPressed>(
            InputData.Addresses.IsInputIdPressed.Value, IsInputIdPressedDetour);
        updateHook = hook.HookFromAddress<ActionManager.Delegates.Update>(
            ActionManager.Addresses.Update.Value, UpdateDetour);
        useActionHook = hook.HookFromAddress<ActionManager.Delegates.UseAction>(
            ActionManager.Addresses.UseAction.Value, UseActionDetour);
        useActionLocationHook = hook.HookFromAddress<ActionManager.Delegates.UseActionLocation>(
            ActionManager.Addresses.UseActionLocation.Value, UseActionLocationDetour);
        cancelCastHook = hook.HookFromAddress<Hotbar.Delegates.CancelCast>(
            Hotbar.Addresses.CancelCast.Value, CancelCastDetour);

        rmiWalkHook.Enable();
        checkStrafeKeybindHook.Enable();
        isInputIdPressedHook.Enable();
        updateHook.Enable();
        useActionHook.Enable();
        useActionLocationHook.Enable();
        cancelCastHook.Enable();
    }

    public void Dispose()
    {
        // Game.Dispose doesn't route through ResetInternal.
        RestoreGaugeIllusion();
        if (Plugin.GameInstance is { } game) TankShieldTracker.ClearAllVisuals(game.World.Party);
        rmiWalkHook?.Dispose();
        checkStrafeKeybindHook?.Dispose();
        isInputIdPressedHook?.Dispose();
        updateHook?.Dispose();
        useActionHook?.Dispose();
        useActionLocationHook?.Dispose();
        cancelCastHook?.Dispose();
    }

    // The player asked to cancel their cast; latch it and let the original run (its
    // outgoing packet is firewalled in the sim, so it has no visible effect here).
    private void CancelCastDetour(Hotbar* thisPtr)
    {
        cancelCastRequested = true;
        cancelCastHook.Original(thisPtr);
    }

    private void RMIWalkDetour(void* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk)
    {
        rmiWalkHook.Original(self, sumLeft, sumForward, sumTurnLeft, haveBackwardOrStrafe, a6, bAdditiveUnk);
        // self is a MoveControllerSubMemberForMine*; the sums are its move vector.
        MovementInputActive = *sumLeft != 0 || *sumForward != 0;
        if (ZeroRotation) *sumTurnLeft = 0;
        if (!ZeroMovement) return;
        *sumLeft = 0;
        *sumForward = 0;
        *haveBackwardOrStrafe = 0;
    }

    private bool CheckStrafeKeybindDetour(IntPtr ptr, KeybindType keybind)
    {
        if (ZeroMovement && (keybind == KeybindType.StrafeLeft || keybind == KeybindType.StrafeRight))
            return false;
        return checkStrafeKeybindHook.Original(ptr, keybind);
    }

    private bool IsInputIdPressedDetour(InputData* inputData, InputId inputId)
    {
        if (ZeroMovement && (inputId == InputId.JUMP || inputId == InputId.PAD_JUMPANDCANCELCAST))
            return false;
        return isInputIdPressedHook.Original(inputData, inputId);
    }

    // Drains queued auto-attacks while DisableAllActions is set so the player
    // doesn't keep swinging mid-stun; mirrors raid-rewritten's UpdateDetour.
    private void UpdateDetour(ActionManager* self)
    {
        updateHook.Original(self);
        ScanAndLogActiveStatuses();
        UpdateGaugeIllusion();
        RefreshShieldVisuals();
        // A missed clear must not pin rotation outside an instance.
        if (LockedRotation is { } lockedRot)
        {
            if (Plugin.GameInstance is { } g && g.World.Map.IsInInstance && Plugin.ObjectTable.LocalPlayer is { } lp)
                ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)lp.Address)->SetRotation(lockedRot);
            else
                LockedRotation = null;
        }
        if (!DisableAllActions) return;
        var autosOn = UIState.Instance()->WeaponState.AutoAttackState.IsAutoAttacking;
        if (autosOn) self->UseAction(ActionType.GeneralAction, 1);
    }

    // Every frame: TankShieldTracker never pushes updates. Also clears every visual once
    // IsInInstance goes false.
    private static void RefreshShieldVisuals()
    {
        if (Plugin.GameInstance is not { } game) return;
        var party = game.World.Party;
        if (!game.World.Map.IsInInstance)
        {
            TankShieldTracker.ClearAllVisuals(party);
            return;
        }
        foreach (var role in Enum.GetValues<PartyRole>())
            if (party.Get(role) is { } member)
                TankShieldTracker.RefreshVisual(member, role);
    }

    // ---- Gauge illusion (client-side only) -----------------------------------
    // The hotbar icon reads the real job gauge to render as available, so a gauge-gated
    // mitigation (Holy Sheltron needs 50 Oath) would look disabled. Topped up for display only;
    // the real value is saved once and restored when the sim ends.
    private byte? savedOathGauge;
    private const byte PaladinClassJobId = 19;

    private void UpdateGaugeIllusion()
    {
        if (Plugin.GameInstance is not { } game || !game.World.Map.IsInInstance)
        {
            RestoreGaugeIllusion();
            return;
        }
        UpdateLimitBreakIllusion();
        // Only Holy Sheltron (Paladin/Oath) needs this today.
        if (Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId != PaladinClassJobId) return;
        var gauge = (PaladinGauge*)Plugin.JobGauges.Address;
        if (gauge == null) return;
        savedOathGauge ??= gauge->OathGauge;
        gauge->OathGauge = 100;
    }

    // Same for the limit break gauge: a scenario that expects a tank LB3 needs the button
    // pressable, and solo in the inn the real gauge is empty. Client-side only; a press is
    // intercepted or swallowed, so no LB packet ever leaves.
    private (byte BarCount, ushort CurrentUnits, ushort BarUnits)? savedLimitBreak;
    private const ushort LimitBreakUnitsPerBar = 10000;
    private const byte LimitBreakBars = 3;
    // An intercepted tank LB3 empties the faked gauge for the rest of the run, as a real one would.
    private bool limitBreakConsumed;
    private static readonly HashSet<uint> TankLimitBreakActionIds = [199, 4240, 4241, 17105];

    private void UpdateLimitBreakIllusion()
    {
        var lb = LimitBreakController.Instance();
        if (lb == null) return;
        savedLimitBreak ??= (lb->BarCount, lb->CurrentUnits, lb->BarUnits);
        lb->BarCount = LimitBreakBars;
        lb->BarUnits = LimitBreakUnitsPerBar;
        lb->CurrentUnits = limitBreakConsumed ? (ushort)0 : (ushort)(LimitBreakUnitsPerBar * LimitBreakBars);
    }

    // Also called from Game.ResetInternal so the restore is immediate on Reset/Leave.
    public void RestoreGaugeIllusion()
    {
        if (savedLimitBreak is { } lbSaved)
        {
            var lb = LimitBreakController.Instance();
            if (lb != null)
            {
                lb->BarCount = lbSaved.BarCount;
                lb->CurrentUnits = lbSaved.CurrentUnits;
                lb->BarUnits = lbSaved.BarUnits;
            }
            savedLimitBreak = null;
        }
        limitBreakConsumed = false;
        if (savedOathGauge is not { } saved) return;
        if (Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId == PaladinClassJobId)
        {
            var gauge = (PaladinGauge*)Plugin.JobGauges.Address;
            if (gauge != null) gauge->OathGauge = saved;
        }
        savedOathGauge = null;
    }

    // Null outside a scenario too: there the gauge is the player's own and none of this applies.
    private int? LimitBreakLevel(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action) return null;
        if (Plugin.GameInstance is not { } game || !game.World.Map.IsInInstance) return null;
        if (Plugin.ObjectTable.LocalPlayer is not { } local) return null;
        var lb = LimitBreakController.Instance();
        if (lb == null) return null;
        var character = (Character*)local.Address;
        for (byte i = 0; i < 3; i++)
            if (lb->GetActionId(character, i) == actionId) return i;
        return null;
    }

    // Only LB3, and only once per run. Everything else is the client's own: with the gauge faked
    // it runs the real UseAction, and the request packet that goes with it is eaten by the firewall.
    private bool RefuseLimitBreak(int level, uint actionId)
    {
        var name = Core.ActionLookup.Name(actionId);
        var job = Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId;
        if (level < 2)
        {
            Core.DiagnosticLog.Info($"[LimitBreak] {name} ({actionId}, LB{level + 1}, job={job}) pressed -- only LB3 is simulated, press dropped.");
            return true;
        }
        if (limitBreakConsumed)
        {
            Core.DiagnosticLog.Info($"[LimitBreak] {name} ({actionId}, job={job}) pressed but this run's gauge is already spent -- press dropped.");
            return true;
        }
        return false;
    }

    // The client's own refusal reason is the only way to see why a press did nothing.
    private void NoteLimitBreakPress(uint actionId, ulong targetId, bool accepted)
    {
        var name = Core.ActionLookup.Name(actionId);
        var job = Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId;
        if (!accepted)
        {
            var am = ActionManager.Instance();
            var status = am == null ? 0u : am->GetActionStatus(ActionType.Action, actionId, targetId);
            var reason = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.LogMessage>()
                .GetRowOrDefault(status)?.Text.ExtractText() ?? "";
            Core.DiagnosticLog.Info($"[LimitBreak] {name} ({actionId}, job={job}) refused by the client -- status {status} \"{reason}\".");
            return;
        }
        limitBreakConsumed = true;
        var castSeconds = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().TryGetRow(actionId, out var row)
            ? row.Cast100ms / 10f
            : 0f;
        Core.DiagnosticLog.Info($"[LimitBreak] {name} ({actionId}, LB3, job={job}) accepted by the client, UseAction target 0x{targetId:X}.");
        Plugin.GameInstance?.World.Party.Player?.WatchLimitBreak(actionId, castSeconds);
    }

    // A cancelled cast costs nothing in retail, so the faked gauge refills for another try.
    public void RefundLimitBreak() => limitBreakConsumed = false;

    private bool UseActionDetour(ActionManager* self, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted)
    {
        RecordRecentAction(actionId, actionType);
        if (DisableAllActions && !IsStopAutosAction(actionType, actionId)) return false;
        if (actionType == ActionType.Action && TryInterceptTankMitigation(actionId, targetId))
        {
            actionUsedSincePoll = true;
            return true;
        }
        var limitBreakLevel = LimitBreakLevel(actionType, actionId);
        if (limitBreakLevel is { } level && RefuseLimitBreak(level, actionId)) return false;
        var result = useActionHook.Original(self, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
        if (!result && actionType == ActionType.Action)
            Plugin.Log.Debug($"[PlayerInput] client refused action {actionId}: ActionStatus {self->GetActionStatus(actionType, actionId)}");
        if (limitBreakLevel == 2) NoteLimitBreakPress(actionId, targetId, result);
        // Ignore the auto-attack-cancel general action that UpdateDetour issues while stunned.
        if (result && !IsStopAutosAction(actionType, actionId))
        {
            actionUsedSincePoll = true;
            ActionExecuted?.Invoke(actionType, actionId);
        }
        return result;
    }

    // Blocks a tracked mitigation's real UseAction while a sim runs, so the real ability and
    // its recast group are never touched; applies the synthetic status, fakes the hotbar sweep
    // and records the sim-only cooldown instead. False for anything untracked or outside an
    // instance; a press still on the sim cooldown is swallowed silently.
    private bool TryInterceptTankMitigation(uint actionId, ulong targetId)
    {
        if (Plugin.GameInstance is not { } game || !game.World.Map.IsInInstance) return false;
        if (!TankMitigation.ByActionId.TryGetValue(actionId, out var ability)) return false;
        // A spent gauge means the gate below refuses the press, so the mitigation must not apply.
        if (limitBreakConsumed && TankLimitBreakActionIds.Contains(actionId)) return false;
        var party = game.World.Party;
        var role = party.PlayerRole;
        if (party.Player is not { } player) return false;

        if (!TankMitigationTracker.IsAvailable(role, ability.StatusId, ability.Charges))
            return true; // still on the sim's own cooldown -- swallow the press, nothing happens

        var appliedRoles = new List<PartyRole>();
        if (ability.SourceSide)
        {
            var affected = ApplySourceSideMitigation(game.World, player, ability);
            // A peer's enemy doppel is cosmetic; the host's copy needs the debuff too.
            Plugin.MultiplayerInstance?.ReportAppliedEnemyStatus(affected, ability.StatusId, ability.Duration ?? 0f);
        }
        else if (ability.Scope == MitigationScope.Party)
        {
            foreach (var r in Enum.GetValues<PartyRole>())
            {
                if (party.Get(r) is not { } member) continue;
                member.AddStatus(ability.StatusId, ability.Duration ?? 0f);
                appliedRoles.Add(r);
            }
        }
        else if (ability.Scope == MitigationScope.Ally)
        {
            var targetRole = ResolveTargetRole(party, targetId);
            if (targetRole is { } r && party.Get(r) is { } member)
            {
                member.AddStatus(ability.StatusId, ability.Duration ?? 0f);
                appliedRoles.Add(r);
            }
            else
            {
                Core.DiagnosticLog.Warn($"[LocalPlayerInputHooks] {ability.Name} pressed with no resolvable party-member target -- swallowed, nothing applied.");
            }
        }
        else
        {
            // Self scope is reported by SendSelfMitigationIfChanged.
            player.AddStatus(ability.StatusId, ability.Duration ?? 0f);
            appliedRoles.Add(role);
        }
        // Banks + visualizes any shield component this ability carries; no-op if it has none.
        var shieldFraction = GrantShield(party, player, ability, appliedRoles);

        // Party/Ally scope touches roles whose puppets are cosmetic on a peer.
        if (ability.Scope is MitigationScope.Party or MitigationScope.Ally)
            Plugin.MultiplayerInstance?.ReportAppliedRoleStatus(appliedRoles, ability.StatusId, ability.Duration ?? 0f, shieldFraction);

        TankMitigationTracker.RecordUse(role, ability.StatusId, ability.Cooldown ?? 0f);
        var jobId = Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId;
        // A tank LB3 spends the gauge, not a recast group, so the press falls through to the
        // real UseAction for the client to play.
        if (TankLimitBreakActionIds.Contains(actionId))
        {
            Core.DiagnosticLog.Info($"[LocalPlayerInputHooks] Applied {ability.Name} (actionId={actionId}) for {role} (job={jobId}) -- status {ability.StatusId} on [{string.Join(",", appliedRoles)}]; the press goes on to the client for its animation.");
            return false;
        }
        ForceRecastSweep(actionId, ability.Cooldown ?? 0f);
        Core.DiagnosticLog.Info($"[LocalPlayerInputHooks] Intercepted {ability.Name} (actionId={actionId}) for {role} (job={jobId}), scope={ability.Scope} -- applied synthetic status {ability.StatusId} to [{string.Join(",", appliedRoles)}], real ability never touched.");
        return true;
    }

    // Reprisal-style: debuffs every active enemy within the ability's radius of the caster.
    private static List<SimEnemy> ApplySourceSideMitigation(SimWorld world, SimCharacter caster, TankMitigationAbility ability)
    {
        var affected = new List<SimEnemy>();
        var radius = ability.Radius ?? 0f;
        if (radius <= 0f) return affected;
        var radiusSq = radius * radius;
        foreach (var enemy in world.Children.OfType<SimEnemy>())
        {
            if (!enemy.IsActive) continue;
            if (Vector3.DistanceSquared(caster.Position, enemy.Position) > radiusSq) continue;
            enemy.AddStatus(ability.StatusId, ability.Duration ?? 0f);
            affected.Add(enemy);
        }
        return affected;
    }

    // Shield % is of the caster's max HP: converted to HP once, then re-expressed against each
    // recipient's own max HP. Returns the granted fraction (0f if none).
    private static float GrantShield(SimParty party, SimCharacter caster, TankMitigationAbility ability, IReadOnlyList<PartyRole> appliedRoles)
    {
        var casterPercent = ability.ShieldPercentOfMaxHp
            ?? (ability.ShieldPotency is { } potency ? TankShieldEstimate.PercentOfCasterMaxHp(potency) : (float?)null);
        if (casterPercent is not { } percent) return 0f; // this ability carries no shield component at all
        var casterBc = caster.BattleCharaPtr;
        if (casterBc == null) return 0f;
        var casterShieldHp = casterBc->MaxHealth * percent;

        var lastGrantedFraction = 0f;
        foreach (var role in appliedRoles)
        {
            if (party.Get(role) is not { } member) continue;
            var recipientBc = member.BattleCharaPtr;
            if (recipientBc == null || recipientBc->MaxHealth == 0) continue;
            var fraction = casterShieldHp / recipientBc->MaxHealth;
            if (fraction <= 0f) continue;
            TankShieldTracker.Grant(role, fraction, ability.Duration ?? 0f);
            TankShieldTracker.RefreshVisual(member, role);
            lastGrantedFraction = fraction;
        }
        return lastGrantedFraction;
    }

    // Ally-scope mitigations (Oblation, Intervention, ...) are cast on a specific member.
    private static PartyRole? ResolveTargetRole(SimParty party, ulong targetId)
    {
        if (targetId == 0) return null;
        foreach (var role in Enum.GetValues<PartyRole>())
            if (party.Get(role) is { } member && (ulong)member.GameObjectId == targetId)
                return role;
        return null;
    }

    // Total must be set explicitly: a never-started group can have a zero Total, and
    // IsActive alone renders no sweep.
    private static void ForceRecastSweep(uint actionId, float cooldownSeconds)
    {
        var am = ActionManager.Instance();
        if (am == null) return;
        var group = am->GetRecastGroup((int)ActionType.Action, actionId);
        if (group < 0) return;
        var detail = am->GetRecastGroupDetail(group);
        if (detail == null) return;
        detail->IsActive = true;
        detail->Elapsed = 0f;
        detail->Total = cooldownSeconds;
    }

    private bool UseActionLocationDetour(ActionManager* self, ActionType actionType, uint actionId, ulong targetId, Vector3* location, uint extraParam, byte a7)
    {
        if (DisableAllActions && !IsStopAutosAction(actionType, actionId)) return false;
        var result = useActionLocationHook.Original(self, actionType, actionId, targetId, location, extraParam, a7);
        if (result)
        {
            actionUsedSincePoll = true;
            ActionExecuted?.Invoke(actionType, actionId);
        }
        return result;
    }

    // Lets the auto-cancel UseAction from UpdateDetour through; everything else
    // bounces while autos are still firing.
    private static bool IsStopAutosAction(ActionType actionType, uint actionId)
    {
        if (!UIState.Instance()->WeaponState.AutoAttackState.IsAutoAttacking) return false;
        return actionType == ActionType.GeneralAction && actionId == 1;
    }
}
