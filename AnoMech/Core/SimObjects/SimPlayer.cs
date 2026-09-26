using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using CastBarNumberArray = FFXIVClientStructs.FFXIV.Client.UI.Arrays.CastBarNumberArray;

namespace AnoMech.Core.SimObjects;

public sealed unsafe class SimPlayer(Coordinates coordinates) : SimCharacter(coordinates), ISimPartyMember
{
    private const ushort StunStatusId = 896;  // "Down for the Count" (896) — IsPermanent + LockControl variant.

    // The real HP bar is only touched on a scenario KO (a 1-HP sliver), restored in RestoreHpBar.
    public void DropHpBar()
    {
        var bc = BattleCharaPtr;
        if (bc != null) bc->Health = 1;
    }

    public void RestoreHpBar()
    {
        var bc = BattleCharaPtr;
        if (bc != null && bc->Health < bc->MaxHealth) bc->Health = bc->MaxHealth;
    }

    // Real native MaxHealth before it was overridden; null if inactive.
    private uint? realMaxHealth;

    // So TankMitigation's fixed-HP tankbuster numbers land against the same pool bot tanks use.
    public void OverrideMaxHealthForTankRole(uint tankMaxHealth)
    {
        var bc = BattleCharaPtr;
        if (bc == null || realMaxHealth != null) return;
        realMaxHealth = bc->MaxHealth;
        bc->MaxHealth = tankMaxHealth;
        bc->Health = tankMaxHealth;
    }

    // Host-authoritative HP for a peer's own character; the real MaxHealth is captured once so
    // Despawn restores it no matter what a host sent.
    public void ApplyNetworkHp(uint currentHp, uint maxHp)
    {
        var bc = BattleCharaPtr;
        if (bc == null || maxHp == 0) return;
        realMaxHealth ??= bc->MaxHealth;
        bc->MaxHealth = maxHp;
        bc->Health = Math.Min(currentHp, maxHp);
    }

    // Must run before RestoreHpBar: restore MaxHealth first, then clamp Health down.
    public void RestoreRealMaxHealth()
    {
        var bc = BattleCharaPtr;
        if (realMaxHealth is not { } original) return;
        if (bc != null)
        {
            bc->MaxHealth = original;
            if (bc->Health > original) bc->Health = original;
        }
        realMaxHealth = null;
    }

    public PartyRole Role { get; set; }
    public bool Dead { get; private set; }

    // For stillness/movement mechanics: IsMoving = movement input, a jump, any action, or an
    // in-flight debug-bot MoveTo; IsActing also counts auto-attacks. Forced false while KO'd.
    public bool IsMoving { get; private set; }
    public bool IsActing { get; private set; }

    internal override BattleChara* BattleCharaPtr => (BattleChara*)(Plugin.ObjectTable.LocalPlayer?.Address ?? 0);

    private protected override PlayerMovement Movement => field ??= new PlayerMovement(this);

    public void Knockback(Vector3 source, float distance, float speed) => Movement.Knockback(source, distance, speed);

    public void PushInDirection(float heading, float distance, float speed) => Movement.PushInDirection(heading, distance, speed);

    public void PushInDirectionEased(float heading, float distance, float durationSeconds) => Movement.PushInDirectionEased(heading, distance, durationSeconds);

    // The input lock is re-derived every tick from Dead/Movement/statuses.
    public override void Tick(float deltaSeconds)
    {
        base.Tick(deltaSeconds);
        SampleActivity();
        TickLimitBreak(deltaSeconds);
        SyncInputLock();
    }

    // The client's own prediction runs the whole cast -- bar, animations, lock, cancel-on-move.
    // All the sim does is watch it, to refund the faked gauge when it doesn't land and to count
    // the cast as activity for stillness mechanics.
    private uint limitBreakActionId;
    private LimitBreakWatch limitBreakWatch;
    private float limitBreakGrace;
    private float limitBreakTotal;
    private float limitBreakWatched;
    private float limitBreakRemaining;
    private float limitBreakSample;

    private enum LimitBreakWatch { Idle, Starting, Casting }

