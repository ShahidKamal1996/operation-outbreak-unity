using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OperationOutbreak.Environment;
using OperationOutbreak.Mission;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1W visual QA fix #3 - EditMode regression tests for the PRESENTATION
    /// constraints of the Chapter 1 Outbreak Outskirts environment.
    ///
    /// The manual portrait gameplay QA rejected the first Outskirts pass for four
    /// reasons: full-width red/orange horizontal bars that read as raw prototype cubes,
    /// unfinished single-box roadside structures, an empty road, three sections that
    /// looked identical, and a weak final roadblock. These tests pin the fixes so the
    /// same regressions cannot come back:
    ///
    ///   * NO non-flat dressing spans the lane, and the central combat sightline
    ///     (|x| &lt;= 4.5, below y = 6) stays completely clear for the whole playable
    ///     corridor (z = -8 .. 58, i.e. past the furthest Section 3 spawn);
    ///   * road detail is strictly FLAT, so filling the empty asphalt can never
    ///     obstruct the player, the infected or the projectiles;
    ///   * the environment palette is restrained - low saturation, low value, and
    ///     measurably far from the yellow projectile / orange Runner / green infected;
    ///   * the quarantine accent survives only on small elements (no big red bars);
    ///   * the three sections are compositionally DIFFERENT and damage escalates
    ///     towards the final approach;
    ///   * the backdrop silhouettes stay beyond the final roadblock;
    ///   * the original kit GUIDs are preserved and the library grew rather than moved.
    ///
    /// Gameplay, camera, colliders, spawning, objectives, rewards and UI are out of
    /// scope here and are covered by the existing suites.
    /// </summary>
    public sealed class Chapter1EnvironmentVisualTests
    {
        private const string ScenePath =
            "Assets/_OperationOutbreak/Scenes/Gameplay_Prototype.unity";

        private const string KitFolder = "Assets/_OperationOutbreak/Prefabs/Environment";
        private const string EnvironmentMaterialFolder =
            "Assets/_OperationOutbreak/Materials/Environment";

        private const string AccentMaterialPath =
            EnvironmentMaterialFolder + "/OO_C1_Checkpoint.mat";

        // The playable combat corridor. z = 58 is comfortably past the furthest
        // Section 3 spawn (forward limit 51 + 4 spawn-ahead); the final roadblock at
        // z = 62 is a BACKDROP beyond it, never an obstacle.
        private const float SightlineHalfWidth = 4.5f;
        private const float CorridorZMin = -8f;
        private const float CorridorZMax = 58f;
        private const float CorridorCeiling = 6f;

        // Anything at or below this height is a flat road decal: it cannot block a
        // projectile, snag the player or hide an enemy.
        private const float FlatDecalHeight = 0.06f;

        private static readonly string[] OriginalKitGuids =
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

        // Gameplay colours the environment must never compete with.
        private static readonly Color ProjectileYellow = new Color(1f, 0.78f, 0.08f);
        private static readonly Color RunnerOrange = new Color(0.92f, 0.42f, 0.09f);
        private static readonly Color InfectedGreen = new Color(0.18f, 0.56f, 0.24f);

        // ------------------------------------------------------------------ helpers

        private sealed class SceneScope : System.IDisposable
        {
            public Scene Scene;
            private readonly bool _opened;

            public SceneScope()
            {
                Scene = EditorSceneManager.GetSceneByPath(ScenePath);
                if (!Scene.isLoaded)
                {
                    Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                    _opened = true;
                }
            }

            public void Dispose()
            {
                if (_opened)
                {
                    EditorSceneManager.CloseScene(Scene, true);
                }
            }
        }

        private static GameObject FindNamed(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                GameObject found = FindNamedRecursive(root, objectName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static GameObject FindNamedRecursive(GameObject gameObject, string objectName)
        {
            if (gameObject.name == objectName)
            {
                return gameObject;
            }

            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                GameObject found =
                    FindNamedRecursive(gameObject.transform.GetChild(i).gameObject, objectName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static GameObject RequireOutskirts(Scene scene)
        {
            GameObject outskirts = FindNamed(scene, "Outskirts");
            Assert.IsNotNull(outskirts, "The scene must contain the Outskirts environment root.");
            return outskirts;
        }

        private static List<string> ModuleNamesUnder(GameObject root)
        {
            List<string> names = new List<string>();
            CollectModuleNames(root, names);
            return names;
        }

        private static void CollectModuleNames(GameObject gameObject, List<string> names)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(gameObject) &&
                PrefabUtility.GetOutermostPrefabInstanceRoot(gameObject) == gameObject)
            {
                GameObject source =
                    PrefabUtility.GetCorrespondingObjectFromSource(gameObject) as GameObject;
                if (source != null)
                {
                    names.Add(source.name);
                }
            }

            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                CollectModuleNames(gameObject.transform.GetChild(i).gameObject, names);
            }
        }

        private static int CountModules(List<string> names, params string[] wanted)
        {
            int count = 0;
            for (int i = 0; i < names.Count; i++)
            {
                for (int j = 0; j < wanted.Length; j++)
                {
                    if (names[i] == wanted[j])
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private static bool OverlapsCorridorZ(Bounds b)
        {
            return b.max.z >= CorridorZMin && b.min.z <= CorridorZMax;
        }

        // ------------------------------------------------- corridor / sightline

        [Test]
        public void CentralCombatSightlineStaysClearOfDressing()
        {
            using (SceneScope scope = new SceneScope())
            {
                GameObject outskirts = RequireOutskirts(scope.Scene);
                MeshRenderer[] renderers = outskirts.GetComponentsInChildren<MeshRenderer>(true);
                Assert.Greater(renderers.Length, 0, "The Outskirts dressing must render something.");

                List<string> offenders = new List<string>();

                for (int i = 0; i < renderers.Length; i++)
                {
                    Bounds b = renderers[i].bounds;

                    if (b.size.y <= FlatDecalHeight)
                    {
                        continue; // flat road detail can never block anything
                    }

                    if (!OverlapsCorridorZ(b) || b.min.y > CorridorCeiling)
                    {
                        continue;
                    }

                    if (b.max.x >= -SightlineHalfWidth && b.min.x <= SightlineHalfWidth)
                    {
                        offenders.Add(renderers[i].name + " (x " + b.min.x + ".." + b.max.x +
                                      ", z " + b.min.z + ".." + b.max.z + ")");
                    }
                }

                Assert.IsEmpty(offenders,
                    "Environment dressing must never enter the central combat sightline " +
                    "(|x| <= " + SightlineHalfWidth + ") inside the playable corridor: " +
                    string.Join(" | ", offenders));
            }
        }

        [Test]
        public void NoWideHorizontalSpanCrossesTheLane()
        {
            // The rejected pass used 13.5-14.6 m accent beams over the road. Nothing
            // solid may span the lane again, at ANY height, inside the corridor.
            using (SceneScope scope = new SceneScope())
            {
                GameObject outskirts = RequireOutskirts(scope.Scene);
                MeshRenderer[] renderers = outskirts.GetComponentsInChildren<MeshRenderer>(true);

                List<string> offenders = new List<string>();

                for (int i = 0; i < renderers.Length; i++)
                {
                    Bounds b = renderers[i].bounds;

                    if (b.size.y <= FlatDecalHeight)
                    {
                        continue;
                    }

                    if (b.min.z > 62f || b.max.z < CorridorZMin)
                    {
                        continue;
                    }

                    bool crossesLane = b.max.x > -6f && b.min.x < 6f;
                    if (crossesLane && b.size.x > 8f)
                    {
                        offenders.Add(renderers[i].name + " (width " + b.size.x + ")");
                    }
                }

                Assert.IsEmpty(offenders,
                    "No non-flat dressing may span the lane like the removed prototype " +
                    "beams: " + string.Join(" | ", offenders));
            }
        }

        [Test]
        public void LandmarkPrefabsCarryNoFullWidthBeam()
        {
            // Direct prefab-level guard: the start gate, the section transition and the
            // final roadblock must not contain a single wide horizontal slab again.
            string[] landmarks =
            {
                KitFolder + "/C1_Landmark_StartGate.prefab",
                KitFolder + "/C1_Landmark_Transition.prefab",
                KitFolder + "/C1_Landmark_FinalRoadblock.prefab"
            };

            for (int i = 0; i < landmarks.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(landmarks[i]);
                Assert.IsNotNull(prefab, landmarks[i] + " must exist.");

                Transform[] parts = prefab.GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < parts.Length; j++)
                {
                    Transform part = parts[j];
                    if (part == prefab.transform)
                    {
                        continue;
                    }

                    bool flat = part.localScale.y <= FlatDecalHeight;
                    bool wide = part.localScale.x > 8f;
                    bool overTheLane = Mathf.Abs(part.localPosition.x) < 6f;

                    Assert.IsFalse(wide && overTheLane && !flat,
                        Path.GetFileName(landmarks[i]) + " child '" + part.name +
                        "' is a full-width horizontal slab over the lane (scale " +
                        part.localScale + ") - that is the rejected prototype beam.");
                }
            }
        }

        [Test]
        public void RoadDetailIsFlatAndNonBlocking()
        {
            using (SceneScope scope = new SceneScope())
            {
                GameObject roadDetail = FindNamed(scope.Scene, "RoadDetail");
                Assert.IsNotNull(roadDetail,
                    "The Outskirts must carry a RoadDetail group - the empty road was a QA failure.");

                MeshRenderer[] renderers = roadDetail.GetComponentsInChildren<MeshRenderer>(true);
                Assert.GreaterOrEqual(renderers.Length, 24,
                    "The road needs enough flat detail to stop reading as empty asphalt.");

                for (int i = 0; i < renderers.Length; i++)
                {
                    Bounds b = renderers[i].bounds;
                    Assert.LessOrEqual(b.size.y, FlatDecalHeight,
                        "Road detail '" + renderers[i].name + "' must be flat (height " +
                        b.size.y + ").");
                    Assert.LessOrEqual(b.max.y, FlatDecalHeight,
                        "Road detail '" + renderers[i].name + "' must stay on the road surface.");
                }
            }
        }

        [Test]
        public void BackdropSilhouettesStayBeyondTheFinalRoadblock()
        {
            using (SceneScope scope = new SceneScope())
            {
                GameObject backdrop = FindNamed(scope.Scene, "Backdrop");
                Assert.IsNotNull(backdrop, "The Outskirts must carry a Backdrop group.");

                MeshRenderer[] renderers = backdrop.GetComponentsInChildren<MeshRenderer>(true);
                Assert.Greater(renderers.Length, 0, "The backdrop must render silhouettes.");

                for (int i = 0; i < renderers.Length; i++)
                {
                    Assert.Greater(renderers[i].bounds.min.z, 62f,
                        "Backdrop element '" + renderers[i].name + "' must stay beyond the " +
                        "final roadblock so it can never interfere with combat readability.");
                }
            }
        }

        // ------------------------------------------------- palette / readability

        [Test]
        public void EnvironmentPaletteDoesNotCompeteWithGameplayColours()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { EnvironmentMaterialFolder });
            Assert.GreaterOrEqual(guids.Length, 7, "The Chapter 1 material family must be committed.");

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(material, path + " must load.");
                Assert.IsTrue(material.HasProperty("_BaseColor"), path + " must expose _BaseColor.");

                Color c = material.GetColor("_BaseColor");
                Color.RGBToHSV(c, out _, out float saturation, out float value);

                Assert.LessOrEqual(saturation, 0.60f,
                    material.name + " is too saturated for background dressing (" + saturation + ").");
                Assert.LessOrEqual(value, 0.62f,
                    material.name + " is too bright for background dressing (" + value + ").");

                // A near-neutral colour can never compete with a saturated actor,
                // however close its raw RGB triple happens to sit.
                if (saturation < 0.30f)
                {
                    continue;
                }

                AssertFarFrom(material, c, ProjectileYellow, "the yellow projectile");
                AssertFarFrom(material, c, RunnerOrange, "the orange Runner");
                AssertFarFrom(material, c, InfectedGreen, "the green Basic Infected");
            }
        }

        private static void AssertFarFrom(Material material, Color c, Color actor, string label)
        {
            float distance = Mathf.Sqrt(
                (c.r - actor.r) * (c.r - actor.r) +
                (c.g - actor.g) * (c.g - actor.g) +
                (c.b - actor.b) * (c.b - actor.b));

            Assert.GreaterOrEqual(distance, 0.35f,
                material.name + " sits too close to " + label + " (distance " + distance + ").");
        }

        [Test]
        public void QuarantineAccentSurvivesOnlyOnSmallElements()
        {
            Material accent = AssetDatabase.LoadAssetAtPath<Material>(AccentMaterialPath);
            Assert.IsNotNull(accent, "The quarantine accent material must still exist.");

            using (SceneScope scope = new SceneScope())
            {
                GameObject outskirts = RequireOutskirts(scope.Scene);
                MeshRenderer[] renderers = outskirts.GetComponentsInChildren<MeshRenderer>(true);

                int accentUses = 0;
                List<string> offenders = new List<string>();

                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i].sharedMaterial != accent)
                    {
                        continue;
                    }

                    accentUses++;
                    Bounds b = renderers[i].bounds;
                    if (b.size.x > 3f)
                    {
                        offenders.Add(renderers[i].name + " (width " + b.size.x + ")");
                    }
                }

                Assert.Greater(accentUses, 0,
                    "The quarantine accent must still identify the checkpoint family.");
                Assert.IsEmpty(offenders,
                    "The quarantine accent must stay on small elements only - large red/orange " +
                    "surfaces were the rejected look: " + string.Join(" | ", offenders));
            }
        }

        // ------------------------------------------------- section differentiation

        [Test]
        public void ThreeSectionGroupsAreAuthoredAndDistinct()
        {
            using (SceneScope scope = new SceneScope())
            {
                GameObject s1 = FindNamed(scope.Scene, "Section_01_Checkpoint");
                GameObject s2 = FindNamed(scope.Scene, "Section_02_Abandoned");
                GameObject s3 = FindNamed(scope.Scene, "Section_03_Compromised");

                Assert.IsNotNull(s1, "Section 1 dressing group must exist.");
                Assert.IsNotNull(s2, "Section 2 dressing group must exist.");
                Assert.IsNotNull(s3, "Section 3 dressing group must exist.");

                List<string> m1 = ModuleNamesUnder(s1);
                List<string> m2 = ModuleNamesUnder(s2);
                List<string> m3 = ModuleNamesUnder(s3);

                Assert.Greater(m1.Count, 8, "Section 1 must be dressed.");
                Assert.Greater(m2.Count, 8, "Section 2 must be dressed.");
                Assert.Greater(m3.Count, 8, "Section 3 must be dressed.");

                // Section 1 = intact evacuation checkpoint.
                Assert.Greater(CountModules(m1, "C1_Structure_GuardBooth"), 0,
                    "Section 1 must read as a manned checkpoint (guard booth).");
                Assert.AreEqual(0, CountModules(m1, "C1_Structure_WreckedCar",
                        "C1_Structure_Container", "C1_Prop_TankTrap", "C1_Prop_RubbleMound"),
                    "Section 1 must stay relatively intact - no wrecks, containers, " +
                    "tank traps or collapse mounds.");

                // Section 2 = damaged and abandoned.
                Assert.Greater(CountModules(m2, "C1_Structure_WreckedCar"), 0,
                    "Section 2 must read as abandoned (wrecked vehicles).");
                Assert.AreEqual(0, CountModules(m2, "C1_Structure_GuardBooth"),
                    "Section 2 must not repeat the intact Section 1 checkpoint structure.");

                // Section 3 = heavily compromised.
                Assert.Greater(CountModules(m3, "C1_Structure_Container"), 0,
                    "Section 3 must narrow the funnel with heavy structures.");
                Assert.Greater(CountModules(m3, "C1_Prop_TankTrap"), 2,
                    "Section 3 must read as a fortified, compromised final approach.");
            }
        }

        [Test]
        public void DamageEscalatesTowardsTheFinalApproach()
        {
            using (SceneScope scope = new SceneScope())
            {
                string[] damage =
                {
                    "C1_Prop_Debris", "C1_Prop_RubbleMound", "C1_Prop_TankTrap",
                    "C1_Structure_WreckedCar", "C1_Structure_Container"
                };

                GameObject g1 = FindNamed(scope.Scene, "Section_01_Checkpoint");
                GameObject g2 = FindNamed(scope.Scene, "Section_02_Abandoned");
                GameObject g3 = FindNamed(scope.Scene, "Section_03_Compromised");

                Assert.IsNotNull(g1, "Section 1 dressing group must exist.");
                Assert.IsNotNull(g2, "Section 2 dressing group must exist.");
                Assert.IsNotNull(g3, "Section 3 dressing group must exist.");

                int d1 = CountModules(ModuleNamesUnder(g1), damage);
                int d2 = CountModules(ModuleNamesUnder(g2), damage);
                int d3 = CountModules(ModuleNamesUnder(g3), damage);

                Assert.Less(d1, d2,
                    "Section 2 must be visibly more damaged than Section 1 (" + d1 + " vs " + d2 + ").");
                Assert.Less(d2, d3,
                    "Section 3 must be visibly more damaged than Section 2 (" + d2 + " vs " + d3 + ").");
            }
        }

        [Test]
        public void FinalRoadblockIsALayeredDramaticLandmark()
        {
            GameObject roadblock = AssetDatabase.LoadAssetAtPath<GameObject>(
                KitFolder + "/C1_Landmark_FinalRoadblock.prefab");
            Assert.IsNotNull(roadblock, "The final roadblock landmark must exist.");

            MeshRenderer[] renderers = roadblock.GetComponentsInChildren<MeshRenderer>(true);
            Assert.GreaterOrEqual(renderers.Length, 30,
                "The final roadblock was rejected as visually weak - it must now be a " +
                "layered landmark, not a wall plus a stripe (" + renderers.Length + " parts).");

            float highest = 0f;
            HashSet<Material> distinct = new HashSet<Material>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Transform t = renderers[i].transform;
                highest = Mathf.Max(highest, t.localPosition.y + (t.localScale.y * 0.5f));
                if (renderers[i].sharedMaterial != null)
                {
                    distinct.Add(renderers[i].sharedMaterial);
                }
            }

            Assert.GreaterOrEqual(highest, 6f,
                "The finale needs real vertical drama (tallest element " + highest + ").");
            Assert.GreaterOrEqual(distinct.Count, 5,
                "The finale must be built from a varied material set, not one grey box.");
        }

        // ------------------------------------------------- kit growth / stability

        [Test]
        public void OriginalKitGuidsArePreserved()
        {
            for (int i = 0; i < OriginalKitGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(OriginalKitGuids[i]);
                Assert.IsFalse(string.IsNullOrEmpty(path),
                    "Original kit GUID " + OriginalKitGuids[i] + " must still resolve.");
                Assert.IsTrue(path.StartsWith(KitFolder),
                    "Original kit GUID " + OriginalKitGuids[i] + " must stay in the kit folder.");

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path + " must load as a prefab.");
                Assert.AreEqual(PrefabAssetType.Regular, PrefabUtility.GetPrefabAssetType(prefab),
                    path + " must remain a regular prefab.");
            }
        }

        [Test]
        public void DressingLibraryGrewWithFinishedStructureModules()
        {
            MissionDefinition mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>(
                "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_01.asset");
            Assert.IsNotNull(mission, "Mission 01 must exist.");

            MissionEnvironmentDefinition profile = mission.Environment;
            Assert.IsNotNull(profile, "Mission 01 must reference the Outskirts profile.");

            HashSet<string> library = new HashSet<string>();
            for (int i = 0; i < profile.SideDressingPrefabs.Count; i++)
            {
                GameObject module = profile.SideDressingPrefabs[i];
                Assert.IsNotNull(module, "Dressing entry " + i + " must resolve.");
                library.Add(module.name);
            }

            Assert.GreaterOrEqual(library.Count, 15,
                "The roadside kit was rejected as unfinished - the library must offer " +
                "enough distinct modules to build believable roadsides.");

            string[] required =
            {
                "C1_Barrier_Concrete", "C1_Barrier_Checkpoint", "C1_Prop_Debris",
                "C1_Prop_Crate", "C1_Prop_Cone",
                "C1_Prop_Sandbags", "C1_Prop_TankTrap", "C1_Prop_WarningSign",
                "C1_Prop_Floodlight", "C1_Prop_RubbleMound",
                "C1_Structure_GuardBooth", "C1_Structure_WreckedCar", "C1_Structure_Container"
            };

            for (int i = 0; i < required.Length; i++)
            {
                Assert.IsTrue(library.Contains(required[i]),
                    "The dressing library must register '" + required[i] + "'.");
            }
        }

        [Test]
        public void EveryKitModuleIsBuiltFromMultipleShapes()
        {
            // "Roadside structures look unfinished" - a kit module must never again be
            // a single untextured box.
            string[] prefabs = Directory.GetFiles(KitFolder, "*.prefab");
            Assert.GreaterOrEqual(prefabs.Length, 20, "The expanded kit must be committed.");

            for (int i = 0; i < prefabs.Length; i++)
            {
                string path = prefabs[i].Replace("\\", "/");
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path + " must load.");

                MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                Assert.GreaterOrEqual(renderers.Length, 5,
                    Path.GetFileName(path) + " must be assembled from several shapes " +
                    "(found " + renderers.Length + ").");

                HashSet<Material> distinct = new HashSet<Material>();
                for (int j = 0; j < renderers.Length; j++)
                {
                    Assert.IsNotNull(renderers[j].sharedMaterial,
                        Path.GetFileName(path) + " has an unassigned material.");
                    distinct.Add(renderers[j].sharedMaterial);
                }

                Assert.GreaterOrEqual(distinct.Count, 2,
                    Path.GetFileName(path) + " must use more than one material to read as " +
                    "a finished object.");
            }
        }
    }
}
