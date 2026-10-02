namespace AnoMech.Core.Native.Interfaces;

// Debug trace of every VFX the client creates while enabled.
public interface IVfxSpawnLog
{
    // Frames counted by Tick, for lining log lines up with the trace.
    long Frame { get; }

    void Enable();
    void Disable();
    void Tick();
}
