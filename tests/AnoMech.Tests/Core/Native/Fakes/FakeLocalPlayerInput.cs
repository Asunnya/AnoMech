using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

// Nobody at the keyboard.
internal sealed class FakeLocalPlayerInput : ILocalPlayerInput
{
    public bool MovementInputActive => false;
    public bool IsJumping => false;
    public bool IsAutoAttacking => false;
    public bool PollActionUsed() => false;

    public bool ZeroMovement { get; set; }
    public bool ZeroRotation { get; set; }
    public bool DisableAllActions { get; set; }
    public float? LockedRotation { get; set; }

    public void SetStatusAffliction(bool afflicted) { }
}
