using System.Numerics;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

internal sealed class FakeLayoutFunctions : ILayoutFunctions
{
    public int SuppressLayer(ushort layerKey) => 0;
    public void ReassertSuppressedLayers() { }
    public void ClearSuppressedLayers() { }

    // Any non-zero count: the zone is fully streamed in from the first frame.
    public int DisableSpawnAreaColliders(Vector3 center, float radius) => 1;

    public string DescribeActiveLayers() => "fake";
    public string DescribeLiveEffects() => "fake";
}
