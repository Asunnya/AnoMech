using System.Collections.Generic;
using AnoMech.Scenarios.Dsr.P1Knights;
using AnoMech.Scenarios.M9s.Aetherletting;
using AnoMech.Scenarios.M9s.Coffinmaker;
using AnoMech.Scenarios.M9s.Deathmatch;
using AnoMech.Scenarios.M9s.Final;
using AnoMech.Scenarios.M9s.Flails;
using AnoMech.Scenarios.M9s.HellInACell;
using AnoMech.Scenarios.M9s.VampStomp;
using AnoMech.Scenarios.M9s.VampStomp2;
using AnoMech.Scenarios.Top.P2PartySynergy;
using AnoMech.Scenarios.Top.P5Delta;
using AnoMech.Scenarios.Top.P5Omega;
using AnoMech.Scenarios.Top.P5Sigma;
using AnoMech.Scenarios.Top.P6WaveCannon2;
using AnoMech.Scenarios.Ucob.P5Exaflares;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P1TeleTrouncing;
using AnoMech.Scenarios.Umad.P2Forsaken;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3LimitCut;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using AnoMech.Scenarios.Umad.P5Celestriad;
using AnoMech.Scenarios.Umad.P5Exaflares;
using AnoMech.Scenarios.Umad.P5Flood;
using AnoMech.Scenarios.Uwu.P1Garuda;
using AnoMech.Scenarios.Uwu.P2Ifrit;
using AnoMech.Scenarios.Uwu.P3Titan;
using AnoMech.Scenarios.Uwu.UltimateAnnihilation;
using AnoMech.Scenarios.Uwu.UltimatePredation;
using AnoMech.Scenarios.Uwu.UltimateSuppression;

namespace AnoMech.Scenarios;

// Every runnable scenario, in menu order. Fresh instances per call: a scenario holds its run state.
public static class ScenarioCatalog
{
    public static IReadOnlyList<IScenario> Create() =>
    [
        new UmadP1TeleTrouncingScenario(),
        new UmadP2ForsakenScenario(),
        new UmadP3LimitCutScenario(),
        new UmadP3BlackHoleScenario(),
        new UmadP4KefkaSaysScenario(),
        new UmadP5FloodScenario(),
        new UmadP5ExaflaresScenario(),
        new UmadP5CelestriadScenario(),
        new UmadP5ForsakenNull(),
        new TopP2PartySynergyScenario(),
        new TopP5DeltaScenario(),
        new TopP5SigmaScenario(),
        new TopP5OmegaScenario(),
        new TopP6WaveCannon2Scenario(),
        new UwuP1GarudaScenario(),
        new UwuP2IfritScenario(),
        new UwuP3TitanScenario(),
        new UltimatePredationScenario(),
        new UltimateAnnihilationScenario(),
        new UltimateSuppressionScenario(),
        new UcobP5ExaflaresScenario(),
        new DsrP1KnightsScenario(),
        new M9sVampStompScenario(),
        new M9sCoffinmakerScenario(),
        new M9sAetherlettingScenario(),
        new M9sVampStomp2Scenario(),
        new M9sFlailsScenario(),
        new M9sHellInACellScenario(),
        new M9sDeathmatchScenario(),
        new M9sFinalScenario(),
    ];
}
