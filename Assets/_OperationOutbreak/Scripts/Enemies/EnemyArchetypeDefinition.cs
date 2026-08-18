using System.Collections.Generic;
using UnityEngine;

namespace OperationOutbreak.Enemies
{
    /// <summary>
    /// Milestone 1S - the DATA-DRIVEN enemy variant definition.
    ///
    /// There remains exactly ONE reusable enemy gameplay framework
    /// (ZombieController + EnemyAnimationBridge + EnemyRagdoll on the shared
    /// Zombie_Prototype prefab). An archetype definition is PURE DATA: it
    /// describes how one variant of that framework is configured. No variant
    /// gets its own controller class, its own spawner or its own death system -
    /// adding a future variant means adding an asset, not code.
    ///
    /// OWNERSHIP (what each layer owns, and only that):
    ///   - EnemyArchetypeDefinition: the variant's authored numbers and
    ///     presentation references (identity, gameplay tuning, production
    ///     visual source, locomotion profile).
    ///   - ZombieController: the one gameplay authority. It READS the
    ///     definition once at spawn (ApplyArchetype) and keeps executing the
    ///     verified combat/movement logic.
    ///   - EnemyAnimationBridge: the one presentation bridge. It READS the
    ///     definition's locomotion profile at spawn (ApplyArchetype) - a
    ///     controller swap and a cadence reference. No "if runner play X"
    ///     branch exists anywhere in gameplay code.
    ///   - EnemyRagdoll + death timings: SHARED, prefab-owned. The death
    ///     lead-in, handoff, settle, collider lifecycle and reset/reuse are
    ///     identical for every production archetype; the definition only
    ///     declares whether the shared production presentation REQUIRES the
    ///     ragdoll (validated in the editor).
    ///
    /// RUNTIME LOADING: assets live under
    /// Assets/_OperationOutbreak/Resources/EnemyArchetypes so
    /// EnemyArchetypeRegistry can resolve them by id at runtime without a
    /// per-variant code path (Resources.LoadAll).
    /// </summary>
    [CreateAssetMenu(
        fileName = "EnemyArchetype_New",
        menuName = "Operation Outbreak/Enemy Archetype Definition")]
    public sealed class EnemyArchetypeDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("STABLE id referenced by spawn requests, e.g. 'basic_infected' or " +
                 "'runner'. Must be unique across all archetype assets; never change " +
                 "it once content references it.")]
        [SerializeField] private string archetypeId = string.Empty;

        [Tooltip("Human-readable debug/display name.")]
        [SerializeField] private string displayName = string.Empty;

        [Header("Gameplay (applied to the shared ZombieController at spawn)")]
        [Min(1)] [SerializeField] private int maxHealth = 3;
        [Min(0.1f)] [SerializeField] private float moveSpeed = 2.5f;
        [Min(1)] [SerializeField] private int attackDamage = 1;
        [Min(0.1f)] [SerializeField] private float attackInterval = 1f;
        [Min(0.1f)] [SerializeField] private float attackRange = 1.25f;
        [Min(0.1f)] [SerializeField] private float separationRadius = 1.1f;
        [Min(0f)] [SerializeField] private float separationStrength = 1.5f;

        [Header("Presentation")]
        [Tooltip("Path of the production visual source prefab for this variant. The " +
                 "visual itself is baked into the SHARED gameplay enemy prefab by the " +
                 "1Q setup tool; this path is the variant's declared visual source, " +
                 "validated in the editor and used by future variants.")]
        [SerializeField] private string productionPrefabPath = string.Empty;

        [Tooltip("Locomotion presentation profile: 'Walk' (Basic) or 'Run' (Runner). " +
                 "Determines which Mixamo clip the controller tool wires into the " +
                 "locomotion state - no gameplay code branches on this.")]
        [SerializeField] private string locomotionProfileName = "Walk";

        [Tooltip("Resources path of the RuntimeAnimatorController to swap onto the " +
                 "shared prefab's Animator at spawn (relative to a Resources folder, " +
                 "no extension). EMPTY means 'keep the shared prefab's authored " +
                 "controller' - the verified Basic Infected behaviour.")]
        [SerializeField] private string locomotionResourcesPath = string.Empty;

        [Tooltip("Locomotion cadence reference (u/s) written onto the bridge at spawn: " +
                 "the speed at which the locomotion clip's foot cadence matches world " +
                 "translation. Basic carries the VERIFIED prefab value; Runner carries " +
                 "the run clip's measured average speed (patched by the Runner " +
                 "controller tool).")]
        [Min(0.01f)] [SerializeField] private float locomotionReferenceSpeed = 1.3f;

        [Tooltip("True when this variant's production presentation requires the shared " +
                 "hybrid ragdoll death. Validated in the editor against the shared " +
                 "enemy prefab (a production archetype may not silently ship without " +
                 "the ragdoll it declares).")]
        [SerializeField] private bool requiresRagdoll = true;

        // Read-only views (authored data must not be writable at runtime).
        public string ArchetypeId => archetypeId;
        public string DisplayName => displayName;
        public int MaxHealth => maxHealth;
        public float MoveSpeed => moveSpeed;
        public int AttackDamage => attackDamage;
        public float AttackInterval => attackInterval;
        public float AttackRange => attackRange;
        public float SeparationRadius => separationRadius;
        public float SeparationStrength => separationStrength;
        public string ProductionPrefabPath => productionPrefabPath;
        public string LocomotionProfileName => locomotionProfileName;
        public string LocomotionResourcesPath => locomotionResourcesPath;
        public float LocomotionReferenceSpeed => locomotionReferenceSpeed;
        public bool RequiresRagdoll => requiresRagdoll;

        /// <summary>Well-known locomotion profiles shared with the controller tool.</summary>
        public const string WalkProfile = "Walk";
        public const string RunProfile = "Run";

        // Validation bounds (single source of truth for the editor validator and
        // the tests).
        public const float MinimumMoveSpeed = 0.1f;
        public const float MaximumMoveSpeed = 20f;
        public const int MinimumHealth = 1;
        public const int MinimumAttackDamage = 1;
        public const float MinimumAttackInterval = 0.1f;
        public const float MaximumAttackInterval = 60f;
        public const float MinimumAttackRange = 0.1f;
        public const float MaximumAttackRange = 10f;
        public const float MinimumSeparationRadius = 0f;
        public const float MaximumSeparationRadius = 5f;
        public const float MinimumSeparationStrength = 0f;
        public const float MaximumSeparationStrength = 50f;

        /// <summary>
        /// Pure definition-level validation (no AssetDatabase): returns every
        /// problem that makes this archetype unsafe to spawn. A broken archetype
        /// must fail clearly in editor/dev QA rather than silently spawning
        /// incorrectly. Static and side-effect free for EditMode tests.
        /// </summary>
        public static List<string> CollectDefinitionProblems(EnemyArchetypeDefinition definition)
        {
            var problems = new List<string>();

            if (definition == null)
            {
                problems.Add("Archetype definition is null.");
                return problems;
            }

            if (string.IsNullOrEmpty(definition.archetypeId))
            {
                problems.Add("Missing stable archetype id.");
            }

            if (string.IsNullOrEmpty(definition.displayName))
            {
                problems.Add("Missing display name.");
            }

            if (definition.maxHealth < MinimumHealth)
            {
                problems.Add("maxHealth must be >= " + MinimumHealth + ".");
            }

            if (definition.moveSpeed < MinimumMoveSpeed || definition.moveSpeed > MaximumMoveSpeed)
            {
                problems.Add("moveSpeed must be within [" + MinimumMoveSpeed + ", " +
                             MaximumMoveSpeed + "].");
            }

            if (definition.attackDamage < MinimumAttackDamage)
            {
                problems.Add("attackDamage must be >= " + MinimumAttackDamage + ".");
            }

            if (definition.attackInterval < MinimumAttackInterval ||
                definition.attackInterval > MaximumAttackInterval)
            {
                problems.Add("attackInterval must be within [" + MinimumAttackInterval +
                             ", " + MaximumAttackInterval + "].");
            }

            if (definition.attackRange < MinimumAttackRange ||
                definition.attackRange > MaximumAttackRange)
            {
                problems.Add("attackRange must be within [" + MinimumAttackRange + ", " +
                             MaximumAttackRange + "].");
            }

            if (definition.separationRadius < MinimumSeparationRadius ||
                definition.separationRadius > MaximumSeparationRadius)
            {
                problems.Add("separationRadius must be within [" + MinimumSeparationRadius +
                             ", " + MaximumSeparationRadius + "].");
            }

            if (definition.separationStrength < MinimumSeparationStrength ||
                definition.separationStrength > MaximumSeparationStrength)
            {
                problems.Add("separationStrength must be within [" + MinimumSeparationStrength +
                             ", " + MaximumSeparationStrength + "].");
            }

            if (string.IsNullOrEmpty(definition.locomotionProfileName))
            {
                problems.Add("Missing locomotion profile name (Walk or Run).");
            }
            else if (definition.locomotionProfileName != WalkProfile &&
                     definition.locomotionProfileName != RunProfile)
            {
                problems.Add("Unknown locomotion profile '" + definition.locomotionProfileName +
                             "' - expected '" + WalkProfile + "' or '" + RunProfile + "'.");
            }

            // A non-default locomotion profile MUST declare the controller that
            // presents it (the shared prefab only ships the Basic controller).
            if (definition.locomotionProfileName == RunProfile &&
                string.IsNullOrEmpty(definition.locomotionResourcesPath))
            {
                problems.Add("Profile '" + RunProfile + "' requires a locomotion " +
                             "controller (locomotionResourcesPath is empty).");
            }

            if (definition.locomotionReferenceSpeed <= 0f)
            {
                problems.Add("locomotionReferenceSpeed must be positive.");
            }

            return problems;
        }

        /// <summary>
        /// Pure duplicate detection: returns every stable id that appears more than
        /// once across the given definitions. Static and side-effect free for
        /// EditMode tests.
        /// </summary>
        public static List<string> FindDuplicateArchetypeIds(
            IReadOnlyList<EnemyArchetypeDefinition> definitions)
        {
            var seen = new HashSet<string>();
            var duplicates = new List<string>();

            if (definitions == null)
            {
                return duplicates;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                EnemyArchetypeDefinition definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }

                string id = definition.archetypeId;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (!seen.Add(id) && !duplicates.Contains(id))
                {
                    duplicates.Add(id);
                }
            }

            return duplicates;
        }
    }
}
