using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s;

public enum BatSpin
{
    Clockwise = 1,
    CounterClockwise = -1,
}

// One ring of bats: Count bats evenly spaced from BaseBearing, all circling the arena centre the
// same way and exploding together once they have covered TravelDegrees.
public sealed record BatRing(int Count, float Radius, float BaseBearing, BatSpin Spin, float TravelDegrees, float CastAt)
{
    public float StartBearing(int index) => BaseBearing + index * 360f / Count;
    public float EndBearing(int index) => StartBearing(index) + (int)Spin * TravelDegrees;
}

// Vamp Stomp's bat rings, randomized per run. All three Vamp Stomps share it, timed from the stomp:
// times are on the first stomp's clock, moved by `shift` for a scenario whose stomp starts earlier.
//
// Measured from four pulls of one log (the second Vamp Stomp lines up with the first to within
// 0.05s): the rings always hold 2/3/5 bats at 6/13/20y, start moving 4.6s after the stomp starts
// casting and cast Blast Beat at 33.51/36.99/40.48s on the first stomp's clock. What varies is
// each ring's spin, picked independently, and where the ring starts, which only ever lands on a
// fixed step: 45° inner, 30° middle, 18° outer. Travel is BossMod's (88° inner, 90° otherwise),
// which the log agrees with to within a degree.
public sealed class M9sBatPattern
{
    public const float BlastRadius = 8f;
    public const float BatRingSpeed = 2f;

    public float BatMoveStartAt { get; }
    public float BatRingStartAt { get; }
    public IReadOnlyList<BatRing> Rings { get; }

    public M9sBatPattern(BatSpin? spin, Rng rng, float shift = 0f)
    {
        BatMoveStartAt = 29.93f + shift;
        BatRingStartAt = 30.47f + shift;
        Rings =
        [
            NewRing(rng, 2, 6f, 45f, 88f, 33.51f + shift, spin),
            NewRing(rng, 3, 13f, 30f, 90f, 36.99f + shift, spin),
            NewRing(rng, 5, 20f, 18f, 90f, 40.48f + shift, spin),
        ];
    }

    private static BatRing NewRing(Rng rng, int count, float radius, float step, float travel, float castAt, BatSpin? spin)
    {
        var steps = (int)MathF.Round(360f / count / step);
        return new BatRing(count, radius, rng.NextInt(steps) * step,
            spin ?? rng.NextObj(BatSpin.Clockwise, BatSpin.CounterClockwise), travel, castAt);
    }

    public Placement BatPlacement(BatRing ring, int index, float time)
    {
        var progress = Math.Clamp((time - BatMoveStartAt) / (ring.CastAt - BatMoveStartAt), 0f, 1f);
        var bearing = ring.StartBearing(index) + (int)ring.Spin * ring.TravelDegrees * progress;
        return new Placement(AtBearing(bearing, ring.Radius), FacingAlongOrbit(bearing, ring.Spin));
    }

    public IReadOnlyList<Vector3> BlastPositions(int ringIndex)
    {
        var ring = Rings[ringIndex];
        return Enumerable.Range(0, ring.Count).Select(i => AtBearing(ring.EndBearing(i), ring.Radius)).ToList();
    }

    public float BatRingRadiusAt(float time) => MathF.Max(0f, (time - BatRingStartAt) * BatRingSpeed);

    // Bearings are compass degrees: 0 = north (-Z), 90 = east (+X).
    public static Vector3 AtBearing(float bearingDegrees, float radius)
    {
        var rad = bearingDegrees * MathF.PI / 180f;
        return new Vector3(radius * MathF.Sin(rad), 0f, -radius * MathF.Cos(rad));
    }

    // Placement rotation 0 faces +Z; the orbit tangent for a compass bearing b is ±90° - b.
    private static float FacingAlongOrbit(float bearingDegrees, BatSpin spin)
    {
        var rad = bearingDegrees * MathF.PI / 180f;
        return (int)spin * MathF.PI / 2f - rad;
    }
}

// The Vamp Stomp mechanic itself, shared by both stomps: the 10y stomp, the curse on everyone, the
// bats and their blasts, and the Curse of the Bombpyre ring. Scenarios call these at their own
// absolute times; only the bat orbit and ring growth schedule their own fine-grained steps.
//
// Every player gets Curse of the Bombpyre with Vamp Stomp. An invisible ring then grows from the
// centre at 2y/s starting with BatRing; whoever it reaches loses the curse and explodes for 8y,
// giving everyone caught Magic Vulnerability Up. That is what makes the stomp a spread: two
// explosions landing on one player, or a bat blast on top of one, kill.
public sealed class M9sBatStomp(SimWorld world, DamageSolver damage, M9sSatisfied satisfied, M9sBatPattern pattern, Func<Vector3, SimEnemy?> spawnHelper)
{
    private const float OrbitStep = 0.05f;
    private const float CurseRingStep = 0.05f;
    private const float CurseRingSeconds = 16f;

