using System.Linq;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

public class RngTests
{
    private static int[] Draws(Rng rng, int count = 32) => Enumerable.Range(0, count).Select(_ => rng.Next(1000)).ToArray();

    [Test]
    public void SameSeedSameSequence()
    {
        Assert.That(Draws(new Rng(42)), Is.EqualTo(Draws(new Rng(42))));
    }

    [Test]
    public void DifferentSeedDifferentSequence()
    {
        Assert.That(Draws(new Rng(42)), Is.Not.EqualTo(Draws(new Rng(43))));
    }

    [Test]
    public void ShuffleFollowsSeed()
    {
        var values = Enumerable.Range(0, 20).ToArray();
        Assert.That(new Rng(7).Shuffle(values), Is.EqualTo(new Rng(7).Shuffle(values)));
        Assert.That(new Rng(7).Shuffle(values), Is.EquivalentTo(values));
    }

    [Test]
    public void ForkDependsOnlyOnSeedAndName()
    {
        var a = new Rng(42);
        var b = new Rng(42);
        Draws(b);   // the parent's draw position must not matter
        Assert.That(Draws(a.Fork("bot-actions")), Is.EqualTo(Draws(b.Fork("bot-actions"))));
    }

    [Test]
    public void ForksAreIndependentStreams()
    {
        var rng = new Rng(42);
        Assert.That(Draws(rng.Fork("bot-actions")), Is.Not.EqualTo(Draws(rng.Fork("party-spawn"))));
        Assert.That(Draws(rng.Fork("bot-actions")), Is.Not.EqualTo(Draws(new Rng(42))));
    }

    // Pinned value: a saved failing seed must replay identically across processes and .NET upgrades.
    [Test]
    public void ForkSeedIsStable()
    {
        Assert.That(new Rng(42).Fork("bot-actions").Seed, Is.EqualTo(ExpectedForkSeed));
    }

    private const int ExpectedForkSeed = -762844398;
}
