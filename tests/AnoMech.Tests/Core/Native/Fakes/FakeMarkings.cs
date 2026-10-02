using AnoMech.Core.Native.Interfaces;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AnoMech.Tests;

internal sealed class FakeMarkings : IMarkings
{
    public void Set(Sign sign, GameObjectId target) { }
    public void Clear(Sign sign) { }
    public void ClearAll() { }
}
