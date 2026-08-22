using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OperationOutbreak.EditorTools;
using OperationOutbreak.Enemies;
using OperationOutbreak.Environment;
using OperationOutbreak.Mission;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1X - EditMode regression tests for the committed Chapter 1 progression
    /// foundation. They load the REAL Chapter_01 asset and the 10 committed MissionDefinition
    /// assets (and the environment profile) and pin:
    ///
    ///   * Chapter 1 contains EXACTLY 10 mission definitions.
    ///   * Mission ids are unique and numbers are sequential 1..10.
    ///   * Every mission has a valid environment reference, objective and reward config.
    ///   * The chapter validates cleanly end-to-end.
    ///   * Progression behaves correctly against the REAL chapter (M1 unlocked, sequential
    ///     unlock through M10, no Mission 11 access, replayable, no-regress, locked cannot be
    ///     selected/started, save/load round-trip, reset).
    ///   * The existing mission/environment architecture stays compatible (Mission 01's
    ///     verified shape, the environment profile, the scene wiring, and no per-mission
    ///     controller duplication).
    ///
    /// Progression here uses an isolated in-memory JSON store so the tests never touch real
    /// PlayerPrefs; the pure-logic edge cases are covered by MissionProgressionTests.
    /// </summary>
    public sealed class Chapter1MissionTests
    {
        private const string ChapterAssetPath =
            "Assets/_OperationOutbreak/Resources/ChapterDefinitions/Chapter_01.asset";

        private const string EnvironmentAssetPath =
            "Assets/_OperationOutbreak/Resources/EnvironmentProfiles/C1_OutbreakOutskirts.asset";

        private const string ScenePath =
            "Assets/_OperationOutbreak/Scenes/Gameplay_Prototype.unity";

        private const string MissionDefinitionsFolder =
            "Assets/_OperationOutbreak/Resources/MissionDefinitions";

        [TearDown]
        public void TearDown()
        {
            ActiveMissionContext.Clear();
            MissionProgressionService.InvalidateDefaultCache();
        }

        // ---------------------------------------------------------------- helpers

        private static ChapterDefinition LoadChapter()
        {
            ChapterDefinition chapter = AssetDatabase.LoadAssetAtPath<ChapterDefinition>(ChapterAssetPath);
            Assert.IsNotNull(chapter, "Chapter_01 must exist at " + ChapterAssetPath + ".");
            return chapter;
        }

        private static HashSet<string> KnownArchetypeIds()
        {
            HashSet<string> ids = new HashSet<string>();
            foreach (EnemyArchetypeDefinition archetype in
                     EnemyArchetypeEditorTools.LoadAllArchetypeDefinitions())
            {
                if (archetype != null && !string.IsNullOrEmpty(archetype.ArchetypeId))
                {
                    ids.Add(archetype.ArchetypeId);
                }
            }

            return ids;
        }

        private sealed class JsonMemoryStore : IMissionProgressionStore
        {
            private string _json = string.Empty;

            public MissionProgressionSave Load()
            {
                if (string.IsNullOrEmpty(_json))
                {
                    return MissionProgressionSave.CreateEmpty();
                }

                try
                {
                    return JsonUtility.FromJson<MissionProgressionSave>(_json)
                        ?? MissionProgressionSave.CreateEmpty();
                }
                catch
                {
                    return MissionProgressionSave.CreateEmpty();
                }
            }

            public void Save(MissionProgressionSave save)
            {
                _json = JsonUtility.ToJson(save ?? MissionProgressionSave.CreateEmpty());
            }

            public void Delete()
            {
                _json = string.Empty;
            }
        }

        // ============================================================ chapter structure

        [Test]
        public void Chapter1ContainsExactlyTenMissions()
        {
            ChapterDefinition chapter = LoadChapter();

            Assert.AreEqual(10, chapter.MissionCount,
                "Chapter 1 must contain exactly 10 mission definitions.");
        }

        [Test]
        public void Chapter1MissionIdsAreUnique()
        {
            ChapterDefinition chapter = LoadChapter();
            HashSet<string> ids = new HashSet<string>();

            for (int i = 0; i < chapter.MissionCount; i++)
            {
                MissionDefinition mission = chapter.GetMission(i);
                Assert.IsNotNull(mission, "Mission slot " + (i + 1) + " must not be null.");
                Assert.IsTrue(ids.Add(mission.MissionId),
                    "Duplicate mission id '" + mission.MissionId + "' in Chapter 1.");
            }
        }

        [Test]
        public void Chapter1MissionNumbersAreSequentialOneToTen()
        {
            ChapterDefinition chapter = LoadChapter();

            for (int i = 0; i < chapter.MissionCount; i++)
            {
                MissionDefinition mission = chapter.GetMission(i);
                Assert.AreEqual(i + 1, mission.MissionNumber,
                    "Mission slot " + (i + 1) + " must carry mission number " + (i + 1) +
                    " (got " + mission.MissionNumber + ").");
                Assert.AreEqual(1, mission.ChapterNumber,
                    "Every Chapter 1 mission must report chapter number 1.");
            }
        }

        [Test]
        public void Chapter1MissionIdsAreTheExpectedStableIds()
        {
            ChapterDefinition chapter = LoadChapter();

            for (int i = 0; i < chapter.MissionCount; i++)
            {
                MissionDefinition mission = chapter.GetMission(i);
                Assert.AreEqual("mission_" + (i + 1).ToString("00"), mission.MissionId,
                    "Mission " + (i + 1) + " must use the stable id mission_" +
                    (i + 1).ToString("00") + ".");
            }
        }

        [Test]
        public void Chapter1DifficultyEscalatesAcrossMissions()
        {
            ChapterDefinition chapter = LoadChapter();
            int previous = -1;

            for (int i = 0; i < chapter.MissionCount; i++)
            {
                MissionDefinition mission = chapter.GetMission(i);
                int total = mission.TotalEnemyCount;
                Assert.Greater(total, previous,
                    "Mission " + (i + 1) + " total enemies (" + total +
                    ") must exceed the previous mission's (" + previous + ").");
                previous = total;
            }

            // Mission 10 is the hardest configuration using existing systems.
            Assert.Greater(chapter.GetMission(9).TotalEnemyCount,
                chapter.GetMission(0).TotalEnemyCount);
        }

        // ============================================================ per-mission validity (environment / objective / reward)

        [Test]
        public void EveryChapter1MissionHasAValidEnvironmentReference()
        {
            ChapterDefinition chapter = LoadChapter();
            MissionEnvironmentDefinition profile =
                AssetDatabase.LoadAssetAtPath<MissionEnvironmentDefinition>(EnvironmentAssetPath);
            Assert.IsNotNull(profile, "The C1 environment profile must exist.");

            for (int i = 0; i < chapter.MissionCount; i++)
            {
                MissionDefinition mission = chapter.GetMission(i);
                Assert.IsNotNull(mission.Environment,
                    "Mission " + (i + 1) + " must reference an environment profile.");
                // Chapter 1 currently reuses the single Outskirts profile for every mission.
                Assert.AreSame(profile, mission.Environment,
                    "Mission " + (i + 1) + " must reference the Chapter 1 Outskirts environment profile.");
            }
        }

        [Test]
        public void EveryChapter1MissionHasValidObjectiveAndRewardConfiguration()
        {
            ChapterDefinition chapter = LoadChapter();
            HashSet<string> known = KnownArchetypeIds();

            for (int i = 0; i < chapter.MissionCount; i++)
            {
                MissionDefinition mission = chapter.GetMission(i);

                List<string> problems = MissionDefinition.CollectProblems(mission, known);
                Assert.IsEmpty(problems,
                    "Mission " + (i + 1) + " (" + mission.name + ") must validate cleanly: " +
                    string.Join(" | ", problems));

                Assert.GreaterOrEqual(mission.RequiredObjectiveCount, 1,
                    "Mission " + (i + 1) + " must have at least one required objective.");
                Assert.IsNotNull(mission.Reward,
                    "Mission " + (i + 1) + " must carry a reward definition.");
                Assert.GreaterOrEqual(mission.Reward.coins, 0);
                Assert.GreaterOrEqual(mission.Reward.supplies, 0);
            }
        }

        [Test]
        public void Chapter1ValidatesCleanlyEndToEnd()
        {
            ChapterDefinition chapter = LoadChapter();
            List<string> problems = ChapterDefinition.CollectProblems(chapter, KnownArchetypeIds());

            Assert.IsEmpty(problems,
                "Chapter 1 must validate cleanly end-to-end: " + string.Join(" | ", problems));
        }

        [Test]
        public void ValidateAllChapterDefinitionsToolPasses()
        {
            bool valid = ChapterDefinitionEditorTools.ValidateAll(out List<string> problems);

            Assert.IsTrue(valid,
                "The chapter validation tool must pass for all chapters: " + string.Join(" | ", problems));
        }

        // ============================================================ progression against the REAL Chapter 1

        [Test]
        public void Chapter1Mission1IsUnlockedByDefaultAndOthersAreLocked()
        {
            MissionProgressionService service =
                new MissionProgressionService(LoadChapter(), new JsonMemoryStore());

            Assert.IsTrue(service.IsUnlocked(service.GetMission(0)));
            for (int i = 1; i < service.MissionCount; i++)
            {
                Assert.IsFalse(service.IsUnlocked(service.GetMission(i)),
                    "Mission " + (i + 1) + " must be locked before its predecessor is completed.");
            }
        }

        [Test]
        public void Chapter1SequentialCompletionUnlocksThroughMission10()
        {
            MissionProgressionService service =
                new MissionProgressionService(LoadChapter(), new JsonMemoryStore());

            for (int i = 0; i < service.MissionCount; i++)
            {
                Assert.IsTrue(service.IsUnlocked(service.GetMission(i)),
                    "Mission " + (i + 1) + " must be unlocked when reached.");
                Assert.IsTrue(service.MarkCompleted(service.GetMission(i)));
            }

            Assert.IsTrue(service.IsCompleted(service.GetMission(9)));
            Assert.AreEqual(10, service.CompletedCount);

            // Completing Mission 10 yields no Mission 11 by any access path.
            Assert.IsNull(service.GetNextMission(service.GetMission(9)));
            Assert.IsNull(service.GetMission(10));
        }

        [Test]
        public void Chapter1LockedMissionsCannotBeSelectedOrStarted()
        {
            MissionSelectionService selection =
                new MissionSelectionService(
                    new MissionProgressionService(LoadChapter(), new JsonMemoryStore()));

            // Only Mission 1 is unlocked; selecting Mission 5 must fail.
            Assert.IsFalse(selection.Select(selection.Progression.GetMission(4)));
            Assert.IsFalse(selection.HasSelection);
            Assert.IsFalse(selection.StartSelected());
            Assert.IsFalse(ActiveMissionContext.HasCurrent);

            // Selecting the unlocked Mission 1 then starting succeeds.
            Assert.IsTrue(selection.Select(selection.Progression.GetMission(0)));
            Assert.IsTrue(selection.StartSelected());
            Assert.AreSame(selection.Progression.GetMission(0), ActiveMissionContext.Current);
        }

        [Test]
        public void Chapter1SaveLoadRoundTripPreservesProgress()
        {
            JsonMemoryStore store = new JsonMemoryStore();
            ChapterDefinition chapter = LoadChapter();

            MissionProgressionService first = new MissionProgressionService(chapter, store);
            for (int i = 0; i < 5; i++)
            {
                first.MarkCompleted(chapter.GetMission(i));
            }

            MissionProgressionService second = new MissionProgressionService(chapter, store);
            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(second.IsCompleted(chapter.GetMission(i)));
            }

            Assert.IsTrue(second.IsUnlocked(chapter.GetMission(5)));
            Assert.IsFalse(second.IsUnlocked(chapter.GetMission(6)));
        }

        [Test]
        public void Chapter1ResetClearsAllProgress()
        {
            JsonMemoryStore store = new JsonMemoryStore();
            ChapterDefinition chapter = LoadChapter();
            MissionProgressionService service = new MissionProgressionService(chapter, store);

            service.MarkCompleted(chapter.GetMission(0));
            service.MarkCompleted(chapter.GetMission(1));

            service.Reset();

            Assert.AreEqual(0, service.CompletedCount);
            Assert.IsFalse(service.IsCompleted(chapter.GetMission(0)));
            Assert.IsTrue(service.IsUnlocked(chapter.GetMission(0)));
            Assert.IsFalse(service.IsUnlocked(chapter.GetMission(1)));
        }

        [Test]
        public void Chapter1ReplayingAnEarlierMissionDoesNotReduceLaterProgress()
        {
            JsonMemoryStore store = new JsonMemoryStore();
            ChapterDefinition chapter = LoadChapter();
            MissionProgressionService service = new MissionProgressionService(chapter, store);

            for (int i = 0; i < 5; i++)
            {
                service.MarkCompleted(chapter.GetMission(i));
            }

            Assert.IsFalse(service.MarkCompleted(chapter.GetMission(0)));

            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(service.IsCompleted(chapter.GetMission(i)));
            }

            Assert.IsTrue(service.IsUnlocked(chapter.GetMission(5)));
        }

        // ============================================================ existing architecture compatibility

        [Test]
        public void Mission01KeepsItsVerifiedPrototypeShape()
        {
            ChapterDefinition chapter = LoadChapter();
            MissionDefinition mission01 = chapter.GetMission(0);

            Assert.AreEqual("mission_01", mission01.MissionId);
            Assert.AreEqual(3, mission01.SectionCount, "Mission 01 must keep its 3 sections.");
            Assert.AreEqual(12, mission01.TotalEnemyCount, "Mission 01 must keep its 12 enemies.");
            Assert.AreEqual(9, mission01.GetArchetypeCount(MissionDefinition.BasicArchetypeId));
            Assert.AreEqual(3, mission01.GetArchetypeCount(MissionDefinition.RunnerArchetypeId));
        }

        [Test]
        public void Chapter1EnvironmentProfileIsValid()
        {
            MissionEnvironmentDefinition profile =
                AssetDatabase.LoadAssetAtPath<MissionEnvironmentDefinition>(EnvironmentAssetPath);

            Assert.IsNotNull(profile);
            List<string> problems = MissionEnvironmentDefinition.CollectProblems(profile);
            Assert.IsEmpty(problems,
                "The C1 environment profile must still validate cleanly: " +
                string.Join(" | ", problems));
        }

        [Test]
        public void ExactlyTenMissionDefinitionAssetsExist()
        {
            // The Resources/MissionDefinitions folder must hold exactly Mission_01..Mission_10.
            string[] guids = AssetDatabase.FindAssets("t:MissionDefinition", new[] { MissionDefinitionsFolder });
            Assert.AreEqual(10, guids.Length,
                "There must be exactly 10 MissionDefinition assets under " + MissionDefinitionsFolder + ".");
        }

        [Test]
        public void GameplaySceneWiresMission01ToAllThreeConsumersAndHostsTheMissionSystem()
        {
            Assert.IsTrue(File.Exists(ScenePath), "Gameplay scene must exist at " + ScenePath + ".");
            string scene = File.ReadAllText(ScenePath);

            string mission01Guid = AssetDatabase.AssetPathToGUID(
                "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_01.asset");

            // The three existing mission consumers must still reference Mission 01 as their
            // serialized default (the active-mission routing only ever overrides this).
            int wired = CountOccurrences(scene,
                "missionDefinition: {fileID: 11400000, guid: " + mission01Guid);
            Assert.GreaterOrEqual(wired, 3,
                "MissionSectionController, MissionObjectiveController and MissionRewardService " +
                "must all still reference Mission_01 as their serialized default.");

            // The new MissionSystem object must host the runtime assignment (early-Awake) and
            // the progression recorder so a started mission becomes authoritative and its
            // completion is recorded.
            string assignmentGuid = AssetDatabase.AssetPathToGUID(
                "Assets/_OperationOutbreak/Scripts/Mission/MissionRuntimeAssignment.cs");
            string recorderGuid = AssetDatabase.AssetPathToGUID(
                "Assets/_OperationOutbreak/Scripts/Mission/MissionProgressionRecorder.cs");

            Assert.IsTrue(scene.Contains("guid: " + assignmentGuid),
                "The gameplay scene must host a MissionRuntimeAssignment so the selected mission becomes authoritative.");
            Assert.IsTrue(scene.Contains("guid: " + recorderGuid),
                "The gameplay scene must host a MissionProgressionRecorder so completion is recorded.");
        }

        [Test]
        public void NoPerMissionControllerDuplicationWasIntroduced()
        {
            // The 1T architectural rule still holds: exactly one mission-flow controller and
            // no Mission1Controller/Mission2Controller. The new 1X systems are data/services,
            // not per-mission controllers.
            System.Reflection.Assembly assembly = typeof(MissionSectionController).Assembly;
            System.Type[] types = assembly.GetTypes();

            int flowControllers = 0;
            foreach (System.Type type in types)
            {
                if (type.Namespace == "OperationOutbreak.Mission" &&
                    type.Name.EndsWith("MissionSectionController"))
                {
                    flowControllers++;
                }
            }

            Assert.AreEqual(1, flowControllers, "Exactly ONE mission-flow controller may exist.");
            Assert.IsNull(assembly.GetType("OperationOutbreak.Mission.Mission1Controller"));
            Assert.IsNull(assembly.GetType("OperationOutbreak.Mission.Mission2Controller"));
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }
    }
}
