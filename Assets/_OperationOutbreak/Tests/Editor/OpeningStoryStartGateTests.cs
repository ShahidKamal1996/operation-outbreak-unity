using NUnit.Framework;
using OperationOutbreak.Cinematic;
using OperationOutbreak.EditorTools;
using OperationOutbreak.Story;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1Z.1B QA fix #10 — EditMode tests for the Mission 01 opening-story start gate.
    ///
    /// Covers ONLY the start-permission race fix. No camera framing, flight path, player
    /// visibility, or 1Z.1C handoff behaviour is asserted here.
    ///
    /// The bug being guarded: MissionStoryDirector.OnEnable could run BEFORE
    /// OpeningCinematicController.Awake, so the QA fix #8 flag was still false and the opening
    /// auto-started — RAVEN ORTIZ dialogue played over the exterior flyover.
    /// </summary>
    public sealed class OpeningStoryStartGateTests
    {
        private GameObject _holder;

        [SetUp]
        public void SetUp()
        {
            OpeningStoryStartPermission.ResetState();
            _holder = new GameObject("StoryGateTestHolder");
        }

        [TearDown]
        public void TearDown()
        {
            if (_holder != null) Object.DestroyImmediate(_holder);
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

        // ---- 1-3: permission authority semantics ----

        [Test]
        public void PermissionDefaultsToAllowed()
        {
            Assert.IsTrue(OpeningStoryStartPermission.IsAllowed,
                "Default state MUST be allowed so bypass mode and every non-cinematic scene " +
                "keep the original Mission 01 flow.");
            Assert.AreEqual(0, OpeningStoryStartPermission.HoldCount);
        }

        [Test]
        public void HoldMakesPermissionDisallowed()
        {
            OpeningStoryStartPermission.Hold();
            Assert.IsFalse(OpeningStoryStartPermission.IsAllowed, "Hold() must disallow story start.");
            Assert.AreEqual(1, OpeningStoryStartPermission.HoldCount);
        }

        [Test]
        public void ReleaseRestoresAllowed()
        {
            OpeningStoryStartPermission.Hold();
            OpeningStoryStartPermission.Release();
            Assert.IsTrue(OpeningStoryStartPermission.IsAllowed, "Release() must restore allowed.");
            Assert.AreEqual(0, OpeningStoryStartPermission.HoldCount);
        }

        [Test]
        public void OwnerKeyedHoldIsIdempotent()
        {
            var ownerA = new object();

            // Duplicate holds by the same owner collapse to one token...
            OpeningStoryStartPermission.Hold(ownerA);
            OpeningStoryStartPermission.Hold(ownerA);
            Assert.AreEqual(1, OpeningStoryStartPermission.HoldCount,
                "Duplicate Hold(owner) must not stack — otherwise a single Release would leak a hold.");

            // ...so a single Release fully clears it.
            OpeningStoryStartPermission.Release(ownerA);
            Assert.IsTrue(OpeningStoryStartPermission.IsAllowed);

            // Over-releasing an owner that holds nothing must not corrupt the gate.
            OpeningStoryStartPermission.Release(ownerA);
            OpeningStoryStartPermission.Release(new object());
            Assert.IsTrue(OpeningStoryStartPermission.IsAllowed,
                "Stray Release calls must never unbalance the permission.");
        }

        [Test]
        public void DistinctOwnersEachRequireTheirOwnRelease()
        {
            var ownerA = new object();
            var ownerB = new object();

            OpeningStoryStartPermission.Hold(ownerA);
            OpeningStoryStartPermission.Hold(ownerB);
            Assert.AreEqual(2, OpeningStoryStartPermission.HoldCount);

            OpeningStoryStartPermission.Release(ownerA);
            Assert.IsFalse(OpeningStoryStartPermission.IsAllowed,
                "Story must stay held while another owner still holds a token.");

            OpeningStoryStartPermission.Release(ownerB);
            Assert.IsTrue(OpeningStoryStartPermission.IsAllowed);
        }

        [Test]
        public void ResetStateClearsStaleStaticHolds()
        {
            // Guards "Enter Play Mode Options" with domain reload disabled: a hold leaked from a
            // previous play session would otherwise permanently suppress the Mission 01 opening.
            OpeningStoryStartPermission.Hold(new object());
            OpeningStoryStartPermission.Hold();
            Assert.IsFalse(OpeningStoryStartPermission.IsAllowed);

            OpeningStoryStartPermission.ResetState();

            Assert.IsTrue(OpeningStoryStartPermission.IsAllowed,
                "ResetState must clear stale static holds across domain-reload-free play sessions.");
            Assert.AreEqual(0, OpeningStoryStartPermission.HoldCount);
        }

        // ---- 4-5: controller declares intent from serialized state ----

        [Test]
        public void AutoStartOffMeansNoStoryHoldRequested()
        {
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, false);

            Assert.IsFalse(controller.RequestsStoryHold,
                "Auto Start On Play OFF must not request a story hold (development bypass).");

            // Even running the real lifecycle must not acquire a token in bypass mode.
            InvokeLifecycle(controller, "Awake");
            InvokeLifecycle(controller, "OnEnable");

            Assert.IsTrue(OpeningStoryStartPermission.IsAllowed,
                "Bypass mode must leave the original Mission 01 flow completely untouched.");
            Assert.IsFalse(controller.IsHoldingDirectorGate,
                "Bypass mode must never acquire a permission token.");
        }

        [Test]
        public void AutoStartOnMeansStoryHoldRequested()
        {
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);

            Assert.IsTrue(controller.RequestsStoryHold,
                "Auto Start On Play ON must request a story hold.");
        }

        [Test]
        public void RequestsStoryHoldIsFalseWhenControllerDisabledOrInactive()
        {
            var root = BuildCinematic();
            var controller = root.GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);

            controller.enabled = false;
            Assert.IsFalse(controller.RequestsStoryHold,
                "A disabled controller does not own startup and must not hold the story.");

            controller.enabled = true;
            root.SetActive(false);
            Assert.IsFalse(controller.RequestsStoryHold,
                "An inactive controller does not own startup and must not hold the story.");
        }

        // ---- 6: director refuses to start while permission is held ----

        [Test]
        public void DirectorDoesNotStartOpeningWhenPermissionHeld()
        {
            var director = new GameObject("Director").AddComponent<MissionStoryDirector>();
            try
            {
                Assert.IsTrue(director.IsOpeningStartAllowed,
                    "Baseline: with nothing held the director must be free to start.");

                OpeningStoryStartPermission.Hold();

                Assert.IsFalse(director.IsOpeningStartAllowed,
                    "A held permission MUST block the Mission 01 opening.");
            }
            finally
            {
                Object.DestroyImmediate(director.gameObject);
            }
        }

        [Test]
        public void DirectorLocalHoldStillBlocksOpening()
        {
            var director = new GameObject("Director").AddComponent<MissionStoryDirector>();
            try
            {
                director.HoldOpeningSequence = true;
                Assert.IsFalse(director.IsOpeningStartAllowed,
                    "The legacy local gate must remain honoured alongside the new authority.");
            }
            finally
            {
                Object.DestroyImmediate(director.gameObject);
            }
        }

        // ---- 7: THE RACE GUARD ----

        [Test]
        public void DirectorDefersWhenActiveCinematicRequestsHoldEvenBeforeItInitializes()
        {
            // This is the regression test for QA fix #10.
            //
            // Scenario: MissionStoryDirector.OnEnable runs BEFORE OpeningCinematicController.Awake.
            // We reproduce it by building the cinematic and deliberately NEVER invoking its Awake,
            // so no permission token exists — exactly the state the director previously saw when
            // it won the initialization race and wrongly started the opening.
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);

            var director = new GameObject("Director").AddComponent<MissionStoryDirector>();
            try
            {
                // Precondition: the cinematic has NOT initialized, so the token layer is silent.
                Assert.IsTrue(OpeningStoryStartPermission.IsAllowed,
                    "Precondition: no token acquired yet (cinematic Awake has not run).");
                Assert.IsFalse(director.HoldOpeningSequence,
                    "Precondition: nothing has pushed the legacy flag onto the director.");

                // ...yet the serialized declaration is already visible to the scene scan.
                Assert.IsTrue(OpeningStoryStartPermission.AnyActiveHoldSourceRequestsHold(),
                    "The scene scan must see serialized intent before the cinematic initializes.");

                // ...so the director defers anyway. THIS is what removes the ordering race.
                Assert.IsFalse(director.IsOpeningStartAllowed,
                    "RACE GUARD: the director must defer to an active cinematic that declares " +
                    "startup ownership, even before that cinematic's Awake has run.");
            }
            finally
            {
                Object.DestroyImmediate(director.gameObject);
            }
        }

        [Test]
        public void DirectorDoesNotDeferToBypassedCinematicBeforeItInitializes()
        {
            // Mirror of the race guard: an Auto Start OFF cinematic must NOT suppress the story,
            // even under the same pre-initialization conditions.
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, false);

            var director = new GameObject("Director").AddComponent<MissionStoryDirector>();
            try
            {
                Assert.IsFalse(OpeningStoryStartPermission.AnyActiveHoldSourceRequestsHold(),
                    "A bypassed cinematic must not register as a hold source.");
                Assert.IsTrue(director.IsOpeningStartAllowed,
                    "Auto Start OFF must leave the original Mission 01 opening flow intact.");
            }
            finally
            {
                Object.DestroyImmediate(director.gameObject);
            }
        }

        [Test]
        public void DirectorStaysDeferredAfterCinematicAcquiresToken()
        {
            // Same scene, but now the cinematic HAS initialized. Both layers agree.
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);
            InvokeLifecycle(controller, "Awake");

            var director = new GameObject("Director").AddComponent<MissionStoryDirector>();
            try
            {
                Assert.IsTrue(controller.IsHoldingDirectorGate,
                    "Cinematic must hold a permission token once initialized.");
                Assert.IsFalse(OpeningStoryStartPermission.IsAllowed,
                    "Token layer must report the hold.");
                Assert.IsFalse(director.IsOpeningStartAllowed,
                    "Director must remain deferred once the cinematic owns startup.");
            }
            finally
            {
                Object.DestroyImmediate(director.gameObject);
            }
        }

        // ---- 8: releasing the hold restores the original opening ----

        [Test]
        public void ReleasingHoldAllowsOriginalOpeningToStartAgain()
        {
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);
            InvokeLifecycle(controller, "Awake");

            var director = new GameObject("Director").AddComponent<MissionStoryDirector>();
            try
            {
                Assert.IsFalse(director.IsOpeningStartAllowed, "Baseline: opening is deferred.");

                // The 1Z.1C handoff entry point.
                controller.ReleaseStoryHandoff();

                Assert.IsFalse(controller.RequestsStoryHold,
                    "After handoff the cinematic must no longer declare startup ownership.");
                Assert.IsTrue(OpeningStoryStartPermission.IsAllowed,
                    "After handoff the permission token must be released.");
                Assert.IsFalse(OpeningStoryStartPermission.AnyActiveHoldSourceRequestsHold(),
                    "After handoff the scene scan must no longer report a hold.");
                Assert.IsTrue(director.IsOpeningStartAllowed,
                    "Releasing every hold must let the original Mission 01 opening start again.");

                // Idempotent.
                controller.ReleaseStoryHandoff();
                Assert.IsTrue(director.IsOpeningStartAllowed,
                    "Repeated handoff calls must remain safe.");
            }
            finally
            {
                Object.DestroyImmediate(director.gameObject);
            }
        }

        [Test]
        public void ReleaseSceneHoldSourcesClearsEveryActiveClaim()
        {
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);
            InvokeLifecycle(controller, "Awake");

            Assert.IsTrue(OpeningStoryStartPermission.AnyActiveHoldSourceRequestsHold());

            int released = OpeningStoryStartPermission.ReleaseSceneHoldSources();

            Assert.AreEqual(1, released, "Exactly one active hold source was present.");
            Assert.IsTrue(OpeningStoryStartPermission.IsAllowed,
                "ReleaseSceneHoldSources must clear the permission.");
            Assert.IsFalse(OpeningStoryStartPermission.AnyActiveHoldSourceRequestsHold());
        }

        [Test]
        public void DirectorForceReleaseClearsCinematicHold()
        {
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);
            InvokeLifecycle(controller, "Awake");

            var director = new GameObject("Director").AddComponent<MissionStoryDirector>();
            try
            {
                Assert.IsFalse(director.IsOpeningStartAllowed);

                // ReleaseOpeningSequence clears only the director's OWN gate — the cinematic
                // still owns startup, so the opening must correctly stay deferred.
                director.ReleaseOpeningSequence();
                Assert.IsFalse(director.IsOpeningStartAllowed,
                    "Clearing only the local gate must not override an active cinematic's claim.");

                // The full handoff clears everything.
                director.ForceReleaseAllOpeningHolds();
                Assert.IsTrue(director.IsOpeningStartAllowed,
                    "ForceReleaseAllOpeningHolds must clear every outstanding claim.");
            }
            finally
            {
                Object.DestroyImmediate(director.gameObject);
            }
        }

        [Test]
        public void CinematicTeardownReleasesStoryHold()
        {
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            SetAutoStart(controller, true);
            InvokeLifecycle(controller, "Awake");
            Assert.IsFalse(OpeningStoryStartPermission.IsAllowed);

            InvokeLifecycle(controller, "OnDestroy");

            Assert.IsTrue(OpeningStoryStartPermission.IsAllowed,
                "Cinematic teardown must release the story hold so the flow is recoverable.");
        }

        [Test]
        public void ControllerImplementsHoldSourceContract()
        {
            var controller = BuildCinematic().GetComponent<OpeningCinematicController>();
            Assert.IsInstanceOf<IOpeningStoryHoldSource>(controller,
                "OpeningCinematicController must participate in the story hold contract so the " +
                "director can discover it without the Story layer referencing the Cinematic layer.");
        }
    }
}
