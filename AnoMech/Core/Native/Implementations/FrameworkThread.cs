using System;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Core.Native.Implementations;

internal sealed class FrameworkThread : IFrameworkThread
{
    public void Run(Action action) => Plugin.Framework.Run(action);
}
