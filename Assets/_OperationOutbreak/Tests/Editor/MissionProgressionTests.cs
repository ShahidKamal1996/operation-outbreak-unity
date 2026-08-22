using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.Mission;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1X - EditMode tests for the mission PROGRESSION + SELECTION logic.
    ///
    /// These exercise the pure, asset-independent contract of:
    ///   * MissionProgression        - the add-only completed-id set
    ///   * MissionProgressionService - sequential unlock derivation + persistence facade
    ///   * MissionSelectionService   - select/start an unlocked mission
    ///   * ActiveMissionContext      - the selected-mission handoff
    ///
    /// They build an in-memory 10-mission chapter (mirroring Chapter 1's shape) and an
    /// in-memory JSON store, so they are deterministic, isolated from real PlayerPrefs and
    /// independent of the committed assets (the committed Chapter 1 asset is covered by
    /// Chapter1MissionTests). No scene, no gameplay.
    /// </summary>
    public sealed class MissionProgressionTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            // ActiveMissionContext is static; never let one test's selection leak into another.
            ActiveMissionContext.Clear();
            MissionProgressionService.InvalidateDefaultCache();

            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        // ---------------------------------------------------------------- helpers

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Field '" + name + "' missing on " + target.GetType().Name + ".");
            field.SetValue(target, value);
        }

        private MissionDefinition BuildMission(int n)
        {
            MissionDefinition mission = ScriptableObject.CreateInstance<MissionDefinition>();
            SetField(mission, "missionId", "mission_" + n.ToString("00"));
            SetField(mission, "missionNumber", n);
            SetField(mission, "chapterNumber", 1);
            SetField(mission, "displayName", "Mission " + n);
            _created.Add(mission);
            return mission;
        }

        private ChapterDefinition BuildChapter(int count)
        {
            ChapterDefinition chapter = ScriptableObject.CreateInstance<ChapterDefinition>();
            SetField(chapter, "chapterId", "chapter_test");
            SetField(chapter, "chapterNumber", 1);
            SetField(chapter, "displayName", "Test Chapter");

            List<MissionDefinition> missions = new List<MissionDefinition>();
            for (int i = 1; i <= count; i++)
            {
                missions.Add(BuildMission(i));
            }

            SetField(chapter, "missions", missions);
            _created.Add(chapter);
            return chapter;
        }

        /// <summary>
        /// In-memory store that round-trips through the SAME JSON format
        /// PlayerPrefsMissionProgressionStore uses, so a passing round-trip here proves the
        /// persistence format survives serialization without touching real player data.
        /// </summary>
        private sealed class JsonMemoryStore : IMissionProgressionStore
        {
            private string _json = string.Empty;
            public int SaveCalls;
            public int LoadCalls;

            public MissionProgressionSave Load()
            {
                LoadCalls++;
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
                SaveCalls++;
                _json = JsonUtility.ToJson(save ?? MissionProgressionSave.CreateEmpty());
            }

            public void Delete()
            {
                _json = string.Empty;
            }
        }

        private static MissionDefinition Mission(MissionProgressionService service, int oneBased)
        {
            MissionDefinition m = service.GetMission(oneBased - 1);
            Assert.IsNotNull(m, "Mission " + oneBased + " must exist in the test chapter.");
            return m;
        }

        // ============================================================ MissionProgression (pure set)

        [Test]
        public void CompletedSetIsInitiallyEmpty()
        {
            MissionProgression progression = new MissionProgression();

            Assert.AreEqual(0, progression.CompletedCount);
            Assert.IsFalse(progression.IsCompleted("mission_01"));
        }

        [Test]
        public void MarkCompletedRecordsAndIsIdempotent()
        {
            MissionProgression progression = new MissionProgression();

            Assert.IsTrue(progression.MarkCompleted("mission_01"), "First completion must be added.");
            Assert.IsFalse(progression.MarkCompleted("mission_01"), "Re-completing must be a no-op.");
            Assert.IsTrue(progression.IsCompleted("mission_01"));
            Assert.AreEqual(1, progression.CompletedCount);
        }

        [Test]
        public void MarkCompletedRejectsNullOrEmptyIds()
        {
            MissionProgression progression = new MissionProgression();

            Assert.IsFalse(progression.MarkCompleted(null));
            Assert.IsFalse(progression.MarkCompleted(string.Empty));
            Assert.AreEqual(0, progression.CompletedCount);
        }

        [Test]
        public void ClearRemovesAllCompletion()
        {
            MissionProgression progression = new MissionProgression();
            progression.MarkCompleted("mission_01");
            progression.MarkCompleted("mission_02");

            progression.Clear();

            Assert.AreEqual(0, progression.CompletedCount);
            Assert.IsFalse(progression.IsCompleted("mission_01"));
        }

        [Test]
        public void RestoreSkipsNullOrEmptyEntries()
        {
            MissionProgression progression = new MissionProgression();

            progression.Restore(new List<string> { "mission_01", null, string.Empty, "mission_03" });

            Assert.IsTrue(progression.IsCompleted("mission_01"));
            Assert.IsTrue(progression.IsCompleted("mission_03"));
            Assert.AreEqual(2, progression.CompletedCount);
        }

        // ============================================================ sequential unlock (the core rule)

        [Test]
        public void Mission1IsUnlockedByDefaultAndMission2IsLockedBeforeCompletion()
        {
            MissionProgressionService service =
                new MissionProgressionService(BuildChapter(10), new JsonMemoryStore());

            Assert.IsTrue(service.IsUnlocked(Mission(service, 1)), "Mission 1 must be unlocked by default.");
            Assert.IsFalse(service.IsUnlocked(Mission(service, 2)),
                "Mission 2 must be locked until Mission 1 is completed.");
            Assert.IsFalse(service.IsUnlocked(Mission(service, 10)),
                "Mission 10 must be locked initially.");
        }

        [Test]
        public void CompletingMission1UnlocksMission2()
        {
            MissionProgressionService service =
                new MissionProgressionService(BuildChapter(10), new JsonMemoryStore());

            Assert.IsTrue(service.MarkCompleted(Mission(service, 1)));

            Assert.IsTrue(service.IsCompleted(Mission(service, 1)));
            Assert.IsTrue(service.IsUnlocked(Mission(service, 2)),
                "Completing Mission 1 must unlock Mission 2.");
            Assert.IsFalse(service.IsUnlocked(Mission(service, 3)),
                "Mission 3 must still be locked after only Mission 1.");
        }

        [Test]
        public void SequentialCompletionUnlocksThroughMission10()
        {
            MissionProgressionService service =
                new MissionProgressionService(BuildChapter(10), new JsonMemoryStore());

            for (int i = 1; i <= 10; i++)
            {
                Assert.IsTrue(service.IsUnlocked(Mission(service, i)),
                    "Mission " + i + " must be unlocked after its predecessor was completed.");
                Assert.IsTrue(service.MarkCompleted(Mission(service, i)),
                    "Mission " + i + " must be newly recorded completed.");
            }

            Assert.AreEqual(10, service.CompletedCount);
            Assert.IsTrue(service.IsCompleted(Mission(service, 10)));
        }

        [Test]
        public void CompletingMission10ProducesNoMission11Access()
        {
            ChapterDefinition chapter = BuildChapter(10);
            MissionProgressionService service =
                new MissionProgressionService(chapter, new JsonMemoryStore());

            for (int i = 1; i <= 10; i++)
            {
                service.MarkCompleted(Mission(service, i));
            }

            // No 11th mission exists by any access path.
            Assert.AreEqual(10, service.MissionCount);
            Assert.IsNull(service.GetMission(10), "Index 10 (11th mission) must not exist.");
            Assert.IsNull(chapter.GetMission(10));
            Assert.IsNull(service.GetNextMission(Mission(service, 10)),
                "Completing Mission 10 must not surface a Mission 11.");
            Assert.IsNull(chapter.GetNextMission(Mission(service, 10)));

            // Completing Mission 10 again is a safe no-op (no crash, no progress loss).
            Assert.IsFalse(service.MarkCompleted(Mission(service, 10)));
            Assert.IsTrue(service.IsCompleted(Mission(service, 10)));
        }

        [Test]
        public void CompletedMissionsRemainReplayable()
        {
            MissionProgressionService service =
                new MissionProgressionService(BuildChapter(10), new JsonMemoryStore());

            service.MarkCompleted(Mission(service, 1));
            service.MarkCompleted(Mission(service, 2));

            // A completed mission stays UNLOCKED (its predecessor is completed), so it can be
            // replayed at will.
            Assert.IsTrue(service.IsUnlocked(Mission(service, 1)));
            Assert.IsTrue(service.IsUnlocked(Mission(service, 2)));
            Assert.IsTrue(service.IsCompleted(Mission(service, 1)));
        }

        [Test]
        public void CompletingAnEarlierMissionDoesNotReduceLaterProgress()
        {
            MissionProgressionService service =
                new MissionProgressionService(BuildChapter(10), new JsonMemoryStore());

            // Earn progress up to Mission 5.
            for (int i = 1; i <= 5; i++)
            {
                service.MarkCompleted(Mission(service, i));
            }

            Assert.IsTrue(service.IsCompleted(Mission(service, 5)));
            Assert.IsTrue(service.IsUnlocked(Mission(service, 6)));

            // Re-complete an earlier mission (replay) and a mid mission.
            Assert.IsFalse(service.MarkCompleted(Mission(service, 1)), "Replay is a no-op add.");
            Assert.IsFalse(service.MarkCompleted(Mission(service, 3)), "Replay is a no-op add.");

            // Later progress is fully preserved.
            for (int i = 1; i <= 5; i++)
            {
                Assert.IsTrue(service.IsCompleted(Mission(service, i)),
                    "Mission " + i + " completion must survive replaying an earlier mission.");
            }

            Assert.IsTrue(service.IsUnlocked(Mission(service, 6)));
            Assert.IsFalse(service.IsUnlocked(Mission(service, 7)));
            Assert.AreEqual(5, service.CompletedCount);
        }

        [Test]
        public void MissionNotInChapterIsNeverUnlocked()
        {
            MissionProgressionService service =
                new MissionProgressionService(BuildChapter(10), new JsonMemoryStore());
            MissionDefinition stranger = BuildMission(99);

            Assert.IsFalse(service.IsUnlocked(stranger));
            Assert.IsFalse(service.IsCompleted(stranger));
        }

        // ============================================================ persistence (save/load round-trip + reset)

        [Test]
        public void SaveLoadRoundTripPreservesProgress()
        {
            JsonMemoryStore store = new JsonMemoryStore();
            ChapterDefinition chapter = BuildChapter(10);

            MissionProgressionService first =
                new MissionProgressionService(chapter, store);

            for (int i = 1; i <= 5; i++)
            {
                first.MarkCompleted(Mission(first, i));
            }

            Assert.Greater(store.SaveCalls, 0, "Each completion must persist.");

            // A brand-new service loading from the SAME store must observe the saved progress.
            MissionProgressionService second =
                new MissionProgressionService(chapter, store);

            for (int i = 1; i <= 5; i++)
            {
                Assert.IsTrue(second.IsCompleted(Mission(second, i)),
                    "Mission " + i + " must survive a save/load round-trip.");
            }

            Assert.IsTrue(second.IsUnlocked(Mission(second, 6)),
                "Unlock state must be derivable from round-tripped completion.");
            Assert.IsFalse(second.IsUnlocked(Mission(second, 7)));
            Assert.AreEqual(5, second.CompletedCount);
        }

        [Test]
        public void EmptySaveLoadRoundTripYieldsEmptyProgress()
        {
            JsonMemoryStore store = new JsonMemoryStore();
            MissionProgressionService service =
                new MissionProgressionService(BuildChapter(10), store);

            Assert.AreEqual(0, service.CompletedCount);
            Assert.IsFalse(service.IsCompleted("mission_01"));
        }

        [Test]
        public void ResetClearsAllProgressAndPersists()
        {
            JsonMemoryStore store = new JsonMemoryStore();
            MissionProgressionService service =
                new MissionProgressionService(BuildChapter(10), store);

            for (int i = 1; i <= 3; i++)
            {
                service.MarkCompleted(Mission(service, i));
            }

            Assert.Greater(service.CompletedCount, 0);

            service.Reset();

            Assert.AreEqual(0, service.CompletedCount);
            Assert.IsFalse(service.IsCompleted(Mission(service, 1)));
            Assert.IsTrue(service.IsUnlocked(Mission(service, 1)));
            Assert.IsFalse(service.IsUnlocked(Mission(service, 2)));

            // A fresh service loading after reset also sees no progress.
            MissionProgressionService reloaded =
                new MissionProgressionService(BuildChapter(10), store);

            Assert.AreEqual(0, reloaded.CompletedCount);
        }

        [Test]
        public void SaveLoadRoundTripAcrossStoreInstancesWorks()
        {
            // Two SEPARATE service instances sharing one store prove Load reads exactly what
            // Save wrote (the persistence contract), through the real JSON format.
            JsonMemoryStore store = new JsonMemoryStore();
            ChapterDefinition chapter = BuildChapter(10);

            MissionProgressionService writer = new MissionProgressionService(chapter, store);
            writer.MarkCompleted(Mission(writer, 1));

            MissionProgressionService reader = new MissionProgressionService(chapter, store);

            Assert.IsTrue(reader.IsCompleted(Mission(reader, 1).MissionId));
            Assert.IsTrue(reader.IsUnlocked(Mission(reader, 2)));
            Assert.AreEqual(1, reader.CompletedCount);
        }

        // ----- production store (PlayerPrefs) resilience, isolated by a unique key -----

        private static string UniqueKey(string tag)
        {
            return "oo_test_progression_" + tag + "_" + System.Guid.NewGuid().ToString("N");
        }

        [Test]
        public void PlayerPrefsStoreRoundTripsAndDeletes()
        {
            string key = UniqueKey("rt");
            PlayerPrefsMissionProgressionStore store = new PlayerPrefsMissionProgressionStore(key);

            try
            {
                store.Save(new MissionProgressionSave
                {
                    version = MissionProgressionSave.CurrentVersion,
                    completedMissionIds = new List<string> { "mission_01", "mission_02" }
                });

                MissionProgressionSave loaded = store.Load();
                Assert.AreEqual(MissionProgressionSave.CurrentVersion, loaded.version);
                Assert.AreEqual(2, loaded.completedMissionIds.Count);
                CollectionAssert.AreEquivalent(
                    new[] { "mission_01", "mission_02" }, loaded.completedMissionIds);

                store.Delete();
                MissionProgressionSave afterDelete = store.Load();
                Assert.AreEqual(0, afterDelete.completedMissionIds.Count,
                    "Delete must clear the saved progression.");
            }
            finally
            {
                new PlayerPrefsMissionProgressionStore(key).Delete();
            }
        }

        [Test]
        public void PlayerPrefsStoreRejectsIncompatibleVersion()
        {
            string key = UniqueKey("ver");
            PlayerPrefsMissionProgressionStore store = new PlayerPrefsMissionProgressionStore(key);

            try
            {
                store.Save(new MissionProgressionSave
                {
                    version = 999,
                    completedMissionIds = new List<string> { "mission_01" }
                });

                MissionProgressionSave loaded = store.Load();
                Assert.AreEqual(0, loaded.completedMissionIds.Count,
                    "An incompatible save version must be rejected (reset to empty), not trusted.");
                Assert.AreEqual(MissionProgressionSave.CurrentVersion, loaded.version);
            }
            finally
            {
                new PlayerPrefsMissionProgressionStore(key).Delete();
            }
        }

        [Test]
        public void InvalidateDefaultCacheIsPublicStaticForCrossAssemblyTooling()
        {
            // Regression guard for the 1X QA fix #2 root cause: MissionProgressionService lives
            // in Assembly-CSharp, but the editor Reset tool and these EditMode tests live in
            // Assembly-CSharp-Editor. An 'internal' InvalidateDefaultCache is invisible across
            // that boundary and broke the build. Pin that the cache-invalidation API stays a
            // PUBLIC static method so the editor reset tooling and tests can always call it.
            MethodInfo method = typeof(MissionProgressionService).GetMethod(
                "InvalidateDefaultCache",
                BindingFlags.Public | BindingFlags.Static);

            Assert.IsNotNull(method,
                "MissionProgressionService.InvalidateDefaultCache must be a public static method " +
                "(it is called from the editor assembly: reset tool + tests).");
            Assert.AreEqual(typeof(void), method.ReturnType,
                "InvalidateDefaultCache must return void.");
        }

        // ============================================================ selection

        [Test]
        public void SelectionStartsEmpty()
        {
            MissionSelectionService selection =
                new MissionSelectionService(new MissionProgressionService(BuildChapter(10), new JsonMemoryStore()));

            Assert.IsFalse(selection.HasSelection);
            Assert.IsNull(selection.SelectedMission);
            Assert.IsFalse(selection.CanStartSelected);
        }

        [Test]
        public void LockedMissionsCannotBeSelectedOrStarted()
        {
            MissionSelectionService selection =
                new MissionSelectionService(new MissionProgressionService(BuildChapter(10), new JsonMemoryStore()));

            // Mission 2 is locked -> Select refuses and leaves the selection empty.
            Assert.IsFalse(selection.Select(Mission(selection.Progression, 2)));
            Assert.IsFalse(selection.HasSelection);

            // Starting with nothing selected also fails.
            Assert.IsFalse(selection.StartSelected());
            Assert.IsFalse(ActiveMissionContext.HasCurrent);
        }

        [Test]
        public void SelectingAndStartingAnUnlockedMissionSucceeds()
        {
            MissionSelectionService selection =
                new MissionSelectionService(new MissionProgressionService(BuildChapter(10), new JsonMemoryStore()));

            Assert.IsTrue(selection.Select(Mission(selection.Progression, 1)));
            Assert.AreSame(Mission(selection.Progression, 1), selection.SelectedMission);
            Assert.IsTrue(selection.CanStartSelected);

            Assert.IsTrue(selection.StartSelected());
            Assert.IsTrue(ActiveMissionContext.HasCurrent);
            Assert.AreSame(Mission(selection.Progression, 1), ActiveMissionContext.Current);
        }

        [Test]
        public void SelectingLockedMissionDoesNotChangeExistingSelection()
        {
            MissionSelectionService selection =
                new MissionSelectionService(new MissionProgressionService(BuildChapter(10), new JsonMemoryStore()));

            selection.Select(Mission(selection.Progression, 1));

            // Attempting to select a locked mission must not clobber the valid selection.
            Assert.IsFalse(selection.Select(Mission(selection.Progression, 5)));
            Assert.AreSame(Mission(selection.Progression, 1), selection.SelectedMission);
        }

        [Test]
        public void StartingACompletedReplayMissionStillWorks()
        {
            JsonMemoryStore store = new JsonMemoryStore();
            MissionProgressionService service =
                new MissionProgressionService(BuildChapter(10), store);
            MissionSelectionService selection = new MissionSelectionService(service);

            service.MarkCompleted(Mission(service, 1));

            // Mission 1 is completed AND unlocked -> selectable and startable (replay).
            Assert.IsTrue(selection.Select(Mission(service, 1)));
            Assert.IsTrue(selection.StartSelected());
        }

        // ============================================================ ActiveMissionContext

        [Test]
        public void ActiveMissionContextResolveFallsBackWhenEmpty()
        {
            ActiveMissionContext.Clear();

            MissionDefinition fallback = BuildMission(1);

            Assert.IsFalse(ActiveMissionContext.HasCurrent);
            Assert.AreSame(fallback, ActiveMissionContext.Resolve(fallback),
                "With no active mission, the consumer's fallback must be used.");
        }

        [Test]
        public void ActiveMissionContextResolvePrefersActive()
        {
            MissionDefinition active = BuildMission(2);
            MissionDefinition fallback = BuildMission(1);

            ActiveMissionContext.SetForRun(active);

            Assert.AreSame(active, ActiveMissionContext.Resolve(fallback));
            Assert.AreEqual("mission_02", ActiveMissionContext.CurrentMissionId);

            ActiveMissionContext.Clear();

            Assert.IsFalse(ActiveMissionContext.HasCurrent);
        }

        // ============================================================ debug UI input (QA fix #3)

        [Test]
        public void DebugUiActionsAssetDefinesPointerAndClickActions()
        {
            // The QA #3 root cause was a runtime-created InputSystemUIInputModule with no UI
            // actions, so ScreenSpaceOverlay buttons did not respond to clicks. Pin that the
            // debug UI builds a UI action map containing the actions the module needs.
            UnityEngine.InputSystem.InputActionAsset asset = MissionSelectionDebugUi.BuildDebugUiActions();

            try
            {
                Assert.IsNotNull(asset, "BuildDebugUiActions must return an action asset.");

                UnityEngine.InputSystem.InputActionMap uiMap = asset.FindActionMap("UI");
                Assert.IsNotNull(uiMap, "The asset must contain a 'UI' action map.");

                UnityEngine.InputSystem.InputAction point = uiMap.FindAction("Point");
                Assert.IsNotNull(point, "The UI map must contain a Point action.");
                Assert.GreaterOrEqual(point.bindings.Count, 1,
                    "Point must have at least one input binding (mouse/touch/pen).");

                UnityEngine.InputSystem.InputAction leftClick = uiMap.FindAction("LeftClick");
                Assert.IsNotNull(leftClick, "The UI map must contain a LeftClick action.");
                Assert.GreaterOrEqual(leftClick.bindings.Count, 1,
                    "LeftClick must have at least one input binding.");
            }
            finally
            {
                if (asset != null)
                {
                    Object.DestroyImmediate(asset);
                }
            }
        }

        [Test]
        public void ConfiguringTheInputModuleWiresPointerAndClickActions()
        {
            // A FRESH module has no pointer/click actions (the bug). After ConfigureInputModule it
            // must carry a real Point + LeftClick action so buttons become clickable.
            GameObject esGo = new GameObject("TestEventSystem",
                typeof(EventSystem), typeof(InputSystemUIInputModule));
            InputSystemUIInputModule module = esGo.GetComponent<InputSystemUIInputModule>();

            try
            {
                Assert.IsNull(module.pointAction.action,
                    "Sanity: a fresh runtime module has no Point action (the QA #3 root cause).");

                MissionSelectionDebugUi.ConfigureInputModule(module);

                Assert.IsNotNull(module.pointAction.action,
                    "After configuration the module must have a Point action.");
                Assert.GreaterOrEqual(module.pointAction.action.bindings.Count, 1,
                    "The Point action must have input bindings.");

                Assert.IsNotNull(module.leftClickAction.action,
                    "After configuration the module must have a LeftClick action.");
                Assert.GreaterOrEqual(module.leftClickAction.action.bindings.Count, 1,
                    "The LeftClick action must have input bindings.");
            }
            finally
            {
                if (module != null && module.actionsAsset != null)
                {
                    Object.DestroyImmediate(module.actionsAsset);
                }

                Object.DestroyImmediate(esGo);
            }
        }
    }
}
