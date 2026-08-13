using System;
using UnityEngine;

namespace OperationOutbreak.Enemies
{
    /// <summary>
    /// Milestone 1N - well-known archetype identifiers.
    ///
    /// These are compile-time constants, not mutable state: nothing here is ever written
    /// at runtime, so the "no static/global run state" rule is respected. Ids are plain
    /// strings so a section can be authored in the Inspector without a code change, and
    /// so a future archetype (TANK, RANGED, EXPLODER, ELITE) only needs a new prefab plus
    /// a new library entry - no edits to the mission controller or the spawner.
    /// </summary>
    public static class EnemyArchetypeId
    {
        /// <summary>The original prototype zombie. Unchanged by Milestone 1N.</summary>
        public const string Basic = "BASIC";

        /// <summary>Milestone 1N - faster, frailer pursuer.</summary>
        public const string Runner = "RUNNER";
    }

    /// <summary>
    /// Milestone 1N - binds an archetype id to the prefab that represents it.
    ///
    /// Deliberately thin. An archetype is NOT a behaviour class: every enemy type in the
    /// prototype runs the same <see cref="ZombieController"/> and therefore shares the
    /// existing chase, separation, contact-attack, damage and death code. An archetype is
    /// only "which prefab, under which name", so adding a type can never fork combat
    /// behaviour into a parallel system.
    /// </summary>
    [Serializable]
    public sealed class EnemyArchetype
    {
        [Tooltip("Identifier referenced by a section's composition, e.g. \"BASIC\" or \"RUNNER\".")]
        public string id = EnemyArchetypeId.Basic;

        [Tooltip("Prefab spawned for this archetype. Must carry a ZombieController.")]
        public ZombieController prefab;

        /// <summary>
        /// Milestone 1N.1 - how many world units CLOSER to the player this archetype enters
        /// the fight, measured from the section's authored spawn band.
        ///
        /// Zero (the default) reproduces the pre-1N.1 behaviour exactly, which is why the
        /// BASIC entry keeps spawning byte-identically without needing a special case. A
        /// positive value is a pressure dial: a fast, frail archetype can be given a head
        /// start on the approach so it reaches the player before the auto-fire deletes it,
        /// without touching the weapon or the archetype's stats.
        ///
        /// The spawner clamps this against the player's position and the lane bounds, so an
        /// over-large value can never place an enemy on top of or behind the player.
        /// </summary>
        [Tooltip("World units closer to the player than the section's spawn band. " +
                 "0 = spawn on the band exactly like the basic zombie.")]
        [Min(0f)] public float spawnDistanceOffset;

        /// <summary>
        /// Milestone 1N.2 - per-archetype override of the spawner's minimum spawn standoff.
        ///
        /// The global standoff is tuned for the BASIC zombie: it is slow, so it needs a wide
        /// approach corridor for the encounter to read fairly. Applying that same corridor to
        /// every archetype silently cancelled the RUNNER's <see cref="spawnDistanceOffset"/>,
        /// because the authored bands sit at most about seven units ahead of the player's
        /// forward limit while the global standoff demands twelve. The clamp therefore always
        /// won and the offset never had any effect in play.
        ///
        /// A NEGATIVE value (the default) means "inherit the spawner's global standoff", so an
        /// archetype that says nothing behaves exactly as it did before this milestone. This is
        /// what keeps BASIC byte-identical without a type check anywhere in the spawn path.
        ///
        /// The override only relaxes how close an archetype MAY start; every other safety rule
        /// still applies afterwards, so a smaller value can never put an enemy on top of, or
        /// behind, the player, and can never skip the overlap nudge.
        /// </summary>
        [Tooltip("Closest this archetype may ever spawn to the player, in world units. " +
                 "Negative = inherit the spawner's global minimum standoff.")]
        public float minimumSpawnStandoffOverride = -1f;

        /// <summary>True when this archetype defines its own standoff instead of inheriting.</summary>
        public bool HasStandoffOverride => minimumSpawnStandoffOverride >= 0f;

        /// <summary>
        /// The standoff this archetype should actually be spawned with, given the spawner's
        /// global default. Pure function, no side effects, so it is directly unit-testable.
        /// </summary>
        public float ResolveMinimumStandoff(float globalStandoff)
        {
            return HasStandoffOverride ? minimumSpawnStandoffOverride : globalStandoff;
        }
    }

