using NUnit.Framework;
using OperationOutbreak.Cinematic;
using OperationOutbreak.EditorTools;
using OperationOutbreak.Story;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1Z.1B QA fix #10 Step 2A — ownership boundary tests.
    ///
    /// Manual QA established that the RAVEN/Kane helicopter-interior sequence belongs to the
    /// GLOBAL GAME-OPENING CINEMATIC, not to Mission 01. These tests prove the ownership split:
    ///
    ///   MissionStoryDirector  -> Mission 01 runtime story (radio beats, outro) + EXECUTING the
    ///                            opening story when explicitly asked + the ONE gameplay-start path
    ///   OpeningCinematic      -> DECIDES when the global opening story runs
    ///
    /// Scope: ownership only. No camera framing, flight path, or 1Z.1C content is asserted.
    /// </summary>
    public sealed class OpeningCinematicOwnershipTests
    {
        private GameObject _holder;

        [SetUp]
        public void SetUp()
        {
            // MissionStoryDirector subscribes to the STATIC StoryCueEvents in Awake, and Unity does
            // not run OnDestroy in Edit Mode, so a destroyed director would stay subscribed and
            // react to a later test's cues. Clear before and after, matching StorySequenceTests.
            StoryCueEvents.ClearSubscribers();
            OpeningStoryStartPermission.ResetState();
            _holder = new GameObject("OwnershipTestHolder");
        }

        [TearDown]
        public void TearDown()
        {
            if (_holder != null) Object.DestroyImmediate(_holder);

            // Destroy any story objects the director created outside the holder (it parents its
            // helpers to itself, but the runner/HUD helpers are created via new GameObject()).
            foreach (var runner in Object.FindObjectsByType<StorySequenceRunner>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (runner != null) Object.DestroyImmediate(runner.gameObject);

            foreach (var rig in Object.FindObjectsByType<HelicopterInteriorRig>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (rig != null) Object.DestroyImmediate(rig.gameObject);

            StoryCueEvents.ClearSubscribers();
            OpeningStoryStartPermission.ResetState();
        }

        private GameObject BuildCinematic() => OpeningCinematicBuilder.BuildInto(_holder.transform);

        private static void SetAutoStart(OpeningCinematicController controller, bool value)
        {
            var field = typeof(OpeningCinematicController).GetField("autoStartOnPlay",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "autoStartOnPlay must exist on OpeningCinematicController.");
            field.SetValue(controller, value);
        }

        private static void InvokeLifecycle(object target, string methodName)
        {
            var method = target.GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method, methodName + " must exist on " + target.GetType().Name + ".");
            method.Invoke(target, null);
        }

        /// <summary>
        /// Builds a director wired to the real Mission 01 context. Unity does not run Awake for
        /// components in Edit Mode, so the real Awake is invoked explicitly to load the production
        /// Mission_01 / opening-sequence assets exactly as it would at runtime.
        /// </summary>
        private MissionStoryDirector BuildInitializedDirector()
        {
            var go = new GameObject("MissionStoryDirector");
            go.transform.SetParent(_holder.transform, false);
            var director = go.AddComponent<MissionStoryDirector>();
            InvokeLifecycle(director, "Awake");
            return director;
        }

        /// <summary>
        /// Detaches every story CUE consumer before a sequence is actually executed.
        ///
        /// These are OWNERSHIP tests: they assert who is allowed to start the sequence, not what
        /// the sequence renders. Letting the cues run would drag in presentation side effects that
        /// are invalid in Edit Mode — notably StoryFadeController.FadeFromBlack, which calls
        /// StartCoroutine (coroutines do not execute in Edit Mode), plus interior-rig construction.
        /// Presentation behaviour is already covered by Mission01InteriorCinematicTests.
        /// </summary>
        private static void IsolateFromPresentationCues() => StoryCueEvents.ClearSubscribers();

        // ---- 1. The director must NOT auto-start the global opening cinematic ----

        [Test]
        public void DirectorEnablingDoesNotStartGlobalOpeningStory()
        {
            // THE regression test for the Step 2A ownership correction. With NO opening cinematic
            // in the scene at all, enabling the director must not play the RAVEN/Kane sequence.
            var director = BuildInitializedDirector();

            InvokeLifecycle(director, "OnEnable");

            Assert.IsFalse(director.IsPlayingOpeningStory,
                "MissionStoryDirector must NEVER auto-start the global RAVEN/Kane opening story. " +
                "That sequence belongs to the global opening cinematic pipeline.");

            var runner = Object.FindAnyObjectByType<StorySequenceRunner>();
            if (runner != null)
                Assert.IsFalse(runner.IsRunning,
                    "No story sequence may be running as a side effect of enabling the director.");
        }

        [Test]
        public void DirectorEnablingWithBypassedCinematicDoesNotPlayOpeningStory()
        {
            // Development bypass: cinematic present but Auto Start On Play OFF.
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, false);
            InvokeLifecycle(controller, "Awake");

            var director = BuildInitializedDirector();
            InvokeLifecycle(director, "OnEnable");

            Assert.IsFalse(director.IsPlayingOpeningStory,
                "Bypass mode must NOT play the RAVEN/Kane interior sequence.");
        }

        [Test]
        public void DirectorStandsDownWhenGlobalCinematicOwnsStartup()
        {
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);
            InvokeLifecycle(controller, "Awake");

            var director = BuildInitializedDirector();
            InvokeLifecycle(director, "OnEnable");

            Assert.IsTrue(director.IsOpeningOwnedByGlobalCinematic,
                "The director must recognise that a global opening cinematic owns startup.");
            Assert.IsFalse(director.IsPlayingOpeningStory,
                "The director must stand down while the global cinematic owns startup.");
            Assert.IsFalse(director.IsInGameplayPhase,
                "The director must NOT jump into gameplay while the opening cinematic is running.");
        }

        // ---- 2. The cinematic owns permission to start the sequence ----

        [Test]
        public void OpeningCinematicOwnsPermissionToStartTheStory()
        {
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);
            InvokeLifecycle(controller, "Awake");

            var director = BuildInitializedDirector();

            // While the cinematic owns startup the director refuses an unauthorised start.
            Assert.IsFalse(director.StartOpeningStorySequence(),
                "The director must refuse to start the opening story while the cinematic holds it.");
            Assert.IsFalse(director.IsPlayingOpeningStory);
        }

        [Test]
        public void CinematicHandoffIsTheEntryPointForTheInteriorStory()
        {
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);
            InvokeLifecycle(controller, "Awake");

            var director = BuildInitializedDirector();
            InvokeLifecycle(director, "OnEnable");
            Assert.IsFalse(director.IsPlayingOpeningStory, "Baseline: story not started.");

            // The global pipeline explicitly hands off — this is the 1Z.1C seam.
            IsolateFromPresentationCues();
            bool started = controller.HandoffToInteriorStory();

            Assert.IsTrue(started, "Handoff must start the interior RAVEN/Kane story.");
            Assert.IsTrue(director.IsPlayingOpeningStory,
                "After handoff the director must be EXECUTING the opening story.");
            Assert.IsFalse(controller.RequestsStoryHold,
                "After handoff the cinematic must relinquish its claim.");
        }

        [Test]
        public void HandoffIsNotCalledAutomaticallyByTheFlyover()
        {
            // Step 2A explicitly defers automatic handoff to 1Z.1C. The flyover must end in
            // AwaitingInteriorTransition and stay there.
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);
            InvokeLifecycle(controller, "Awake");

            var director = BuildInitializedDirector();
            InvokeLifecycle(director, "OnEnable");

            Assert.AreNotEqual(OpeningCinematicController.Phase.Complete, controller.CurrentPhase,
                "The cinematic must not auto-complete its handoff in this step.");
            Assert.IsFalse(director.IsPlayingOpeningStory,
                "No automatic handoff: the interior story must not start on its own.");
        }

        // ---- 3. Development bypass enters gameplay without the opening story ----

        [Test]
        public void BypassEntersMission01GameplayWithoutOpeningStory()
        {
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, false);
            InvokeLifecycle(controller, "Awake");

            var director = BuildInitializedDirector();
            InvokeLifecycle(director, "OnEnable");

            Assert.IsTrue(director.IsInGameplayPhase,
                "Bypass must enter the Mission 01 GAMEPLAY state directly.");
            Assert.IsFalse(director.IsPlayingOpeningStory,
                "Bypass must not play the opening story.");
        }

        [Test]
        public void BypassWithNoCinematicInSceneAlsoEntersGameplay()
        {
            // No cinematic authored at all — the plain Gameplay_Prototype case.
            var director = BuildInitializedDirector();
            InvokeLifecycle(director, "OnEnable");

            Assert.IsTrue(director.IsInGameplayPhase,
                "With no opening cinematic owning startup, Mission 01 must go straight to gameplay.");
        }

        [Test]
        public void ExplicitBypassEntryIsIdempotent()
        {
            var director = BuildInitializedDirector();
            InvokeLifecycle(director, "OnEnable");
            Assert.IsTrue(director.IsInGameplayPhase);

            // Re-entry must not re-run gameplay initialization.
            director.EnterGameplayWithoutOpening();
            Assert.IsTrue(director.IsInGameplayPhase,
                "Repeated gameplay entry must remain a safe no-op.");
        }

        // ---- 4. Mission-specific runtime story behaviour survives ----

        [Test]
        public void MissionSpecificStoryApiRemainsAvailable()
        {
            // The director must KEEP its Mission 01 runtime story responsibilities: radio beats
            // driven from Update (gameplay phase) and the encounter-driven outro.
            var directorType = typeof(MissionStoryDirector);

            Assert.IsNotNull(directorType.GetMethod("Update",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
                "Radio-beat Update loop must be preserved.");
            Assert.IsNotNull(directorType.GetMethod("OnEncounterCompleted",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
                "Encounter-driven outro handler must be preserved.");
            Assert.IsNotNull(directorType.GetMethod("BuildRadioBeat",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static),
                "Radio-beat construction must be preserved.");
        }

        [Test]
        public void RadioBeatsRequireGameplayPhaseNotOpeningPhase()
        {
            // Bypass reaches the gameplay phase, which is the phase radio beats require. This
            // proves mission-specific story remains reachable when the opening is skipped.
            var director = BuildInitializedDirector();
            InvokeLifecycle(director, "OnEnable");

            Assert.IsTrue(director.IsInGameplayPhase,
                "Radio beats only fire in the Gameplay phase, so bypass must reach it.");
        }

        // ---- 5. The existing opening sequence asset/content is preserved ----

        [Test]
        public void OpeningSequenceAssetIsPreservedAndReused()
        {
            const string openingPath =
                "Assets/_OperationOutbreak/Resources/StorySequences/Chapter01_Mission01_Opening.asset";

            var seq = UnityEditor.AssetDatabase.LoadAssetAtPath<StorySequenceDefinition>(openingPath);
            Assert.IsNotNull(seq,
                "Step 2A must NOT move, rename, or delete the opening sequence asset — ownership " +
                "moved, content did not.");

            // Resources.Load is how the director resolves it at runtime; that path must still work.
            var viaResources = Resources.Load<StorySequenceDefinition>(
                "StorySequences/Chapter01_Mission01_Opening");
            Assert.IsNotNull(viaResources,
                "The opening sequence must remain loadable from Resources at its original path.");

            // Content spot-check: the RAVEN/Kane interior dialogue is intact and NOT duplicated.
            bool hasRaven = false;
            for (int i = 0; i < seq.BeatCount; i++)
            {
                var b = seq.GetBeat(i);
                if (b.beatType == StoryBeatType.Dialogue && b.dialogue != null
                    && b.dialogue.speakerId == "raven_ortiz")
                    hasRaven = true;
            }
            Assert.IsTrue(hasRaven, "RAVEN ORTIZ dialogue content must be preserved unchanged.");
        }

        [Test]
        public void OpeningStoryContentIsNotDuplicatedElsewhere()
        {
            // Guard against someone re-authoring the interior story in a second asset.
            var all = Resources.LoadAll<StorySequenceDefinition>("StorySequences");
            int interiorSequences = 0;
            foreach (var seq in all)
            {
                if (seq == null) continue;
                for (int i = 0; i < seq.BeatCount; i++)
                {
                    var b = seq.GetBeat(i);
                    if (b.beatType == StoryBeatType.EventCue && b.cueId == "m01_interior_setup")
                    {
                        interiorSequences++;
                        break;
                    }
                }
            }
            Assert.AreEqual(1, interiorSequences,
                "The helicopter-interior story must exist in exactly ONE sequence asset — " +
                "ownership moved, content must not be duplicated.");
        }

        // ---- 6. Exactly one authoritative gameplay-start transition ----

        [Test]
        public void SingleAuthoritativeGameplayStartTransitionExists()
        {
            // Both the bypass path and the opening-complete path must funnel through the same
            // private transition, so gameplay initialization is never duplicated.
            var transition = typeof(MissionStoryDirector).GetMethod("EnterMission01GameplayState",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(transition,
                "There must be ONE authoritative Mission 01 gameplay-start transition.");

            // And it must not be publicly bypassable — no second entry point.
            Assert.IsFalse(transition.IsPublic,
                "The gameplay transition must stay internal so callers cannot fork a second path.");
        }

        [Test]
        public void SkippedOpeningAndBypassReachTheSameGameplayState()
        {
            // Bypass path.
            var bypassDirector = BuildInitializedDirector();
            InvokeLifecycle(bypassDirector, "OnEnable");
            bool bypassInGameplay = bypassDirector.IsInGameplayPhase;
            Object.DestroyImmediate(bypassDirector.gameObject);

            OpeningStoryStartPermission.ResetState();

            // Opening-completed path: start the story, then complete it.
            var storyDirector = BuildInitializedDirector();
            InvokeLifecycle(storyDirector, "OnEnable");
            IsolateFromPresentationCues();
            storyDirector.StartOpeningStorySequence();

            var completed = typeof(MissionStoryDirector).GetMethod("EnterMission01GameplayState",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            completed.Invoke(storyDirector, new object[] { true });

            Assert.IsTrue(bypassInGameplay,
                "Bypass must reach the Mission 01 gameplay state.");
            Assert.IsTrue(storyDirector.IsInGameplayPhase,
                "A completed/skipped opening must reach the SAME Mission 01 gameplay state.");
        }
    }
}
