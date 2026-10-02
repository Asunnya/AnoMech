using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

internal sealed class FakeVfxFunctions : IVfxFunctions
{
    public IStaticVfxProxy? SpawnStatic(string path, Placement placement, Vector3 scale) => new FakeVfxHandle();
    public bool PathExists(string path) => true;
    public string? LockonIconName(uint lockonId) => lockonId.ToString(CultureInfo.InvariantCulture);
}
