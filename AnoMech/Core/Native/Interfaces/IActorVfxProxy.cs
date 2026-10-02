namespace AnoMech.Core.Native.Interfaces;

// A VFX attached to a character (VfxData). Remove only one the game hasn't freed itself: removing
// a self-completed fire-and-forget VFX crashes in VfxData::Dtor.
public interface IActorVfxProxy
{
    void Remove();
}
