using System.Numerics;

namespace AnoMech.Core.Native.Interfaces;

// A VFX placed in the world (VfxObject), e.g. an omen.
public interface IStaticVfxProxy
{
    void SetScale(Vector3 scale);
    void Remove();
}
