namespace AnoMech.Core.Native.Interfaces;

// Scenario music in the Content BGM scene.
public interface IBgm
{
    // Idempotent for the track already playing.
    void Play(ushort bgmId, float secondsIn = 0f);
    void Sync(float secondsIn);

    // Hands the scene back so the territory's own BGM resumes.
    void Reset();

    void Tick(float deltaSeconds);
    void LogPosition(string label);
}