    /// <summary>
    /// Milestone 1N - one line of a section's enemy composition: "how many of which type".
    ///
    /// A section's composition is a list of these, which is what makes encounter make-up
    /// data-driven instead of hard-coded. There is no "if section == 2 spawn a Runner"
    /// branch anywhere in the codebase.
    /// </summary>
    [Serializable]
    public sealed class EnemySpawnEntry
    {
        [Tooltip("Archetype id to spawn. Falls back to the spawner's basic prefab if unknown.")]
        public string archetypeId = EnemyArchetypeId.Basic;

        [Tooltip("How many of this archetype this section spawns.")]
        [Min(0)] public int count = 1;

        public EnemySpawnEntry() { }

        public EnemySpawnEntry(string archetypeId, int count)
        {
            this.archetypeId = archetypeId;
            this.count = count;
        }
    }

    /// <summary>
    /// Milestone 1N.2 - the pure geometry of an archetype spawn offset, split out of the
    /// spawner so it can be unit tested directly instead of being re-implemented inside a
    /// test. The spawner calls this and then applies the overlap nudge on top of the result;
    /// keeping the two apart means the safety rules have exactly one definition.
    /// </summary>
    public static class EnemySpawnMath
    {
        /// <summary>
        /// Returns the z the enemy should spawn at when pulled <paramref name="offset"/> units
        /// toward the player from <paramref name="bandZ"/>.
        ///
        /// Invariants, all of which hold for any input:
        ///  * never nearer the player than <paramref name="standoff"/>;
        ///  * never further out than the authored band, so the offset only ever pulls inward
        ///    and can never push an enemy away from the fight;
        ///  * never behind the player, because the floor is measured forward from the player.
        /// When the player has already advanced past the safety line the offset is applied
        /// partially, or not at all, rather than being forced.
        /// </summary>
        public static float ClampForwardOffset(float bandZ, float playerZ, float offset, float standoff)
        {
            float nearestAllowedZ = playerZ + standoff;
            float desiredZ = bandZ - offset;

            // Never past the standoff...
            float clampedZ = Mathf.Max(desiredZ, nearestAllowedZ);

            // ...and never further out than the band we started from.
            return Mathf.Min(clampedZ, bandZ);
        }
    }

    /// <summary>
    /// Milestone 1O-R - read-only description of a single spawn, handed to diagnostics
    /// listeners. It exists so an observer can tell the difference between "no spawn offset
    /// was configured" and "an offset was configured but the safety clamps removed it".
    ///
    /// Purely informational: the enemy has already been placed by the time this is built,
    /// and nothing in the spawn path reads it back.
    /// </summary>
    public readonly struct EnemySpawnReport
    {
        /// <summary>Archetype id that produced the enemy (BASIC / RUNNER).</summary>
        public readonly string ArchetypeId;

        /// <summary>Mission section index, or -1 for the legacy non-mission waves.</summary>
        public readonly int SectionIndex;

        /// <summary>Where the enemy actually ended up.</summary>
        public readonly Vector3 FinalPosition;

        /// <summary>The authored band slot before any archetype offset was considered.</summary>
        public readonly Vector3 BandPosition;

        /// <summary>The archetype's configured spawnDistanceOffset (what was asked for).</summary>
        public readonly float RequestedOffset;

        /// <summary>
        /// Milestone 1N.2 - the minimum standoff actually in force for this spawn, after any
        /// per-archetype override. Reported so a clamped offset can be explained rather than
        /// merely observed.
        /// </summary>
        public readonly float StandoffUsed;

        /// <summary>How much of the requested offset survived the standoff/nudge clamps.</summary>
        public float AppliedOffset => BandPosition.z - FinalPosition.z;

        public EnemySpawnReport(
            string archetypeId,
            int sectionIndex,
            Vector3 finalPosition,
            Vector3 bandPosition,
            float requestedOffset,
            float standoffUsed)
        {
            ArchetypeId = archetypeId;
            SectionIndex = sectionIndex;
            FinalPosition = finalPosition;
            BandPosition = bandPosition;
            RequestedOffset = requestedOffset;
            StandoffUsed = standoffUsed;
        }
    }
}
