using System.Numerics;
using AnoMech.Core.Game;

namespace AnoMech.Core.Native.Interfaces;

// World VFX. Character-attached VFX and tethers live on IBattleCharaProxy.
public interface IVfxFunctions
{
    // Placement is world space. Null when the path doesn't exist or the spawn failed: a bad path
    // crashes on the file thread, so it is checked first.
    IStaticVfxProxy? SpawnStatic(string path, Placement placement, Vector3 scale);

    // Lockon-sheet icon name for a head marker (vfx/lockon/eff/{name}.avfx); null for an unknown id.
    string? LockonIconName(uint lockonId);
}