    // The client needs a frame or two after UseAction before its cast bar exists.
    private const float LimitBreakStartGrace = 0.5f;
    private const float LimitBreakSampleSeconds = 0.5f;
    // How long past its own cast time the bar may linger before we stop believing it.
    private const float LimitBreakOverstaySeconds = 1.5f;

    public bool IsLimitBreaking => limitBreakWatch != LimitBreakWatch.Idle;

    // An instant limit break (every tank LB3) has no cast to lose, so there is nothing to watch.
    public void WatchLimitBreak(uint actionId, float castSeconds)
    {
        limitBreakActionId = actionId;
        limitBreakTotal = castSeconds;
        limitBreakWatched = 0f;
        limitBreakRemaining = castSeconds;
        limitBreakSample = LimitBreakSampleSeconds;
        if (castSeconds <= 0f)
        {
            limitBreakWatch = LimitBreakWatch.Idle;
            DiagnosticLog.Info($"[LimitBreak] {ActionLookup.Name(actionId)} ({actionId}) is instant -- the gauge stays spent. Timeline slots {SimEnemy.DescribeActionTimeline(BattleCharaPtr)}.");
            return;
        }
        limitBreakWatch = LimitBreakWatch.Starting;
        limitBreakGrace = LimitBreakStartGrace;
        DiagnosticLog.Info($"[LimitBreak] {ActionLookup.Name(actionId)} ({actionId}) accepted, {castSeconds:F1}s cast -- watching the client's own bar. {DescribeCast()}.");
    }

    private void TickLimitBreak(float deltaSeconds)
    {
        if (limitBreakWatch == LimitBreakWatch.Idle) return;
        var bc = BattleCharaPtr;
        if (bc == null) { DropLimitBreakWatch("there is no character to watch", refund: true); return; }

        limitBreakWatched += deltaSeconds;
        var casting = bc->CastInfo.IsCasting && bc->CastInfo.ActionId == limitBreakActionId;
        if (casting)
        {
            limitBreakWatch = LimitBreakWatch.Casting;
            limitBreakRemaining = bc->CastInfo.TotalCastTime - bc->CastInfo.CurrentCastTime;
            // An overstaying bar means the client is holding out for a reply the firewall ate;
            // pinning IsActing true for the rest of the run is worse.
            if (limitBreakWatched > limitBreakTotal + LimitBreakOverstaySeconds)
                DropLimitBreakWatch($"the client's bar never cleared ({limitBreakWatched:F1}s for a {limitBreakTotal:F1}s cast)", refund: false);
            else
                SampleLimitBreak(deltaSeconds);
            return;
        }

        if (limitBreakWatch == LimitBreakWatch.Starting)
        {
            limitBreakGrace -= deltaSeconds;
            if (limitBreakGrace <= 0f)
                DropLimitBreakWatch("the client never opened a cast bar for it", refund: true);
            return;
        }

        // Past the slidecast window the action is committed; anything earlier is an interrupt,
        // which costs nothing in retail.
        var landed = limitBreakRemaining <= Plugin.Config.CastInterruptThreshold;
        DropLimitBreakWatch(landed
            ? $"it landed (bar cleared with {limitBreakRemaining:F2}s left)"
            : $"it was interrupted with {limitBreakRemaining:F2}s left", refund: !landed);
    }

    private void DropLimitBreakWatch(string why, bool refund)
    {
        limitBreakWatch = LimitBreakWatch.Idle;
        if (refund) Plugin.PlayerInputHooks.RefundLimitBreak();
        DiagnosticLog.Info($"[LimitBreak] {ActionLookup.Name(limitBreakActionId)}: {why}{(refund ? " -- the gauge is refunded" : "")}. Timeline slots {SimEnemy.DescribeActionTimeline(BattleCharaPtr)}.");
    }

    private void SampleLimitBreak(float deltaSeconds)
    {
        limitBreakSample -= deltaSeconds;
        if (limitBreakSample > 0f) return;
        limitBreakSample = LimitBreakSampleSeconds;
        DiagnosticLog.Info($"[LimitBreak] {ActionLookup.Name(limitBreakActionId)} casting, {limitBreakRemaining:F2}s left: {DescribeCast()}; timeline {SimEnemy.DescribeActionTimeline(BattleCharaPtr)}.");
    }

