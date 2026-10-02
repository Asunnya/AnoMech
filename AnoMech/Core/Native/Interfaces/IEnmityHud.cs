using System.Collections.Generic;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Native.Interfaces;

// Mirrors the sim enemies into the _EnemyList addon, cast bars included.
public interface IEnmityHud
{
    void Refresh(IEnumerable<SimEnemy> enemies, float deltaSeconds);
    void Clear();
}
