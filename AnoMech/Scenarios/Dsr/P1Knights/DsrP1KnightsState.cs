using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Dsr.P1Knights;

public enum ChainSymbol { Circle, Triangle, Square, Cross }

public enum Dimension { Empty, Full }

public sealed class DsrP1KnightsState
{
    public static readonly Vector3 PrisonCentre = new(-11f, 0f, 0f);
    public static readonly Vector3 CharibertSpot = new(-11f, 0f, 5f);

    private static readonly PartyRole[] Tanks = [PartyRole.MainTank, PartyRole.OffTank];
    private static readonly PartyRole[] Healers = [PartyRole.RegenHealer, PartyRole.ShieldHealer];
    private static readonly PartyRole[] Dps = [PartyRole.MeleeDpsA, PartyRole.MeleeDpsB, PartyRole.PhysRangedDps, PartyRole.CasterDps];
    private static readonly PartyRole[] NonTanks = [.. Healers, .. Dps];

    public static readonly IReadOnlyList<PartyRole> HallowingInterrupters = [PartyRole.OffTank, PartyRole.PhysRangedDps, PartyRole.OffTank];

    // Brightwing bait order: healers, melee, ranged, tanks.
    public static readonly IReadOnlyList<PartyRole[]> BrightwingPairs =
    [
        [PartyRole.RegenHealer, PartyRole.ShieldHealer],
        [PartyRole.MeleeDpsA, PartyRole.MeleeDpsB],
        [PartyRole.PhysRangedDps, PartyRole.CasterDps],
        [PartyRole.MainTank, PartyRole.OffTank],
    ];

    public PartyRole ShieldBashSeed { get; }
    public PartyRole HeavensblazeTarget { get; }
    public IReadOnlyList<PartyRole> FirstSlashTargets { get; }
    public IReadOnlyList<PartyRole> SecondSlashTargets { get; }
    public float AdelphelLandingBearing { get; }
    public float AdelphelFirstDashBearing { get; }
    public IReadOnlyDictionary<PartyRole, ChainSymbol> Symbols { get; }
    public Dimension SecondDimension { get; }

    public SimTether? ShieldBashTether { get; set; }

    public DsrP1KnightsState(Rng rng)
    {
        ShieldBashSeed = NonTanks[rng.Next(NonTanks.Length)];
        HeavensblazeTarget = NonTanks[rng.Next(NonTanks.Length)];

        var shuffled = rng.Shuffle(Enum.GetValues<PartyRole>()).ToList();
        FirstSlashTargets = shuffled.Take(4).OrderBy(r => r).ToList();
        SecondSlashTargets = shuffled.Skip(4).OrderBy(r => r).ToList();

        AdelphelLandingBearing = 90f * rng.Next(4);
        AdelphelFirstDashBearing = (AdelphelLandingBearing + (rng.Next(2) == 0 ? 90f : 270f)) % 360f;

        var crossTank = Tanks[rng.Next(2)];
        var crossHealer = Healers[rng.Next(2)];
        var dps = rng.Shuffle(Dps).ToList();
        var symbols = new Dictionary<PartyRole, ChainSymbol>
        {
            [crossTank] = ChainSymbol.Cross,
            [crossHealer] = ChainSymbol.Cross,
            [Tanks.First(t => t != crossTank)] = ChainSymbol.Square,
            [Healers.First(h => h != crossHealer)] = ChainSymbol.Triangle,
            [dps[0]] = ChainSymbol.Circle,
            [dps[1]] = ChainSymbol.Circle,
            [dps[2]] = ChainSymbol.Square,
            [dps[3]] = ChainSymbol.Triangle,
        };
        Symbols = symbols;
        SecondDimension = rng.Next(2) == 0 ? Dimension.Empty : Dimension.Full;
    }

    public IEnumerable<(PartyRole A, PartyRole B, ChainSymbol Symbol)> ChainPairs() =>
        Symbols.GroupBy(kv => kv.Value)
            .Select(g => g.Select(kv => kv.Key).OrderBy(r => r).ToList())
            .Select(pair => (pair[0], pair[1], Symbols[pair[0]]));

    public static bool IsTank(PartyRole role) => role is PartyRole.MainTank or PartyRole.OffTank;
    public static bool IsHealer(PartyRole role) => role is PartyRole.RegenHealer or PartyRole.ShieldHealer;
    public static bool IsMelee(PartyRole role) => role is PartyRole.MeleeDpsA or PartyRole.MeleeDpsB;

    // Tanks take north/north-east, healers south/south-east, DPS the west half; circle goes east-west.
    public float ChainBearing(PartyRole role)
    {
        var symbol = Symbols[role];
        return symbol switch
        {
            ChainSymbol.Cross => IsTank(role) ? 0f : 180f,
            ChainSymbol.Square => IsTank(role) ? 45f : 225f,
            ChainSymbol.Triangle => IsHealer(role) ? 135f : 315f,
            _ => TakesEastCircle(role) ? 90f : 270f,
        };
    }

    private bool TakesEastCircle(PartyRole role)
    {
        var partner = Symbols.First(kv => kv.Value == ChainSymbol.Circle && kv.Key != role).Key;
        if (IsMelee(role) != IsMelee(partner)) return IsMelee(role);
        return role < partner;
    }

    public Vector3 AdelphelLanding => DsrConstants.AtBearing(AdelphelLandingBearing, DsrConstants.Geometry.ArenaHalfWidth);

    // Hourglass: landing -> first dash -> across -> opposite of landing -> back to landing.
    public IReadOnlyList<Vector3> AdelphelDashPath()
    {
        var a = AdelphelLandingBearing;
        var b = AdelphelFirstDashBearing;
        var r = DsrConstants.Geometry.ArenaHalfWidth;
        return
        [
            DsrConstants.AtBearing(a, r),
            DsrConstants.AtBearing(b, r),
            DsrConstants.AtBearing(b + 180f, r),
            DsrConstants.AtBearing(a + 180f, r),
            DsrConstants.AtBearing(a, r),
        ];
    }

    // The quadrant between the first dash cardinal and the one opposite the landing is never crossed.
    public float SafeQuadrantBearing()
    {
        var b = AdelphelFirstDashBearing;
        var oppositeLanding = (AdelphelLandingBearing + 180f) % 360f;
        var diff = ((oppositeLanding - b + 540f) % 360f) - 180f;
        return (b + diff / 2f + 360f) % 360f;
    }
}
