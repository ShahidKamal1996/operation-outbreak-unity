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
}
