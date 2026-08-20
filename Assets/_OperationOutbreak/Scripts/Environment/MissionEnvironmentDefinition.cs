using System.Collections.Generic;
using UnityEngine;

namespace OperationOutbreak.Environment
{
    /// <summary>
    /// Milestone 1W - the data-driven Chapter 1 environment profile.
    ///
    /// A profile is PURE, STATIC configuration: it describes/references the reusable
    /// environment family (road/ground materials, barrier material, road markings,
    /// roadside dressing, the three landmark prefabs and the dressing prefab library)
    /// plus a deterministic seed for authored assembly. It contains NO gameplay state,
    /// NO runtime logic, NO colliders, NO objective/reward/result data - those belong to
    /// their own systems.
    ///
    /// MissionDefinition references one profile (optional serialized reference) so a
    /// mission's presentation is data-configured rather than hard-coded. The committed
    /// Mission 01 uses the "c1_outbreak_outskirts" profile.
    ///
    /// Authoring workflow: Assets > Create > Operation Outbreak > Environment Profile,
    /// then assign the profile to the mission. No C# required for a normal environment.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MissionEnvironment_New",
        menuName = "Operation Outbreak/Environment Profile")]
    public sealed class MissionEnvironmentDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("STABLE environment family id, e.g. 'c1_outbreak_outskirts'. Unique across profiles.")]
        [SerializeField] private string environmentId = string.Empty;

        [Tooltip("Human-readable debug/display name.")]
        [SerializeField] private string displayName = string.Empty;

        [Header("Materials (shared, batching-friendly)")]
        [Tooltip("Road/ground material (the authored road treatment).")]
        [SerializeField] private Material roadMaterial;

        [Tooltip("Concrete barrier material (roadside edges + checkpoints).")]
        [SerializeField] private Material barrierMaterial;

        [Tooltip("Road marking material (worn lane stripes + hazard accents).")]
        [SerializeField] private Material roadMarkingMaterial;

        [Tooltip("Roadside verge/ground material (outside the playable lane).")]
        [SerializeField] private Material roadsideMaterial;

        [Tooltip("Quarantine accent material (checkpoint orange-red).")]
        [SerializeField] private Material accentMaterial;

        [Header("Landmarks (section-transition + finale dressing)")]
        [Tooltip("Start checkpoint landmark (identifiable mission start).")]
        [SerializeField] private GameObject startLandmarkPrefab;

        [Tooltip("Section-transition landmark (flanks each later section activation line).")]
        [SerializeField] private GameObject transitionLandmarkPrefab;

        [Tooltip("Final encounter landmark (roadblock backdrop at the corridor end).")]
        [SerializeField] private GameObject finalLandmarkPrefab;

        [Header("Dressing library (reusable kit modules)")]
        [Tooltip("Reusable side-dressing prefabs (barriers, debris, crates, cones).")]
        [SerializeField] private List<GameObject> sideDressingPrefabs = new List<GameObject>();

        [Header("Deterministic authoring")]
        [Tooltip("Seed for the deterministic environment assembly plan (never changes layout randomly).")]
        [SerializeField] private int deterministicSeed = 1;

        // ------------------------------------------------------------------ read-only views

        public string EnvironmentId => environmentId;
        public string DisplayName => displayName;
        public Material RoadMaterial => roadMaterial;
        public Material BarrierMaterial => barrierMaterial;
        public Material RoadMarkingMaterial => roadMarkingMaterial;
        public Material RoadsideMaterial => roadsideMaterial;
        public Material AccentMaterial => accentMaterial;
        public GameObject StartLandmarkPrefab => startLandmarkPrefab;
        public GameObject TransitionLandmarkPrefab => transitionLandmarkPrefab;
        public GameObject FinalLandmarkPrefab => finalLandmarkPrefab;
        public IReadOnlyList<GameObject> SideDressingPrefabs => sideDressingPrefabs;
        public int DeterministicSeed => deterministicSeed;

        /// <summary>
        /// Pure, side-effect-free validation: returns every problem that makes this
        /// environment profile unusable (missing id, missing required material or
        /// landmark, null dressing entries). Broken environment data must be reported
        /// loudly, never silently repaired. Static and testable without a scene.
        /// </summary>
        public static List<string> CollectProblems(MissionEnvironmentDefinition profile)
        {
            List<string> problems = new List<string>();

            if (profile == null)
            {
                problems.Add("Environment profile is null.");
                return problems;
            }

            string label = !string.IsNullOrEmpty(profile.displayName)
                ? profile.displayName
                : (!string.IsNullOrEmpty(profile.environmentId) ? profile.environmentId : profile.name);

            if (string.IsNullOrEmpty(profile.environmentId))
            {
                problems.Add(label + ": missing stable environment id.");
            }

            if (profile.roadMaterial == null)
            {
                problems.Add(label + ": missing road material.");
            }

            if (profile.barrierMaterial == null)
            {
                problems.Add(label + ": missing barrier material.");
            }

            if (profile.roadMarkingMaterial == null)
            {
                problems.Add(label + ": missing road marking material.");
            }

            if (profile.roadsideMaterial == null)
            {
                problems.Add(label + ": missing roadside material.");
            }

            if (profile.accentMaterial == null)
            {
                problems.Add(label + ": missing accent material.");
            }

            if (profile.startLandmarkPrefab == null)
            {
                problems.Add(label + ": missing start landmark prefab.");
            }

            if (profile.transitionLandmarkPrefab == null)
            {
                problems.Add(label + ": missing section-transition landmark prefab.");
            }

            if (profile.finalLandmarkPrefab == null)
            {
                problems.Add(label + ": missing final landmark prefab.");
            }

            if (profile.sideDressingPrefabs == null)
            {
                problems.Add(label + ": side dressing prefab list is null.");
            }
            else
            {
                for (int i = 0; i < profile.sideDressingPrefabs.Count; i++)
                {
                    if (profile.sideDressingPrefabs[i] == null)
                    {
                        problems.Add(label + ": side dressing entry " + (i + 1) + " is null.");
                    }
                }
            }

            return problems;
        }
    }
}
