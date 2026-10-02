using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

// Dalamud queues Framework.Run work and drains it at the start of the next frame, before Update.
internal sealed class FakeFrameworkThread : IFrameworkThread
{
    private readonly Queue<Action> pending = new();

    public void Run(Action action) => pending.Enqueue(action);

    internal void RunPending()
    {
        for (var count = pending.Count; count > 0; count--)
            pending.Dequeue()();
    }
}
