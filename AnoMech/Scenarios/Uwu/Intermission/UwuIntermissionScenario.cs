using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Network;
using static AnoMech.Scenarios.Uwu.UwuConstants;
using static AnoMech.Scenarios.Uwu.UwuUtils;
using static AnoMech.Scenarios.Uwu.P3Titan.UwuP3TitanState;

namespace AnoMech.Scenarios.Uwu.Intermission;

// From Titan vanishing to Lahabrea fading out, just before Ultima's cast that opens Ultimate Predation.
public sealed class UwuIntermissionScenario : IScenario
{
    public string Name => "Intermission";
    public IPhase Phase => UwuZone.Intermission;
    public bool SupportsSolo => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new UwuIntermissionAi()];

    private const float FreefireCasterRadius = 18f;
    private const float MagitekBitRadius = 13f;
    private const uint MagitekBitMaxHp = 59338;
    private const float MagitekBitsDrainFrom = 12.0f;
    private const float MagitekBitsDrainTo = 15.3f;
    private const uint LahabreaMaxHp = 95389;
    private const float LahabreaDrainFrom = 30.0f;
    private const float LahabreaDrainTo = 43.6f;
    private const uint InterruptCastControl = 15;
    private const uint InterruptCastReason = 538;
    private const uint FadeOutControl = 607;

    private SimWorld world = null!;
    private SimParty party = null!;
    private UwuUtils utils = null!;
    private DamageSolver damage = null!;

    private SimEnemy? lahabrea;
    private readonly List<SimEnemy> freefireCasters = [];
    private readonly List<SimEnemy> magitekBits = [];

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = world.Party;
        utils = new UwuUtils(world);
        damage = new DamageSolver(party);
        lahabrea = null;
        freefireCasters.Clear();
        magitekBits.Clear();

        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<UwuIntermissionState>)AiStrats[idx]).Run(new UwuIntermissionState(), world);

        world.Events.Add(0f, () => utils.SpawnArenaFloor());
        world.Events.Add(6.68f, SpawnFreefireCasters);
        world.Events.Add(7.14f, Freefire);
        world.Events.Add(8.83f, SpawnMagitekBits);
        world.Events.Add(10.18f, () => { foreach (var bit in magitekBits) CastSelf(bit, ActionId.SelfDetonate, 11.7f); });
        world.Events.Add(15.35f, InterruptMagitekBits);
        world.Events.Add(17.20f, KillMagitekBits);
        world.Events.Add(18.06f, SpawnLahabrea);
        world.Events.Add(20.07f, Blight);
        world.Events.Add(26.03f, () => { foreach (var member in AliveMembers()) member.RemoveStatus(StatusId.Doom); });
        world.Events.Add(29.15f, () => lahabrea?.SetTargetable(true));
        world.Events.Add(29.15f, () => CastSelf(lahabrea, ActionId.DarkIV, 16.7f));
        world.Events.Add(43.72f, () => lahabrea?.SetTargetable(false));
        world.Events.Add(43.81f, () => lahabrea?.PlayActionTimeline(ActionTimelineId.LahabreaFalls));
        world.Events.Add(46.84f, () => lahabrea?.PlayActionTimeline(ActionTimelineId.LahabreaFadesOut));
        world.Events.Add(47.91f, DespawnAll);
    }

    public void Tick(float delta, float elapsed)
    {
        foreach (var bit in magitekBits) Drain(bit, MagitekBitMaxHp, MagitekBitsDrainFrom, MagitekBitsDrainTo, elapsed);
        Drain(lahabrea, LahabreaMaxHp, LahabreaDrainFrom, LahabreaDrainTo, elapsed);
    }

    private IEnumerable<SimCharacter> AliveMembers()
    {
        for (var slot = 0; slot < 8; slot++)
            if (party.Get(slot) is { } member && member.IsAlive())
                yield return member;
    }

    private SimEnemy? SpawnEnemy(uint baseId, uint nameId, Placement placement, bool targetable, EnemyListMode enemyList) =>
        world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: baseId,
            NameId: nameId,
            Level: Level,
            Targetable: targetable,
            EnemyList: enemyList,
            IsVisible: true,
            Placement: placement));

    private static unsafe void SetHp(SimEnemy? enemy, uint maxHp, float fraction)
    {
        var chara = enemy?.BattleCharaPtr;
        if (chara == null) return;
        chara->MaxHealth = maxHp;
        chara->Health = (uint)MathF.Ceiling(maxHp * Math.Clamp(fraction, 0f, 1f));
    }

    // The party burns the bits and Lahabrea on the clear's schedule; the player's hits don't change it.
    private static void Drain(SimEnemy? enemy, uint maxHp, float from, float to, float elapsed)
    {
        if (enemy is not { IsActive: true } || elapsed < from - 1f) return;
        SetHp(enemy, maxHp, 1f - Math.Clamp((elapsed - from) / (to - from), 0f, 1f));
    }

    private void SpawnFreefireCasters()
    {
        for (var i = 0; i < 4; i++)
        {
            var at = AtBearing(90f * i, FreefireCasterRadius);
            if (SpawnEnemy(BNpcBaseId.Dummy, BNpcNameId.UltimaWeapon, new Placement(at, FacingCentre(at)), false, EnemyListMode.Never) is { } caster)
                freefireCasters.Add(caster);
        }
    }

    private void Freefire()
    {
        foreach (var caster in freefireCasters)
        {
            PlayEffect(caster, ActionId.FreefireIntermission, 1.1f);
            foreach (var member in AliveMembers())
                damage.ApplyDamage(member, 0.16f, ActionId.FreefireIntermission, "Raidwide", false);
        }
    }

    private void SpawnMagitekBits()
    {
        for (var i = 0; i < 6; i++)
        {
            var at = AtBearing(30f + 60f * i, MagitekBitRadius);
            if (SpawnEnemy(BNpcBaseId.MagitekBit, BNpcNameId.MagitekBit, new Placement(at, FacingCentre(at)), true, EnemyListMode.Always) is not { } bit) continue;
            magitekBits.Add(bit);
            SetHp(bit, MagitekBitMaxHp, 1f);
        }
    }

    private void InterruptMagitekBits()
    {
        foreach (var bit in magitekBits)
            PacketDispatcher.HandleActorControlPacket(bit.EntityId, InterruptCastControl, InterruptCastReason, 1, ActionId.SelfDetonate, 0, 0, 0, 0, 0, 0xE0000000, false);
    }

    private void KillMagitekBits()
    {
        foreach (var bit in magitekBits)
        {
            bit.SetTargetable(false);
            PacketDispatcher.HandleActorControlPacket(bit.EntityId, FadeOutControl, bit.EntityId, 1, 0, 100, 0, 0, 0, 0, 0xE0000000, false);
            world.Events.Add(1.5f, bit.Despawn);
        }
        magitekBits.Clear();
    }

    private void SpawnLahabrea()
    {
        lahabrea = SpawnEnemy(BNpcBaseId.Lahabrea, BNpcNameId.Lahabrea, new Placement(new Vector3(0f, 0f, -0.9f), 0f), false, EnemyListMode.Always);
        SetHp(lahabrea, LahabreaMaxHp, 1f);
        lahabrea?.PlayActionTimeline(ActionTimelineId.WarpEnd);
    }

    // Everyone is doomed and stunned; the Doom lifts before it runs out.
    private void Blight()
    {
        PlayEffect(lahabrea, ActionId.Blight, 1.1f);
        foreach (var member in AliveMembers())
        {
            member.StopMoving();
            member.AddStatus(StatusId.Doom, 7.96f);
            member.AddStatus(StatusId.DownForTheCount, 3.96f);
        }
    }

    private void DespawnAll()
    {
        lahabrea?.Despawn();
        lahabrea = null;
        foreach (var enemy in freefireCasters.Concat(magitekBits)) enemy.Despawn();
        freefireCasters.Clear();
        magitekBits.Clear();
    }
}
