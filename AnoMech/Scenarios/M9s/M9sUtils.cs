using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s;

// Satisfied stacks: one per avoidable hit; 8+ enlarges attacks, Brutal Rain hits 3 + stacks/4.
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

// Hardcore hits both tanks, falling back to the next living players.
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
    // The server sends it with 0x07 as a saw corridor goes up and 0x01 as it comes down.
    public const uint CorridorDirectorCommand = 0x8000000D;

    // Shows the hit only; `fraction` is the log's median share of max HP.
    public static void Raidwide(SimParty party, DamageSolver damage, uint actionId, float fraction)
    {
        for (var slot = 0; slot < 8; slot++)
            if (party.Get(slot) is { } member && member.IsAlive())
                damage.ApplyDamage(member, fraction, actionId, "raidwide", lethal: false);
    }
}
