#if UNITY_EDITOR
using System.Collections.Generic;
using OperationOutbreak.Enemies;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1S - editor validation and debug tooling for the data-driven
    /// enemy archetype system.
    ///
    ///   Tools > Operation Outbreak > Validate Enemy Archetypes
    ///       Runs the FULL validation across every archetype asset: definition
    ///       problems (missing id, invalid ranges, missing locomotion setup),
    ///       duplicate stable ids, missing production prefabs, missing ragdoll
    ///       on the shared enemy prefab (when the archetype requires it) and
    ///       missing locomotion controllers. Broken archetypes fail LOUDLY here
    ///       instead of silently spawning incorrectly.
    ///
    ///   Tools > Operation Outbreak > Spawn Basic Infected (Debug)
    ///   Tools > Operation Outbreak > Spawn Runner (Debug)
    ///       Play-Mode debug spawns through the SHARED spawner seam (the
    ///       archetype definition is resolved by stable id and applied to the
    ///       shared gameplay prefab), proving one framework serves every
    ///       variant. Falls back to a direct instantiation when the scene has
    ///       no spawner (that path is NOT mission-tracked).
    /// </summary>
    public static class EnemyArchetypeEditorTools
    {
        [MenuItem("Tools/Operation Outbreak/Validate Enemy Archetypes")]
        public static void ValidateAllArchetypes()
        {
            bool valid = ValidateAllArchetypes(out List<string> problems);

            if (valid)
            {
                Debug.Log("[1S] Enemy archetype validation PASSED: every archetype has a " +
                          "unique stable id, valid gameplay ranges, a valid production " +
                          "prefab, valid locomotion setup and (where required) the shared " +
                          "ragdoll is configured.");
                EditorUtility.DisplayDialog(
                    "Enemy Archetypes",
                    "All enemy archetypes are valid.",
                    "OK");
                return;
            }

            foreach (string problem in problems)
            {
                Debug.LogWarning("[1S] " + problem);
            }

            EditorUtility.DisplayDialog(
                "Enemy Archetypes",
                problems.Count + " archetype problem(s) found - see the console. " +
                "Broken archetypes must be fixed before dev QA.",
                "OK");
        }

        /// <summary>
        /// The complete archetype validation. Pure checks (definition fields,
        /// duplicates) are static; asset-level checks (prefab/controller/ragdoll
        /// presence) resolve through AssetDatabase. Returns true when zero
        /// problems were found.
        /// </summary>
        public static bool ValidateAllArchetypes(out List<string> problems)
        {
            problems = new List<string>();

            var archetypes = LoadAllArchetypeDefinitions();

            if (archetypes.Count == 0)
            {
                problems.Add("No EnemyArchetypeDefinition assets found under " +
                             "Assets/_OperationOutbreak/Resources/EnemyArchetypes - the " +
                             "registry can never resolve a spawn request.");
            }

            foreach (EnemyArchetypeDefinition archetype in archetypes)
            {
                List<string> definitionProblems =
                    EnemyArchetypeDefinition.CollectDefinitionProblems(archetype);

                foreach (string problem in definitionProblems)
                {
                    problems.Add(archetype.name + ": " + problem);
                }
            }

            List<string> duplicates = EnemyArchetypeDefinition.FindDuplicateArchetypeIds(archetypes);
            foreach (string duplicate in duplicates)
            {
                problems.Add("DUPLICATE stable archetype id '" + duplicate + "' - every id " +
                             "must be unique.");
            }

            foreach (EnemyArchetypeDefinition archetype in archetypes)
            {
                ValidateProductionPresentation(archetype, problems);
            }

            return problems.Count == 0;
        }

        /// <summary>Loads every archetype definition asset in the project (editor).</summary>
        public static List<EnemyArchetypeDefinition> LoadAllArchetypeDefinitions()
        {
            var archetypes = new List<EnemyArchetypeDefinition>();
            string[] guids = AssetDatabase.FindAssets("t:EnemyArchetypeDefinition");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                EnemyArchetypeDefinition archetype =
                    AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(path);

                if (archetype != null)
                {
                    archetypes.Add(archetype);
                }
            }

            return archetypes;
        }

        private static void ValidateProductionPresentation(
            EnemyArchetypeDefinition archetype, List<string> problems)
        {
            // Production visual source must resolve.
            if (string.IsNullOrEmpty(archetype.ProductionPrefabPath))
            {
                problems.Add(archetype.name + ": missing production prefab path.");
            }
            else if (AssetDatabase.LoadAssetAtPath<GameObject>(archetype.ProductionPrefabPath) == null)
            {
                problems.Add(archetype.name + ": production prefab missing at '" +
                             archetype.ProductionPrefabPath + "'.");
            }

            // A production archetype that requires the hybrid ragdoll must find
            // it configured on the SHARED enemy prefab.
            if (archetype.RequiresRagdoll)
            {
                ValidateSharedPrefabRagdoll(archetype, problems);
            }

            // A variant with its own locomotion controller must find the asset.
            if (archetype.LocomotionProfileName == EnemyArchetypeDefinition.RunProfile &&
                !string.IsNullOrEmpty(archetype.LocomotionResourcesPath))
            {
                string controllerPath = "Assets/_OperationOutbreak/Resources/" +
                                        archetype.LocomotionResourcesPath + ".controller";

                if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath) == null)
                {
                    problems.Add(archetype.name + ": locomotion controller missing at '" +
                                 controllerPath + "' - run Tools > Operation Outbreak > " +
                                 "Rebuild Runner Animator Controller and commit the asset.");
                }
            }
        }

        private static void ValidateSharedPrefabRagdoll(
            EnemyArchetypeDefinition archetype, List<string> problems)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(EnemyVisualSetup.ZombiePrefabPath);

            try
            {
                EnemyRagdoll ragdoll = contents != null ? contents.GetComponent<EnemyRagdoll>() : null;

                if (ragdoll == null || !ragdoll.IsConfigured)
                {
                    problems.Add(archetype.name + ": requires the hybrid ragdoll death but " +
                                 "the shared enemy prefab has no configured EnemyRagdoll - " +
                                 "run Tools > Operation Outbreak > Set Up Basic Infected " +
                                 "Ragdoll.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [MenuItem("Tools/Operation Outbreak/Spawn Basic Infected (Debug)")]
        public static void SpawnBasicForDebug()
        {
            SpawnForDebug(EnemyArchetypeRegistry.DefaultArchetypeId);
        }

        [MenuItem("Tools/Operation Outbreak/Spawn Runner (Debug)")]
        public static void SpawnRunnerForDebug()
        {
            SpawnForDebug("runner");
        }

        private static void SpawnForDebug(string archetypeId)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[1S] Debug spawns run in Play Mode only - enter Play Mode first.");
                return;
            }

            EnemyArchetypeDefinition archetype =
                EnemyArchetypeRegistry.ResolveRequestedArchetype(archetypeId);

            if (archetype == null)
            {
                Debug.LogError(
                    "[1S] Cannot debug-spawn '" + archetypeId + "': the archetype assets are " +
                    "missing from the project.");
                return;
            }

            // Preferred path: through the shared spawner seam, so the enemy is
            // mission-tracked exactly like a real spawn.
            EnemySpawner spawner = Object.FindFirstObjectByType<EnemySpawner>();

            if (spawner != null)
            {
                ZombieController spawned = spawner.SpawnEnemy(archetype);
                Debug.Log(
                    "[1S] Debug-spawned '" + archetype.ArchetypeId + "' (" +
                    archetype.DisplayName + ", speed " + archetype.MoveSpeed.ToString("0.0") +
                    ", health " + archetype.MaxHealth + ") through the shared spawner seam.",
                    spawned);
                return;
            }

            // Fallback: no spawner in the active scene - instantiate the shared
            // prefab directly and apply the definition (NOT mission-tracked).
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                EnemyVisualSetup.ZombiePrefabPath);

            if (prefab == null)
            {
                Debug.LogError("[1S] Shared enemy prefab missing at " +
                               EnemyVisualSetup.ZombiePrefabPath + ".");
                return;
            }

            Camera camera = Camera.main;
            Vector3 position = camera != null
                ? camera.transform.position + camera.transform.forward * 5f
                : new Vector3(0f, 1f, 10f);
            position.y = 1f;

            GameObject instance = (GameObject)Object.Instantiate(prefab, position, Quaternion.identity);
            EnemyArchetypeApplication.Apply(instance, archetype);

            Debug.Log(
                "[1S] Debug-spawned '" + archetype.ArchetypeId + "' directly (no spawner in " +
                "the scene - this enemy is NOT mission-tracked).", instance);
        }
    }
}
#endif
