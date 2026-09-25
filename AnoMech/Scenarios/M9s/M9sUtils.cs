using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s;

// Vamp Fatale's Satisfied stacks. Crowd Kill gives four at once; beyond that, each stack in the
// log lines up with a player getting hit by something avoidable, so every such player adds one
// here. At 8+ Hardcore and Half Moon switch to their larger versions, and Brutal Rain hits
// 3 + stacks/4 times: BossMod's formula, which matches the log's three opener hits at 0 stacks
// and four in the second Vamp Stomp at 4.
public sealed class M9sSatisfied(int initialStacks)
{
    public const int MoreThreshold = 8;

    private SimEnemy? boss;

    public int Stacks { get; private set; } = initialStacks;
    public bool IsMore => Stacks >= MoreThreshold;
    public int BrutalRainHits => 3 + Stacks / 4;

    public void Attach(SimEnemy? vamp)
    {
        boss = vamp;
        Show();
    }

    public void Add(int count)
    {
        if (count <= 0) return;
        Stacks += count;
        Show();
    }

    public void AddFor(IReadOnlyList<SimCharacter> hit, SimCharacter? except = null)
    {
        var count = 0;
        foreach (var member in hit)
            if (!ReferenceEquals(member, except)) count++;
        Add(count);
    }

    private void Show()
    {
        if (Stacks > 0) boss?.AddStatus(StatusId.Satisfied, stacks: Stacks, overrideStacks: true);
    }
}

// Hardcore: a buster on the top two of the enmity list. Every pull of the log put it on both tanks,
// so the tanks are the list here, with the next living players standing in if a tank is down.
public sealed class M9sHardcore(SimParty party, DamageSolver damage, M9sSatisfied satisfied, Func<Vector3, SimEnemy?> spawnHelper)
{
    private static readonly PartyRole[] EnmityOrder =
    [
        PartyRole.MainTank, PartyRole.OffTank, PartyRole.MeleeDpsA, PartyRole.MeleeDpsB,
        PartyRole.PhysRangedDps, PartyRole.CasterDps, PartyRole.RegenHealer, PartyRole.ShieldHealer,
    ];

    private readonly List<SimCharacter> targets = [];
    private uint actionId;

    public void Cast(Vector3 from)
    {
        targets.Clear();
        actionId = satisfied.IsMore ? ActionId.HardcoreBig : ActionId.HardcoreSmall;
        foreach (var role in EnmityOrder)
        {
            if (targets.Count == 2) break;
            if (party.Get(role) is not { } target || !target.IsAlive()) continue;
            targets.Add(target);
            target.AttachLockonVfx(LockonId.Tankbuster, persistent: false);
            spawnHelper(from)?.Cast(actionId, castSeconds: 4.7f, targetId: target.GameObjectId);
        }
    }

    public void Resolve()
    {
        foreach (var target in targets)
            damage.Resolve(target, actionId, [DamageType.TankBuster], []);
    }
}

public static class M9sUtils
{
    // Raidwides aren't lethal on their own here (nothing tracks HP), so they only show the hit:
    // `fraction` is the median share of max HP the clear's players took.
    public static void Raidwide(SimParty party, DamageSolver damage, uint actionId, float fraction)
    {
        for (var slot = 0; slot < 8; slot++)
            if (party.Get(slot) is { } member && member.IsAlive())
                damage.ApplyDamage(member, fraction, actionId, "raidwide", lethal: false);
    }
}
