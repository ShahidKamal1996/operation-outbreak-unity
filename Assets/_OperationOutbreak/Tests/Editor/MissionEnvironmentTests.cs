using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.EditorTools;
using OperationOutbreak.Environment;
using OperationOutbreak.Mission;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1W - EditMode regression tests for the Chapter 1 environment/content
    /// pipeline. They pin: Mission 01 references a valid, stable-id Chapter 1 Outskirts
    /// profile whose materials/prefabs resolve; the deterministic assembly plan is
    /// section-aligned and repeatable; the environment namespace holds NO
    /// mission-specific gameplay controllers and NO second completion path; the
    /// Mission 01 shape (3/12/9/3) and objective/reward configuration are unchanged;
    /// validation rejects null/invalid profiles and missing references; the gameplay
    /// corridor (CombatLane/boundaries) is untouched; no dressing sits inside the
    /// playable band; decorative modules carry no physics components; and the profile
    /// holds no runtime mission state.
    /// </summary>
    public sealed class MissionEnvironmentTests
    {
        private const string MissionAssetPath =
            "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_01.asset";

        private const string ScenePath =
            "Assets/_OperationOutbreak/Scenes/Gameplay_Prototype.unity";

        private const string KitFolder = "Assets/_OperationOutbreak/Prefabs/Environment";

        private const string StartGateGuid = "c3c5895d25567ec4878a1177e0e368b0";
        private const string TransitionGuid = "7ce6c24146d69fd1187b5f82747ff9fc";
        private const string FinalRoadblockGuid = "fc64bef05209876643cf175da50b95a0";

        // ------------------------------------------------------------------ helpers

        private static MissionDefinition LoadCommittedMission()
        {
            MissionDefinition mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>(MissionAssetPath);
            Assert.IsNotNull(mission, "The committed Mission_01 asset must exist at " + MissionAssetPath + ".");
            return mission;
        }

        private static string ReadSceneText()
        {
            Assert.IsTrue(File.Exists(ScenePath), "Expected the gameplay scene at " + ScenePath + ".");
            return File.ReadAllText(ScenePath);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Field '" + fieldName + "' missing on " + target.GetType().Name + ".");
            field.SetValue(target, value);
        }

        private static MissionEnvironmentDefinition NewProfile(string id)
        {
            MissionEnvironmentDefinition profile =
                ScriptableObject.CreateInstance<MissionEnvironmentDefinition>();
            SetField(profile, "environmentId", id);
            SetField(profile, "displayName", id);
            return profile;
        }

        // ------------------------------------------------- committed profile wiring

        [Test]
        public void Mission01ReferencesValidEnvironmentProfile()
        {
            MissionDefinition mission = LoadCommittedMission();

            Assert.IsNotNull(mission.Environment,
                "Mission 01 must reference a Chapter 1 environment profile.");
            Assert.AreEqual("c1_outbreak_outskirts", mission.Environment.EnvironmentId,
                "Mission 01 must reference the Chapter 1 Outbreak Outskirts profile.");

            List<string> problems = MissionEnvironmentDefinition.CollectProblems(mission.Environment);
            Assert.IsEmpty(problems,
                "The Mission 01 environment profile must validate cleanly: " +
                string.Join(" | ", problems));
        }

        [Test]
        public void Chapter1OutskirtsProfileHasStableUniqueId()
        {
            List<MissionEnvironmentDefinition> profiles =
                MissionEnvironmentEditorTools.LoadAllEnvironmentProfiles();

            Assert.IsNotEmpty(profiles, "At least one environment profile must be committed.");

            int matches = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i] != null && profiles[i].EnvironmentId == "c1_outbreak_outskirts")
                {
                    matches++;
                }
            }

            Assert.AreEqual(1, matches,
                "The 'c1_outbreak_outskirts' environment id must be unique and committed exactly once.");
        }

        [Test]
        public void Chapter1OutskirtsProfileResolvesRequiredAssets()
        {
            MissionEnvironmentDefinition profile = LoadCommittedMission().Environment;

            Assert.IsNotNull(profile, "Mission 01 must have an environment profile.");
            Assert.IsNotNull(profile.RoadMaterial, "Road material must resolve.");
            Assert.IsNotNull(profile.BarrierMaterial, "Barrier material must resolve.");
            Assert.IsNotNull(profile.RoadMarkingMaterial, "Road marking material must resolve.");
            Assert.IsNotNull(profile.RoadsideMaterial, "Roadside material must resolve.");
            Assert.IsNotNull(profile.AccentMaterial, "Accent material must resolve.");
            Assert.IsNotNull(profile.StartLandmarkPrefab, "Start landmark prefab must resolve.");
            Assert.IsNotNull(profile.TransitionLandmarkPrefab, "Transition landmark prefab must resolve.");
            Assert.IsNotNull(profile.FinalLandmarkPrefab, "Final landmark prefab must resolve.");
            Assert.GreaterOrEqual(profile.SideDressingPrefabs.Count, 1,
                "The dressing library must hold at least one reusable module.");
        }

        // ------------------------------------------------- deterministic assembly

        [Test]
        public void Mission01EnvironmentPlanIsDeterministicAndSectionAligned()
        {
            MissionDefinition mission = LoadCommittedMission();

            List<string> first = MissionEnvironmentEditorTools.BuildEnvironmentPlan(mission);
            List<string> second = MissionEnvironmentEditorTools.BuildEnvironmentPlan(mission);

            Assert.IsNotEmpty(first, "The environment plan must not be empty.");
            Assert.AreEqual(first.Count, second.Count, "The plan must be deterministic.");
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i], second[i],
                    "Two builds of the same mission must produce an identical plan.");
            }

            Assert.IsTrue(first.Exists(e => e == "landmark:transition@20"),
                "The plan must place a transition landmark on Section 2's activation line (z=20).");
            Assert.IsTrue(first.Exists(e => e == "landmark:transition@38"),
                "The plan must place a transition landmark on Section 3's activation line (z=38).");
            Assert.IsTrue(first.Exists(e => e == "landmark:final@62"),
                "The plan must place the final landmark beyond the last forward limit (z=62).");
        }

        // ------------------------------------------------- architecture invariants

        [Test]
        public void EnvironmentNamespaceContainsNoMissionSpecificGameplayController()
        {
            Assembly assembly = typeof(MissionEnvironmentDefinition).Assembly;

            Assert.IsNull(assembly.GetType("OperationOutbreak.Environment.Mission01EnvironmentController"),
                "Mission01EnvironmentController must not exist.");
            Assert.IsNull(assembly.GetType("OperationOutbreak.Environment.Mission02RoadBuilder"),
                "Mission02RoadBuilder must not exist.");
            Assert.IsNull(assembly.GetType("OperationOutbreak.Environment.Mission03CheckpointManager"),
                "Mission03CheckpointManager must not exist.");
            Assert.IsNull(assembly.GetType("OperationOutbreak.Environment.Chapter1EnvironmentManager"),
                "Chapter1EnvironmentManager must not exist.");
        }

        [Test]
        public void EnvironmentProfileDoesNotReplaceMissionDefinitionAuthority()
        {
            // The profile is static presentation data: no lifecycle methods, and no
            // references into the mission/objective/reward authority systems.
            foreach (MethodInfo method in typeof(MissionEnvironmentDefinition).GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                if (method.Name == "Update" || method.Name == "LateUpdate" ||
                    method.Name == "FixedUpdate" || method.Name == "Awake" ||
                    method.Name == "OnEnable")
                {
                    Assert.Fail("MissionEnvironmentDefinition must not run lifecycle logic: " + method.Name);
                }
            }

            foreach (FieldInfo field in typeof(MissionEnvironmentDefinition).GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(MissionDefinition) ||
                    field.FieldType == typeof(MissionObjectiveDefinition) ||
                    field.FieldType == typeof(MissionRewardDefinition))
                {
                    Assert.Fail("MissionEnvironmentDefinition must not own mission authority data: " + field.Name);
                }
            }

            // MissionDefinition keeps the environment as a static reference only.
            Assert.IsNotNull(
                typeof(MissionDefinition).GetProperty("Environment"),
                "MissionDefinition must expose its environment reference.");
        }

        [Test]
        public void EnvironmentDressingDoesNotIntroduceSecondCompletionPath()
        {
            // The environment profile is inert: it has no events and no completion types.
            Assert.AreEqual(0, typeof(MissionEnvironmentDefinition).GetEvents().Length,
                "MissionEnvironmentDefinition must not publish any events.");

            foreach (System.Type type in typeof(MissionEnvironmentDefinition).Assembly.GetTypes())
            {
                if (type.Namespace == "OperationOutbreak.Environment" &&
                    (type.Name.Contains("Objective") || type.Name.Contains("Reward") ||
                     type.Name.Contains("Completion") || type.Name.EndsWith("Controller")))
                {
                    Assert.Fail("Environment namespace must stay data-only, found: " + type.Name);
                }
            }

            // The single completion authority remains MissionObjectiveController.
            Assert.IsNotNull(
                typeof(MissionObjectiveController).GetMethod(
                    "EvaluateRequiredObjectives", BindingFlags.Instance | BindingFlags.NonPublic),
                "MissionObjectiveController must remain the completion authority.");
        }

        // ------------------------------------------------- Unity importability (1W QA fix #1)

        [Test]
        public void KitPrefabsLoadAsValidRegularPrefabsWithResolvedComponents()
        {
            // 1W QA fix #1 - static YAML anchor checks are NOT enough: Unity must
            // actually import each kit module as a regular prefab. This loads every
            // committed kit prefab through the AssetDatabase and proves it is a valid
            // Regular prefab whose components and materials resolve (no corrupt prefab,
            // no missing Variant parent, no broken references).
            string[] prefabs = Directory.GetFiles(KitFolder, "*.prefab");
            Assert.AreEqual(8, prefabs.Length, "All 8 committed kit prefabs must exist.");

            for (int i = 0; i < prefabs.Length; i++)
            {
                string path = prefabs[i].Replace("\\", "/");
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                Assert.IsNotNull(go, path + " must load as a GameObject asset.");
                Assert.AreEqual(
                    PrefabAssetType.Regular,
                    PrefabUtility.GetPrefabAssetType(go),
                    path + " must import as a Regular prefab (not broken/variant/missing).");

                Transform rootTransform = go.GetComponent<Transform>();
                Assert.IsNotNull(rootTransform, path + " must have a root Transform.");

                MeshRenderer[] renderers = go.GetComponentsInChildren<MeshRenderer>(true);
                Assert.Greater(renderers.Length, 0, path + " must render at least one mesh.");

                for (int j = 0; j < renderers.Length; j++)
                {
                    Assert.IsNotNull(renderers[j].sharedMaterial,
                        path + " has a MeshRenderer with no material.");
                    Assert.IsNotNull(renderers[j].GetComponent<MeshFilter>(),
                        path + " has a MeshRenderer without a MeshFilter.");
                }
            }
        }

        [Test]
        public void ScenePrefabInstancesReferenceThePrefabObjectFileId()
        {
            // 1W QA fix #1 regression - the scene's PrefabInstance.m_SourcePrefab must
            // reference the prefab asset's internal Prefab object (fileID 100100000),
            // NOT the root GameObject fileID (100001). The original hand-authored
            // blocks used 100001, which Unity rejected with
            // "PPtr cast failed ... Casting from GameObject to Prefab at fileID 100001"
            // and "corrupt / missing Variant parent or nested Prefabs" on import.
            string scene = ReadSceneText();

            string[] kitGuids =
            {
                "a30581505fc58961ad5eb626f307a567", // C1_Barrier_Concrete
                "72d64da3c86ae5500d31b1fe46826896", // C1_Barrier_Checkpoint
                "c3adb0b9d1e468101a7c8205f30ae0de", // C1_Prop_Debris
                "f458a656813567caf6570c07bfe20a80", // C1_Prop_Crate
                "10d476f7440c1c82af2a6c43610aa159", // C1_Prop_Cone
                "c3c5895d25567ec4878a1177e0e368b0", // C1_Landmark_StartGate
                "7ce6c24146d69fd1187b5f82747ff9fc", // C1_Landmark_Transition
                "fc64bef05209876643cf175da50b95a0"  // C1_Landmark_FinalRoadblock
            };

            for (int i = 0; i < kitGuids.Length; i++)
            {
                string guid = kitGuids[i];
                int instances = System.Text.RegularExpressions.Regex.Matches(
                    scene, "m_SourcePrefab: \\{fileID: 100100000, guid: " + guid).Count;

                Assert.Greater(instances, 0,
                    "Kit prefab " + guid + " must be instanced in the scene with the " +
                    "correct prefab-object source reference.");
                Assert.AreEqual(
                    0,
                    System.Text.RegularExpressions.Regex.Matches(
                        scene, "m_SourcePrefab: \\{fileID: 100001, guid: " + guid).Count,
                    "Kit prefab " + guid + " must NOT reference fileID 100001 (the root " +
                    "GameObject) as its source prefab - that produced the PPtr cast failure.");
            }
        }

        // ------------------------------------------------- Mission 01 preservation

        [Test]
        public void Mission01ShapeRemainsUnchanged()
        {
            MissionDefinition mission = LoadCommittedMission();

            Assert.AreEqual(3, mission.SectionCount, "Mission 01 must keep three sections.");
            Assert.AreEqual(12, mission.TotalEnemyCount, "Mission 01 must keep twelve enemies.");
            Assert.AreEqual(9, mission.GetArchetypeCount(MissionDefinition.BasicArchetypeId),
                "Mission 01 must keep nine Basic.");
            Assert.AreEqual(3, mission.GetArchetypeCount(MissionDefinition.RunnerArchetypeId),
                "Mission 01 must keep three Runners.");
        }

        [Test]
        public void Mission01ObjectiveAndRewardConfigurationUnchanged()
        {
            MissionDefinition mission = LoadCommittedMission();

            MissionObjectiveDefinition objective = mission.GetObjective("clear_all_sections");
            Assert.IsNotNull(objective, "Mission 01 must keep its clear_all_sections objective.");
            Assert.AreEqual(MissionObjectiveType.ClearAllSections, objective.objectiveType);
            Assert.IsTrue(objective.required);

            Assert.IsNotNull(mission.Reward, "Mission 01 must keep its reward definition.");
            Assert.AreEqual(0, mission.Reward.coins, "Mission 01 keeps zero Coins.");
            Assert.AreEqual(0, mission.Reward.supplies, "Mission 01 keeps zero Supplies.");
        }

        // ------------------------------------------------- landmarks + scene

        [Test]
        public void RequiredEnvironmentLandmarksPresentInProfileAndScene()
        {
            MissionEnvironmentDefinition profile = LoadCommittedMission().Environment;

            Assert.IsNotNull(profile.StartLandmarkPrefab, "The start landmark must be configured.");
            Assert.IsNotNull(profile.TransitionLandmarkPrefab, "The transition landmark must be configured.");
            Assert.IsNotNull(profile.FinalLandmarkPrefab, "The final landmark must be configured.");

            string scene = ReadSceneText();

            Assert.IsTrue(scene.Contains("guid: " + StartGateGuid),
                "The scene must instance the start checkpoint landmark.");
            Assert.IsTrue(scene.Contains("guid: " + TransitionGuid),
                "The scene must instance the section-transition landmarks.");
            Assert.IsTrue(scene.Contains("guid: " + FinalRoadblockGuid),
                "The scene must instance the final roadblock landmark.");
        }

        [Test]
        public void SceneContainsAuthoredOutskirtsDressing()
        {
            string scene = ReadSceneText();

            Assert.IsTrue(scene.Contains("m_Name: Outskirts"),
                "The scene must contain the Outskirts environment root.");
            Assert.IsTrue(scene.Contains("Roadside_Left") && scene.Contains("Roadside_Right"),
                "The scene must contain the roadside dressing strips.");
            Assert.IsTrue(scene.Contains("m_Name: C1_Barrier_Concrete"),
                "The scene must instance the concrete barrier kit module.");
            Assert.IsTrue(scene.Contains("m_Name: C1_Prop_Debris"),
                "The scene must instance the debris kit module.");
        }

        // ------------------------------------------------- validation

        [Test]
        public void MissingEnvironmentProfileIsRejected()
        {
            List<string> problems = MissionEnvironmentDefinition.CollectProblems(null);

            Assert.IsTrue(Contains(problems, "Environment profile is null"),
                "A null environment profile must be rejected.");
        }

        [Test]
        public void InvalidEnvironmentIdIsRejected()
        {
            MissionEnvironmentDefinition profile = NewProfile("");

            try
            {
                List<string> problems = MissionEnvironmentDefinition.CollectProblems(profile);
                Assert.IsTrue(Contains(problems, "missing stable environment id"),
                    "An empty environment id must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void MissingMaterialAndPrefabReferencesAreRejected()
        {
            MissionEnvironmentDefinition profile = NewProfile("c1_test");
            SetField(profile, "roadMaterial", null);
            SetField(profile, "finalLandmarkPrefab", null);

            try
            {
                List<string> problems = MissionEnvironmentDefinition.CollectProblems(profile);
                Assert.IsTrue(Contains(problems, "missing road material"),
                    "A missing road material must be rejected.");
                Assert.IsTrue(Contains(problems, "missing final landmark prefab"),
                    "A missing landmark prefab must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        // ------------------------------------------------- gameplay-lane protection

        [Test]
        public void GameplayLaneCorridorPreserved()
        {
            string scene = ReadSceneText();

            // The verified gameplay geometry is byte-identical: ground, side walls.
            Assert.IsTrue(scene.Contains("m_LocalPosition: {x: 0, y: -0.25, z: 40}"),
                "CombatLane position must be unchanged.");
            Assert.IsTrue(scene.Contains("m_LocalScale: {x: 12, y: 0.5, z: 100}"),
                "CombatLane scale must be unchanged.");
            Assert.IsTrue(scene.Contains("m_LocalPosition: {x: -6.3, y: 0.5, z: 40}"),
                "Boundary_Left position must be unchanged.");
            Assert.IsTrue(scene.Contains("m_LocalPosition: {x: 6.3, y: 0.5, z: 40}"),
                "Boundary_Right position must be unchanged.");
        }

        [Test]
        public void NoDressingInsidePlayableBand()
        {
            string scene = ReadSceneText();

            var xs = new List<float>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         scene, @"propertyPath: m_LocalPosition\.x\s*\n\s*value: (-?[\d.]+)"))
            {
                xs.Add(float.Parse(m.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture));
            }

            Assert.IsNotEmpty(xs, "Expected authored dressing instances with positions.");

            for (int i = 0; i < xs.Count; i++)
            {
                float x = xs[i];
                if (x == 0f)
                {
                    continue; // overhead landmarks (gates/roadblock) span the lane ABOVE it.
                }

                Assert.GreaterOrEqual(Mathf.Abs(x), 6.0f,
                    "Decorative dressing must stay outside the playable band " +
                    "(found a dressing instance at x=" + x + ").");
            }
        }

        [Test]
        public void DecorativeModulesCarryNoPhysicsComponents()
        {
            // The kit is decorative: no colliders, no rigidbodies - nothing that could
            // snag the player, block enemies or intercept projectiles.
            string[] prefabs = Directory.GetFiles(KitFolder, "*.prefab");
            Assert.GreaterOrEqual(prefabs.Length, 6, "Expected the committed kit prefabs.");

            for (int i = 0; i < prefabs.Length; i++)
            {
                string text = File.ReadAllText(prefabs[i]);
                Assert.IsFalse(text.Contains("BoxCollider"),
                    Path.GetFileName(prefabs[i]) + " must not carry a BoxCollider.");
                Assert.IsFalse(text.Contains("CapsuleCollider"),
                    Path.GetFileName(prefabs[i]) + " must not carry a CapsuleCollider.");
                Assert.IsFalse(text.Contains("Rigidbody"),
                    Path.GetFileName(prefabs[i]) + " must not carry a Rigidbody.");
                Assert.IsFalse(text.Contains("MeshCollider"),
                    Path.GetFileName(prefabs[i]) + " must not carry a MeshCollider.");
            }
        }

        // ------------------------------------------------- runtime-state boundary

        [Test]
        public void EnvironmentProfileContainsNoRuntimeMissionState()
        {
            foreach (FieldInfo field in typeof(MissionEnvironmentDefinition).GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string name = field.Name.ToLowerInvariant();
                if (name.Contains("progress") || name.Contains("result") ||
                    name.Contains("grant") || name.Contains("wallet") ||
                    name.Contains("balance") || name.Contains("earned") ||
                    name.Contains("objective") || name.Contains("reward"))
                {
                    Assert.Fail("MissionEnvironmentDefinition must hold no runtime mission state: " + field.Name);
                }
            }
        }

        private static bool Contains(List<string> problems, string fragment)
        {
            for (int i = 0; i < problems.Count; i++)
            {
                if (problems[i].Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
