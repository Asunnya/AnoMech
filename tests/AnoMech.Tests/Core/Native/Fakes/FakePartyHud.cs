using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;

namespace AnoMech.Tests;

internal sealed class FakePartyHud : IPartyHud
{
    public void Refresh(SimParty party) { }
    public void Clear() { }
}
