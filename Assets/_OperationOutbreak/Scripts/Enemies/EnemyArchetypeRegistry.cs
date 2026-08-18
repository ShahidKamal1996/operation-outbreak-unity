using System.Collections.Generic;
using UnityEngine;

namespace OperationOutbreak.Enemies
{
    /// <summary>
    /// Milestone 1S - runtime archetype registry.
    ///
    /// Resolves stable archetype ids to their data-driven definitions. The
    /// definitions live under Assets/_OperationOutbreak/Resources/EnemyArchetypes
    /// and are loaded with Resources.LoadAll, so builds resolve them without
    /// scene wiring. Duplicate stable ids are detected and reported as errors
    /// (first one wins), and any unknown or empty spawn request resolves to the
    /// DEFAULT archetype ("basic_infected") - which preserves the verified
    /// Basic Infected behaviour for every caller that asks for nothing.
    ///
    /// There is no per-variant code path here and none downstream: this class
    /// only maps ids to data.
    /// </summary>
    public static class EnemyArchetypeRegistry
    {
        /// <summary>The stable id of the verified Basic Infected - the default for
        /// every spawn request that does not name an archetype.</summary>
        public const string DefaultArchetypeId = "basic_infected";

        /// <summary>Resources folder (relative to any Resources directory) holding
        /// the archetype definition assets.</summary>
        public const string ResourcesFolder = "EnemyArchetypes";

        private static readonly Dictionary<string, EnemyArchetypeDefinition> ById =
            new Dictionary<string, EnemyArchetypeDefinition>();

        private static readonly List<EnemyArchetypeDefinition> All =
            new List<EnemyArchetypeDefinition>();

        private static bool _initialized;

        /// <summary>
        /// Loads every archetype definition from the Resources folder and indexes
        /// it by stable id. Duplicate ids are rejected with a clear error (the
        /// first asset wins). Idempotent; the tests call Initialize explicitly so
        /// a stale editor cache can never hide a problem.
        /// </summary>
        public static void Initialize()
        {
            ById.Clear();
            All.Clear();

            EnemyArchetypeDefinition[] loaded =
                Resources.LoadAll<EnemyArchetypeDefinition>(ResourcesFolder);

            for (int i = 0; i < loaded.Length; i++)
            {
                EnemyArchetypeDefinition definition = loaded[i];

                if (definition == null)
                {
                    continue;
                }

                All.Add(definition);

                if (string.IsNullOrEmpty(definition.ArchetypeId))
                {
                    Debug.LogError(
                        "[1S] Archetype asset '" + definition.name + "' has no stable id - " +
                        "it can never be requested by id. Fix or delete the asset.", definition);
                    continue;
                }

                if (ById.ContainsKey(definition.ArchetypeId))
                {
                    Debug.LogError(
                        "[1S] DUPLICATE archetype id '" + definition.ArchetypeId + "': asset '" +
                        definition.name + "' collides with '" +
                        ById[definition.ArchetypeId].name + "'. The first asset wins - fix the " +
                        "duplicate.", definition);
                    continue;
                }

                ById.Add(definition.ArchetypeId, definition);
            }

            _initialized = true;
        }

        /// <summary>Every loaded archetype definition (for validation and tools).</summary>
        public static IReadOnlyList<EnemyArchetypeDefinition> AllArchetypes
        {
            get
            {
                EnsureInitialized();
                return All;
            }
        }

        /// <summary>True when a definition with the given stable id exists.</summary>
        public static bool TryGetArchetype(string archetypeId, out EnemyArchetypeDefinition definition)
        {
            EnsureInitialized();

            if (!string.IsNullOrEmpty(archetypeId) && ById.TryGetValue(archetypeId, out definition))
            {
                return true;
            }

            definition = null;
            return false;
        }

        /// <summary>
        /// Resolves a spawn request to a definition: an explicit known id returns
        /// its definition; a null/empty request returns the DEFAULT (verified
        /// Basic Infected); an unknown id logs a clear error and still falls back
        /// to the default, so a typo degrades to the original enemy rather than
        /// silently spawning nothing.
        /// </summary>
        public static EnemyArchetypeDefinition ResolveRequestedArchetype(string archetypeId)
        {
            EnsureInitialized();

            if (string.IsNullOrEmpty(archetypeId))
            {
                return ResolveDefault();
            }

            if (ById.TryGetValue(archetypeId, out EnemyArchetypeDefinition definition))
            {
                return definition;
            }

            Debug.LogError(
                "[1S] Unknown archetype id '" + archetypeId + "' - falling back to the " +
                "default ('" + DefaultArchetypeId + "'). Check the spawn request.");

            return ResolveDefault();
        }

        /// <summary>The verified Basic Infected definition, or null when the
        /// archetype assets are missing from the project.</summary>
        public static EnemyArchetypeDefinition ResolveDefault()
        {
            EnsureInitialized();
            ById.TryGetValue(DefaultArchetypeId, out EnemyArchetypeDefinition definition);
            return definition;
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                Initialize();
            }
        }
    }
}
