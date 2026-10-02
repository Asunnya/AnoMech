using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native;
using AnoMech.Core.UserActions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace AnoMech.Core.SimObjects;

public sealed unsafe class SimPartyNpc : SimNpc, ISimPartyMember
{
    public PartyRole Role { get; set; }
    public bool Dead { get; private set; }
    public byte ClassJob { get; }
    public string DisplayName { get; }

    internal SimPartyNpc(int index, Coordinates coordinates, PartyRole role, byte classJob, string name) : base(index, coordinates)
    {
        Role = role;
        ClassJob = classJob;
        DisplayName = name;
    }

    // A bot's button press: the animation, then the same JobActions effects a player's press applies.
    private void UseAction(uint actionId)
    {
        PlayAction(actionId);
        JobActions.ApplyEffects(this, actionId, (ulong)GameObjectId, Random.Shared);
    }

    // level 1-3. False if KO'd or the job has no limit break at that level.
    internal bool UseLimitBreak(int level)
    {
        if (!this.IsAlive()) return false;
        var actionId = LimitBreakHandler.ActionId(ClassJob, level);
        if (actionId == 0) return false;
        DiagnosticLog.Info($"[SimPartyNpc] {Role} (job {ClassJob}) uses LB{level} {ActionLookup.Name(actionId)}.");
        UseAction(actionId);
        return true;
    }

    private static readonly Dictionary<byte, uint> InvulnActionIdByJob = new()
    {
        [19] = 30,    // Paladin: Hallowed Ground
        [21] = 43,    // Warrior: Holmgang
        [32] = 3638,  // Dark Knight: Living Dead
        [37] = 16152, // Gunbreaker: Superbolide
    };

    // False for a non-tank job.
    public bool UseInvuln()
    {
        if (!InvulnActionIdByJob.TryGetValue(ClassJob, out var actionId)) return false;
        UseAction(actionId);
        return true;
    }

    public void Knockback(Vector3 source, float distance, float speed) => Movement.Knockback(source, distance, speed);

    public void PushInDirection(float heading, float distance, float speed) => Movement.PushInDirection(heading, distance, speed);

    public void PushInDirectionEased(float heading, float distance, float durationSeconds) => Movement.PushInDirectionEased(heading, distance, durationSeconds);

    public override void Despawn()
    {
        base.Despawn();
    }

    public void OnKilled()
    {
        Dead = true;
        StopMoving();
        var bc = BattleCharaPtr;
        if (bc == null) return;
        ApplyDeadState(bc);
        this.PlayKoActionTimeline();
    }

    private static void ApplyDeadState(BattleChara* bc)
    {
        bc->Health = 0;
        bc->Mana = 0;
        bc->Mode = CharacterModes.Dead;
    }
}
