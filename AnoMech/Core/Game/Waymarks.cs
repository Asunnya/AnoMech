using System;
using System.Collections.Generic;
using System.Numerics;

namespace AnoMech.Core.Game;

public enum WaymarkSlot : int
{
    A = 0, B = 1, C = 2, D = 3,
    One = 4, Two = 5, Three = 6, Four = 7,
}

public sealed record Waymark(WaymarkSlot Slot, Vector3 Offset);

// A named waymark layout, selectable in the main window's "Waymarks:" dropdown.
// Markers are scenario-local offsets, same frame as Waymark.Offset.
public sealed record WaymarkLayout(string Name, IReadOnlyList<Waymark> Markers);

public static class WaymarkPresets
{
    // Ring of A,1,B,2,C,3,D,4 spaced 45° apart, A at north going clockwise.
    // Angle convention: offset = (sin(a)*r, 0, cos(a)*r), a=π is north (-Z).
    public static Waymark[] Ring(float radius)
    {
        var slots = new[]
        {
            WaymarkSlot.A, WaymarkSlot.One,
            WaymarkSlot.B, WaymarkSlot.Two,
            WaymarkSlot.C, WaymarkSlot.Three,
            WaymarkSlot.D, WaymarkSlot.Four,
        };
        var ring = new Waymark[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            var angle = MathF.PI - i * (MathF.PI / 4f);
            ring[i] = new Waymark(slots[i], new Vector3(radius * MathF.Sin(angle), 0, radius * MathF.Cos(angle)));
        }
        return ring;
    }
}
