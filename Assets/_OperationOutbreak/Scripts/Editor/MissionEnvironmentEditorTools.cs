#if UNITY_EDITOR
using System.Collections.Generic;
using OperationOutbreak.Environment;
using OperationOutbreak.Mission;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1W - editor validation + deterministic assembly plan for Chapter 1
    /// environment profiles.
    ///
    ///   Tools > Operation Outbreak > Validate Mission Environment
    ///
    /// Validation is read-only: it reports missing/invalid profiles, duplicate
    /// environment ids, missing materials/prefabs, and any committed MissionDefinition
    /// that references no environment profile (Mission 01 must reference the Chapter 1
    /// Outskirts profile). It never repairs data silently.
    ///
    /// `BuildEnvironmentPlan` is the DETERMINISTIC assembly seam: given a
    /// MissionDefinition (and its environment profile) it produces a stable, ordered
    /// placement plan (road, roadside, landmarks at the section activation lines, the
    /// final landmark, and the dressing library) from a fixed formula + the profile's
    /// stored seed. The same mission always yields the same plan - no random layout,
    /// no gameplay geometry is ever generated. The committed Mission 01 scene is the
    /// authored instance of this plan.
    /// </summary>
    public static class MissionEnvironmentEditorTools
    {
        [MenuItem("Tools/Operation Outbreak/Validate Mission Environment")]
        public static void ValidateMissionEnvironment()
        {
            bool valid = ValidateAll(out List<string> problems);

            if (problems.Count == 0)
            {
                Debug.Log("[1W] Environment validation PASSED: every profile is valid and every " +
                          "committed mission references a valid environment profile.");
                EditorUtility.DisplayDialog("Mission Environment", "Environment validation PASSED.", "OK");
                return;
            }

            for (int i = 0; i < problems.Count; i++)
            {
                Debug.LogError("[1W] Environment validation FAILED: " + problems[i]);
            }

            EditorUtility.DisplayDialog(
                "Mission Environment",
                "Environment validation FAILED (" + problems.Count + " problem(s)). See the Console.",
                "OK");
        }

        /// <summary>
        /// Validates every MissionEnvironmentDefinition asset, duplicate ids, and that
        /// every committed MissionDefinition references a valid environment profile.
        /// </summary>
        public static bool ValidateAll(out List<string> problems)
        {
            problems = new List<string>();

            List<MissionEnvironmentDefinition> profiles = LoadAllEnvironmentProfiles();

            if (profiles.Count == 0)
            {
                problems.Add("No environment profiles found - create one via Assets > Create > " +
                             "Operation Outbreak > Environment Profile.");
                return false;
            }

            HashSet<string> seen = new HashSet<string>();

            for (int i = 0; i < profiles.Count; i++)
            {
                MissionEnvironmentDefinition profile = profiles[i];
                List<string> profileProblems = MissionEnvironmentDefinition.CollectProblems(profile);

                for (int j = 0; j < profileProblems.Count; j++)
                {
                    problems.Add(profileProblems[j]);
                }

                if (profile != null && !string.IsNullOrEmpty(profile.EnvironmentId))
                {
                    if (!seen.Add(profile.EnvironmentId))
                    {
                        problems.Add(profile.name + ": duplicate environment id '" +
                                     profile.EnvironmentId + "'.");
                    }
                }
            }

            // Every committed MissionDefinition must reference an environment profile.
            List<MissionDefinition> missions = MissionDefinitionEditorTools.LoadAllMissionDefinitions();

            for (int i = 0; i < missions.Count; i++)
            {
                MissionDefinition mission = missions[i];

                if (mission.Environment == null)
                {
                    problems.Add(mission.name + ": missing environment profile reference.");
                    continue;
                }

                List<string> referenced = MissionEnvironmentDefinition.CollectProblems(mission.Environment);
                for (int j = 0; j < referenced.Count; j++)
                {
                    problems.Add(mission.name + " -> " + referenced[j]);
                }
            }

            return problems.Count == 0;
        }

        /// <summary>Loads every environment profile asset in the project (editor).</summary>
        public static List<MissionEnvironmentDefinition> LoadAllEnvironmentProfiles()
        {
            List<MissionEnvironmentDefinition> profiles = new List<MissionEnvironmentDefinition>();
            string[] guids = AssetDatabase.FindAssets("t:MissionEnvironmentDefinition");

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MissionEnvironmentDefinition profile =
                    AssetDatabase.LoadAssetAtPath<MissionEnvironmentDefinition>(path);

                if (profile != null)
                {
                    profiles.Add(profile);
                }
            }

            return profiles;
        }

        /// <summary>
        /// Deterministic environment assembly plan for a mission. The plan is a stable,
        /// ordered list of entries derived from the profile and the mission's sections -
        /// two calls for the same mission always return an identical plan (the layout
        /// never changes randomly). Landmarks land on the section activation lines; the
        /// final landmark sits beyond the last forward limit; dressing is the profile's
        /// library. This is the seam future Chapter 1 missions consume; it never mutates
        /// gameplay geometry (landmarks are placed at the lane shoulders / overhead and
        /// dressing outside the playable band, all without colliders).
        /// </summary>
        public static List<string> BuildEnvironmentPlan(MissionDefinition mission)
        {
            List<string> plan = new List<string>();

            if (mission == null || mission.Environment == null)
            {
                return plan;
            }

            MissionEnvironmentDefinition profile = mission.Environment;

            plan.Add("profile:" + profile.EnvironmentId + "@seed:" + profile.DeterministicSeed);
            plan.Add("road:roadMaterial");
            plan.Add("road:roadMarkingMaterial");
            plan.Add("roadside:left");
            plan.Add("roadside:right");

            plan.Add("landmark:start@z=-5");

            int sectionCount = mission.SectionCount;

            for (int i = 1; i < sectionCount; i++)
            {
                MissionDefinition.MissionSection section = mission.GetSection(i);
                if (section != null)
                {
                    plan.Add("landmark:transition@" + section.activationZ);
                }
            }

            if (sectionCount > 0)
            {
                MissionDefinition.MissionSection last = mission.GetSection(sectionCount - 1);
                if (last != null)
                {
                    plan.Add("landmark:final@" + (last.forwardLimitZ + 11f));
                }
            }

            IReadOnlyList<GameObject> dressing = profile.SideDressingPrefabs;

            if (dressing != null)
            {
                for (int i = 0; i < dressing.Count; i++)
                {
                    GameObject module = dressing[i];
                    if (module != null)
                    {
                        plan.Add("dressing:" + module.name + "@shoulder");
                    }
                }
            }

            return plan;
        }
    }
}
#endif
