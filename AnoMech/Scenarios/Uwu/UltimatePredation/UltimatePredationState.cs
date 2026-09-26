using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using static AnoMech.Scenarios.Uwu.UwuConstants;

namespace AnoMech.Scenarios.Uwu.UltimatePredation;

public class UltimatePredationState
{
    public Placement UltimaPlacement { get; init; }
    public Placement GarudaPlacement { get; init; }
    public Placement IfritPlacement { get; init; }
    public Placement TitanPlacement { get; init; }
    public UltimatePredationScenarioObjects ScenarioObjects { get; } = new();

    public readonly Rng Rng = new();

    // Unlike other scenarios' State, UltimatePredationAi.Run makes live RNG tie-break draws
    // instead of resolving randomness once in the constructor. A peer replaying with a fresh
    // Rng could independently land on a different-but-valid safe spot than the host's bots,
    // visibly splitting the party on an AiMove.All(...) call. Fixed by having GetSafeCardinal/
    // GetSafeForFirstSet/GetSafeForSecondSet write their choice here on the real run, and read
    // it back instead of rolling once set -- same "broadcast resolved values" idiom as every
    // other FromNetworkReplay.
    public Placement? ResolvedSafeCardinal;
    public Placement? ResolvedSafeFirstSet;
    public Placement? ResolvedSafeSecondSet;

    public UltimatePredationState(UltimatePredationStateOverrides overrides)
    {
        var centerDodge = overrides.CenterDodge ?? false;

        (GarudaPlacement, var garudaIntercardinal) = GetPlacement(Geometry.GarudaPlacements);
        (UltimaPlacement, var ultimaIntercardinal) = GetPlacement(Geometry.UltimaPlacements, centerDodge ? [garudaIntercardinal] : null);
        (IfritPlacement, _) = GetPlacement(Geometry.IfritPlacements, [ultimaIntercardinal]);
        TitanPlacement = GetTitanPlacement(Geometry.TitanPlacements, centerDodge);

#if DEBUG
        Plugin.Log.Debug("[UltimatePredationState]\n" +
            $"UltimaPlacement = {UltimaPlacement.Position2}\n" +
            $"GarudaPlacement = {GarudaPlacement.Position2}\n" +
            $"IfritPlacement = {IfritPlacement.Position2}\n" +
            $"TitanPlacement = {TitanPlacement.Position2}");
#endif
    }

    // Network-replay constructor: reconstructs only the placements UltimatePredationAi reads,
    // skipping the RNG-driven resolution body. ScenarioObjects.Titan is set separately once
    // peerEnemies has it (mirrors UmadP3BlackHoleState's Chaos/Exdeath).
    private UltimatePredationState() { }

    public static UltimatePredationState FromNetworkReplay(
        Placement garudaPlacement, Placement titanPlacement, Placement ifritPlacement, Placement ultimaPlacement)
        => new()
        {
            GarudaPlacement = garudaPlacement,
            TitanPlacement = titanPlacement,
            IfritPlacement = ifritPlacement,
            UltimaPlacement = ultimaPlacement,
        };

    private (Placement Placement, DirectionEnum Intercardinal) GetPlacement(IDictionary<DirectionEnum, Placement> possiblePlacements, List<DirectionEnum>? ignoreDirections = null)
    {
        IDictionary<DirectionEnum, Placement> dict;

        if (ignoreDirections == null)
        {
            dict = possiblePlacements;
        }
        else
        {
            dict = new Dictionary<DirectionEnum, Placement>(possiblePlacements);
            ignoreDirections.ForEach(x => dict.Remove(x));
        }

        var key = Rng.NextObj(dict.Keys.ToArray());
        return (dict[key], key);
    }

    private Placement GetTitanPlacement(IDictionary<DirectionEnum, Placement> possiblePlacements, bool centerDodge)
    {
        const float Offset = 3;

        if (!centerDodge)
        {
            (var placement, _) = GetPlacement(possiblePlacements);

            var distance = Rng.NextBool() ? Offset : -Offset;
            return placement.MoveRight(distance);
        }
        else
        {
            List<DirectionEnum> ignore;
            var ignoreClosest = Rng.NextBool();

            if (ignoreClosest)
            {
                ignore = possiblePlacements
                    .OrderBy(x => Vector2.DistanceSquared(x.Value.Position2, GarudaPlacement.Position2))
                    .Take(2)
                    .Select(x => x.Key)
                    .ToList();
            }
            else
            {
                ignore = possiblePlacements
                    .OrderByDescending(x => Vector2.DistanceSquared(x.Value.Position2, GarudaPlacement.Position2))
                    .Take(2)
                    .Select(x => x.Key)
                    .ToList();
            }

            (var cardinal, _) = GetPlacement(possiblePlacements, ignore);

            var offsets = new Placement[]
            {
                cardinal.MoveRight(-Offset),
                cardinal.MoveRight(Offset)
            };

            if (ignoreClosest)
            {
                return offsets
                    .OrderBy(x => Vector2.DistanceSquared(x.Position2, GarudaPlacement.Position2))
                    .First();
            }
            else
            {
                return offsets
                    .OrderByDescending(x => Vector2.DistanceSquared(x.Position2, GarudaPlacement.Position2))
                    .First();
            }
        }
    }
}
