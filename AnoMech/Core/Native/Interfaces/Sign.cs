namespace AnoMech.Core.Native.Interfaces;

// Party head-marker "signs" (the 17-slot icon set selectable from the party
// list — Attack 1-8, Bind 1-3, Ignore 1-2, Square/Circle/Cross/Triangle).
// Index value is the slot in MarkingController._markers; do not renumber.
public enum Sign
{
    Attack1 = 0,
    Attack2 = 1,
    Attack3 = 2,
    Attack4 = 3,
    Attack5 = 4,
    Bind1 = 5,
    Bind2 = 6,
    Bind3 = 7,
    Ignore1 = 8,
    Ignore2 = 9,
    Square = 10,
    Circle = 11,
    Cross = 12,
    Triangle = 13,
    Attack6 = 14,
    Attack7 = 15,
    Attack8 = 16,
}
