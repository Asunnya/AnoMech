using System;

namespace AnoMech.Core.Native.Interfaces;

public interface IFrameworkThread
{
    // Runs on the framework thread, deferred to the next framework tick.
    void Run(Action action);
}
