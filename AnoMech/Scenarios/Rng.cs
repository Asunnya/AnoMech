using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios;

// A run's randomness. Same seed, same draw order => same rolls, so a run replays from its seed.
public class Rng
{
    private readonly Random rng;

    public int Seed { get; }

    // For state that never decides the run (a peer's replay shadow): fixed, not the run's seed.
    public static Rng Detached => new(0);

    public Rng(int seed)
    {
        Seed = seed;
#pragma warning disable RS0030 // the one seeded Random everything else draws through
        rng = new Random(seed);
#pragma warning restore RS0030
    }

    // An independent stream for one consumer. Derived from (Seed, name) only, so draws on one
    // stream never shift another's. FNV-1a, not string.GetHashCode: that one is randomized per process.
    public Rng Fork(string name)
    {
        const ulong prime = 1099511628211;
        var hash = 14695981039346656037;
        foreach (var b in BitConverter.GetBytes(Seed))
            hash = (hash ^ b) * prime;
        foreach (var c in name)
            hash = (hash ^ c) * prime;
        return new Rng((int)(hash ^ (hash >> 32)));
    }

    public int Next(int maxExclusive) => rng.Next(maxExclusive);

    public float NextSingle() => rng.NextSingle();

    public double NextDouble() => rng.NextDouble();

    public bool NextBool()
    {
        return (rng.Next(2) == 0);
    }

    public T NextObj<T>(params T[] values)
    {
        return values[rng.Next(values.Length)];
    }

    public Direction NextDirection()
    {
        return Direction.All[rng.Next(8)];
    }

    public int NextSign()
    {
        return rng.Next(2) * 2 - 1;
    }

    public int NextInt(int i)
    {
        return rng.Next(i);
    }

    public Direction NextIntercardinal()
    {
        return Direction.Intercardinal[rng.Next(4)];
    }

    public Direction NextCardinal()
    {
        return Direction.Cardinal[rng.Next(4)];
    }

    public PartyRole NextRole()
    {
        return (PartyRole)rng.Next(8);
    }

    // Our own Fisher-Yates: LINQ's Enumerable.Shuffle draws from the unseeded Random.Shared.
    public IReadOnlyList<T> Shuffle<T>(params IEnumerable<T> values)
    {
        var list = values.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>
    /// Re-orders <paramref name="array"/> and returns a copy starting from a random index.
    /// </summary>
    public T[] RandomStart<T>(T[] array)
    {
        var start = rng.Next(array.Length);

        return array.Skip(start)
            .Concat(array.Take(start))
            .ToArray();
    }

    public PartyRole NextSupportRole()
    {
        return (PartyRole)rng.Next(4);
    }

    public PartyRole NextDpsRole()
    {
        return (PartyRole)(rng.Next(4) + 4);
    }

    public PartyRole NextHealerRole()
    {
        return (PartyRole)(rng.Next(2) + 2);
    }

    public PartyRole NextTankRole()
    {
        return (PartyRole)rng.Next(2);
    }
}