    private SimEnemy? stompHelper;
    private SimOmen? curseRing;
    private readonly List<(SimEnemy Bat, BatRing Ring, int Index)> bats = [];

    public void ApplyCurses()
    {
        for (var slot = 0; slot < 8; slot++)
            if (world.Party.Get(slot) is { } member && member.IsAlive())
                member.AddStatus(StatusId.CurseOfTheBombpyre);
    }

    public void CastVampStomp()
    {
        stompHelper = spawnHelper(Vector3.Zero);
        stompHelper?.Cast(ActionId.VampStomp, castSeconds: 4.7f);
    }

    public void ResolveVampStomp() => satisfied.AddFor(damage.Resolve(stompHelper, ActionId.VampStomp, [DamageType.Lethal], []));

    public void CastBatRing() => stompHelper?.Cast(ActionId.BatRing, castSeconds: 0f, animationLock: 0f);

    public void SpawnBats()
    {
        bats.Clear();
        foreach (var ring in pattern.Rings)
            for (var i = 0; i < ring.Count; i++)
            {
                var bat = world.SpawnEnemy(new EnemySpawnConfig(
                    BNpcBaseId: BNpcBaseId.VampetteFatale,
                    NameId: BNpcNameId.VampetteFatale,
                    Level: 100,
                    Targetable: false,
                    EnemyList: EnemyListMode.Never,
                    IsVisible: true,
                    Placement: pattern.BatPlacement(ring, i, 0f)));
                if (bat != null) bats.Add((bat, ring, i));
            }
    }

    // Bats orbit by position steps rather than MoveTo, which only walks straight lines. Stepping on
    // world.Events keeps the flight locked to the blasts under EventTimeScale.
    public void ScheduleBatFlight()
    {
        for (var t = pattern.BatMoveStartAt; t <= pattern.Rings[^1].CastAt + OrbitStep; t += OrbitStep)
        {
            var at = t;
            world.Events.Add(at, () =>
            {
                foreach (var (bat, ring, index) in bats)
                    if (at <= ring.CastAt + OrbitStep) bat.SetPosition(pattern.BatPlacement(ring, index, at));
            });
        }
    }

    public void CastBlastBeat(int ringIndex)
    {
        foreach (var (bat, ring, _) in bats)
            if (ring == pattern.Rings[ringIndex]) bat.Cast(ActionId.BlastBeatBat, castSeconds: 0.7f);
    }

    public void ResolveBlastBeat(int ringIndex)
    {
        foreach (var (bat, ring, _) in bats)
            if (ring == pattern.Rings[ringIndex]) satisfied.AddFor(damage.Resolve(bat, ActionId.BlastBeatBat, [DamageType.Lethal], []));
    }

    public void DespawnBats(int ringIndex)
    {
        foreach (var (bat, ring, _) in bats)
            if (ring == pattern.Rings[ringIndex]) bat.Despawn();
    }

    public void ScheduleCurseRing()
    {
        world.Events.Add(pattern.BatRingStartAt, () =>
            curseRing = world.SpawnOmen(VfxPath.CurseRing, new Placement(Vector3.Zero, 0f), RingScale(CurseRingStep * M9sBatPattern.BatRingSpeed), CurseRingSeconds));

        var endAt = pattern.BatRingStartAt + CurseRingSeconds;
        for (var t = pattern.BatRingStartAt + CurseRingStep; t <= endAt; t += CurseRingStep)
        {
            var radius = pattern.BatRingRadiusAt(t);
            world.Events.Add(t, () =>
            {
                curseRing?.SetScale(RingScale(radius));
                DetonateCursesInside(radius);
            });
        }
    }

    // Omen files are unit-sized; SimOmen scales circles and donuts by EffectRange the same way.
    private static Vector3 RingScale(float radius) => new(radius, 1f, radius);

    private void DetonateCursesInside(float radius)
    {
        for (var slot = 0; slot < 8; slot++)
        {
            if (world.Party.Get(slot) is not { } member || !member.HasStatus(StatusId.CurseOfTheBombpyre)) continue;
            if (new Vector2(member.Position.X, member.Position.Z).Length() > radius) continue;

            member.RemoveStatus(StatusId.CurseOfTheBombpyre);
            spawnHelper(Vector3.Zero)?.Cast(ActionId.BlastBeatSpread, castSeconds: 0f, targetId: member.GameObjectId, animationLock: 0f);
            var hit = damage.Resolve(member, ActionId.BlastBeatSpread, [DamageType.Magic],
                [(StatusId.MagicVulnerabilityUp, 1.96f)]);
            satisfied.AddFor(hit, except: member);
        }
    }

    public void DespawnBats()
    {
        foreach (var (bat, _, _) in bats) bat.Despawn();
        bats.Clear();
    }
}
