#if UNITY_EDITOR
using System.Collections.Generic;
using OperationOutbreak.Enemies;
using OperationOutbreak.Mission;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1T - editor validation and authoring support for MissionDefinition
    /// assets.
    ///
    ///   Tools > Operation Outbreak > Validate Mission Definitions
    ///
    /// Validation is data-only and read-only: it reports every problem that would make
    /// a mission unsafe or impossible to execute (missing identity, zero sections,
    /// duplicate section ids, empty/unknown archetype ids, non-positive counts,
    /// structurally impossible progression). It never repairs a broken asset silently -
    /// a malformed production mission must be fixed by the author, and the diagnostics
    /// identify the mission, the section and the exact correction.
    ///
    /// Authoring workflow (no C# required for a normal mission):
    ///   1. Assets > Create > Operation Outbreak > Mission Definition
    ///   2. Enter mission identity.
    ///   3. Add sections (each with a stable section id and composition entries).
    ///   4. Assign the asset to MissionSectionController.missionDefinition in the scene.
    ///   5. Run Validate Mission Definitions.
    /// </summary>
    public static class MissionDefinitionEditorTools
    {
        /// <summary>Where committed mission definition assets live.</summary>
        public const string MissionDefinitionsFolder =
            "Assets/_OperationOutbreak/Resources/MissionDefinitions";

        [MenuItem("Tools/Operation Outbreak/Validate Mission Definitions")]
        public static void ValidateAllMissionDefinitions()
        {
            bool valid = ValidateAll(out List<string> problems);

            if (problems.Count == 0)
            {
                Debug.Log("[1T] Mission definition validation PASSED: every committed mission " +
                          "has a valid id, valid numbers and fully valid sections/compositions.");
                EditorUtility.DisplayDialog(
                    "Mission Definitions",
                    "Mission definition validation PASSED.",
                    "OK");
                return;
            }

            for (int i = 0; i < problems.Count; i++)
            {
                Debug.LogError("[1T] Mission definition validation FAILED: " + problems[i]);
            }

            EditorUtility.DisplayDialog(
                "Mission Definitions",
                "Mission definition validation FAILED (" + problems.Count + " problem(s)).\n" +
                "See the Console for the mission, section and correction.",
                "OK");
        }

        /// <summary>
        /// Validates every MissionDefinition asset in the project. Returns true when
        /// there is nothing to fix; <paramref name="problems"/> carries one actionable
        /// string per problem.
        /// </summary>
        public static bool ValidateAll(out List<string> problems)
        {
            problems = new List<string>();

            HashSet<string> knownArchetypeIds = LoadKnownArchetypeIds();
            List<MissionDefinition> missions = LoadAllMissionDefinitions();

            for (int i = 0; i < missions.Count; i++)
            {
                List<string> missionProblems =
                    MissionDefinition.CollectProblems(missions[i], knownArchetypeIds);

                for (int j = 0; j < missionProblems.Count; j++)
                {
                    problems.Add(missions[i].name + ": " + missionProblems[j]);
                }
            }

            if (missions.Count == 0)
            {
                problems.Add("No MissionDefinition assets found under " +
                             MissionDefinitionsFolder + ". Create one via Assets > Create > " +
                             "Operation Outbreak > Mission Definition.");
            }

            return problems.Count == 0;
        }

        /// <summary>Loads every MissionDefinition asset in the project (editor).</summary>
        public static List<MissionDefinition> LoadAllMissionDefinitions()
        {
            List<MissionDefinition> missions = new List<MissionDefinition>();
            string[] guids = AssetDatabase.FindAssets("t:MissionDefinition");

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MissionDefinition mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>(path);

                if (mission != null)
                {
                    missions.Add(mission);
                }
            }

            return missions;
        }

        /// <summary>
        /// The set of stable ids the 1S archetype registry can resolve, from the
        /// committed EnemyArchetypeDefinition assets.
        /// </summary>
        public static HashSet<string> LoadKnownArchetypeIds()
        {
            HashSet<string> ids = new HashSet<string>();
            List<EnemyArchetypeDefinition> archetypes =
                EnemyArchetypeEditorTools.LoadAllArchetypeDefinitions();

            for (int i = 0; i < archetypes.Count; i++)
            {
                if (archetypes[i] != null && !string.IsNullOrEmpty(archetypes[i].ArchetypeId))
                {
                    ids.Add(archetypes[i].ArchetypeId);
                }
            }

            return ids;
        }
    }
}
#endif
