using AnoMech.Core.SimObjects;
using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Core.Game.Party;

// Spawns and configures the eight party members around the scenario origin.
// Reads PartyPresets for the player's job (the player's own slot is null in
// the preset list — that role gets the SimPlayer reference instead), builds
// each non-player BattleChara as a Lalafell PC, and stores the resulting
// SimPartyNpc (or SimPlayer) into the supplied SimParty. Doppels are
// inserted into CharacterManager._battleCharas so row-click targeting and
// mouseover tooltips resolve through the engine's normal lookup path; the
// matching unregister lives in SimPartyNpc.Despawn. Scenarios are
// inn-gated upstream (Game.RunScenarioInternal). Game is the entry point —
// it computes the origin and delegates here.
internal static class PartyCreator
{
    private const float RingRadius = 2.5f;
    private const float RadiusJitter = 0.6f;
    private const float AngleJitter = 0.4f;

    // networkRoles: slots held by other real participants, spawned as SimNetworkPuppet (position
    // from the network, not AiManager); takes priority over `solo`. networkSeats: their lobby
    // name and job for the nameplate and party list, each falling back to the role preset's.
    public static void Populate(SimParty party, SimPlayer player, uint playerJob, SimWorld world, PartyRole? roleOverride = null, bool solo = false, IReadOnlySet<PartyRole>? networkRoles = null, IReadOnlyDictionary<PartyRole, NetworkSeat>? networkSeats = null)
    {
        var presets = roleOverride is { } skip
            ? PartyPresets.ForRole(skip)
            : PartyPresets.ForPlayerJob(playerJob);
        var spawnRng = world.Stream("party-spawn");

        for (int i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            var role = (PartyRole)i;
            if (preset == null)
            {
                // Player's own job slot — wire the SimPlayer in directly so
                // Party.Get(role) returns a uniform SimCharacter.
                party.SetSlot(role, player);
                // Steered around only under DebugBotControl (PlayerMovement).
                player.Obstacles = world.Obstacles;
                continue;
            }

            if (networkRoles != null && networkRoles.Contains(role))
            {
                var angle0 = (i / (float)presets.Count) * MathF.Tau;
                var localPos0 = new Vector3(MathF.Sin(angle0) * RingRadius, 0f, MathF.Cos(angle0) * RingRadius);
                var puppetPreset = networkSeats?.GetValueOrDefault(role) is { } seat
                    ? preset with
                    {
                        Name = seat.Name.Length > 0 ? seat.Name : preset.Name,
                        ClassJob = seat.ClassJob != 0 ? seat.ClassJob : preset.ClassJob,
                    }
                    : preset;
                var puppet = SpawnPuppet(puppetPreset, world, role, new Placement(localPos0, MathF.Atan2(-localPos0.X, -localPos0.Z)));
                if (puppet != null) party.SetSlot(role, puppet);
                continue;
            }

            // Solo mode: only the player's own slot (and any network puppets) is filled.
            if (solo) continue;

            var angle = (i / (float)presets.Count) * MathF.Tau
                        + ((float)spawnRng.NextDouble() - 0.5f) * AngleJitter;
            var distance = RingRadius + ((float)spawnRng.NextDouble() - 0.5f) * RadiusJitter;
            // Local ring around the scenario origin. Y stays at 0 (local floor);
            // Coordinates.ToGlobal lifts it to origin.Y at spawn.
            var localPos = new Vector3(MathF.Sin(angle) * distance, 0f, MathF.Cos(angle) * distance);
            var facingPlayer = MathF.Atan2(-localPos.X, -localPos.Z);

            var member = Spawn(preset, world, role, new Placement(localPos, facingPlayer));
            if (member != null) party.SetSlot(role, member);
        }
    }

    private static SimPartyNpc? Spawn(PartyMemberPreset preset, SimWorld world, PartyRole role, Placement placement)
    {
        if (Natives.BattleCharas.SpawnDoppel(preset, world.Coordinates.ToGlobal(placement)) is not { } chara) return null;

        Plugin.Log.Info($"PartyCreator: spawned {preset.Name} ({role}, job {preset.ClassJob}) at index {chara.Slot}");
        var member = new SimPartyNpc(chara, world.Coordinates, role, preset.ClassJob, preset.Name);
        // Bots steer around the scenario's geometry; only doppels get the live
        // field (bosses/puppets keep ObstacleField.Empty and move in straight lines).
        member.Obstacles = world.Obstacles;
        // Seed the stored Position/Rotation to match the spawn placement so
        // anything reading SimCharacter.Position before the first Tick sees
        // the correct value (the Tick re-sync only kicks in next frame).
        member.SetPosition(placement);
        return member;
    }

    // Same visuals as a bot, but excluded from the Obstacles field only steering doppels need.
    private static SimNetworkPuppet? SpawnPuppet(PartyMemberPreset preset, SimWorld world, PartyRole role, Placement placement)
    {
        if (Natives.BattleCharas.SpawnDoppel(preset, world.Coordinates.ToGlobal(placement)) is not { } chara) return null;

        Plugin.Log.Info($"PartyCreator: spawned network puppet {preset.Name} ({role}, job {preset.ClassJob}) at index {chara.Slot}");
        var puppet = new SimNetworkPuppet(chara, world.Coordinates, role, preset.ClassJob, preset.Name);
        puppet.SetPosition(placement);
        return puppet;
    }
}