    private string DescribeCast()
    {
        var bc = BattleCharaPtr;
        var hud = CastBarNumberArray.Instance();
        var info = bc == null
            ? "none"
            : $"casting={bc->CastInfo.IsCasting} action={bc->CastInfo.ActionId} {bc->CastInfo.CurrentCastTime:F2}/{bc->CastInfo.TotalCastTime:F2}";
        var bar = hud == null
            ? "none"
            : $"icon={hud->CastIconId} {hud->CompletionPercentage}% interrupted={hud->Interupted}";
        return $"CastInfo({info}) HUD({bar})";
    }

    private void SampleActivity()
    {
        var hooks = Plugin.PlayerInputHooks;
        // Drained every frame, even while dead, so a stale press can't carry over.
        var actedThisFrame = hooks.PollActionUsed();
        if (Dead)
        {
            IsMoving = false;
            IsActing = false;
            return;
        }
        IsMoving = hooks.MovementInputActive || actedThisFrame || hooks.IsJumping || Movement.IsMoving;
        // A limit break casts for seconds, and an Acceleration Bomb landing in that window has
        // caught the player acting, exactly as it would in the fight.
        IsActing = IsMoving || hooks.IsAutoAttacking || IsLimitBreaking;
    }

    private void CancelLimitBreak(string why)
    {
        if (IsLimitBreaking) DropLimitBreakWatch(why, refund: true);
    }

    public void OnKilled()
    {
        CancelLimitBreak("the player died");
        Dead = true;
        StopMoving();
        DropHpBar(); // godmode preview skips this path
        AddStatus(StunStatusId);
        this.PlayKoActionTimeline();
        SyncInputLock(); // engage the lock now, not one frame later
    }

    public override void Despawn()
    {
        CancelLimitBreak("the run ended");
        base.Despawn();
        StopMoving();
        // Order matters; see RestoreRealMaxHealth.
        RestoreRealMaxHealth();
        // Unconditional: also covers a godmode preview drop, where Dead is never set.
        RestoreHpBar();
        if (Dead)
        {
            ResetActionTimelineNative();
            PlayActionTimelineNative(77); // revive
            Dead = false;
        }
        // Nothing ticks between a reset and the next scenario, so the lock must clear here.
        SyncInputLock();
    }

    // Real FFXIV ids: Confused and Sleep take control away in retail, so the local player is
    // locked out the way a bot doppel has no input.
    private const ushort StatusIdConfused = 0x503;
    private const ushort StatusIdSleep = 0x131E;
    private const ushort StatusIdBind = 0x9D6;

    private void SyncInputLock()
    {
        var hooks = Plugin.PlayerInputHooks;
        var asleep = !Dead && HasStatus(StatusIdSleep);
        var confused = !Dead && HasStatus(StatusIdConfused);
        var bound = !Dead && HasStatus(StatusIdBind);
        var incapacitated = asleep || confused;
        hooks.ZeroMovement = Dead || Movement.IsMoving || incapacitated || bound || HasAnyStatus(LocksMovement);
        hooks.DisableAllActions = Dead || incapacitated || HasAnyStatus(LocksActions);
        // A knockback slide still lets you turn, so this isn't folded into ZeroMovement.
        hooks.ZeroRotation = Dead || incapacitated;
        // Sleep pins the rotation it landed at; Confused re-pins every tick, since the
        // scenario's Follow already turned the player toward the ally it walks them into.
        if (asleep) hooks.LockedRotation ??= Rotation;
        else if (confused) hooks.LockedRotation = Rotation;
        else hooks.LockedRotation = null;
    }

    // Sim statuses never reach the server, so their Status sheet locks (e.g. Fetters) are applied here.
    private static readonly Dictionary<ushort, (bool Movement, bool Actions)> statusLocks = [];

    private static (bool Movement, bool Actions) LocksOf(ushort statusId)
    {
        if (statusLocks.TryGetValue(statusId, out var locks)) return locks;
        var row = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>().GetRowOrDefault(statusId);
        return statusLocks[statusId] = (row?.LockMovement ?? false, row?.LockActions ?? false);
    }

    private static bool LocksMovement(ushort statusId) => LocksOf(statusId).Movement;

    private static bool LocksActions(ushort statusId) => LocksOf(statusId).Actions;
}
