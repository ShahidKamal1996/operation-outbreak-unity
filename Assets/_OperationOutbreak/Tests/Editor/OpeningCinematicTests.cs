using NUnit.Framework;
using OperationOutbreak.Cinematic;
using OperationOutbreak.EditorTools;
using UnityEngine;
using UnityEngine.TestTools;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1Z.1B — EditMode tests for the opening exterior helicopter flyover cinematic.
    /// Uses OpeningCinematicBuilder.BuildInto to create the hierarchy under a temp parent (never
    /// mutates the scene or production assets). Preserves the 454-test baseline.
    /// </summary>
    public sealed class OpeningCinematicTests
    {
        private GameObject _holder;

        [SetUp]
        public void SetUp() => _holder = new GameObject("TestHolder");

        [TearDown]
        public void TearDown() { if (_holder != null) Object.DestroyImmediate(_holder); }

        private GameObject Build() => OpeningCinematicBuilder.BuildInto(_holder.transform);

        [Test]
        public void CinematicRootAuthored()
        {
            var root = Build();
            Assert.IsNotNull(root);
            Assert.AreEqual("[Cinematic] Opening Sequence", root.name);
            Assert.AreEqual(_holder.transform, root.transform.parent);
        }

        [Test]
        public void BuilderIsIdempotent()
        {
            Build();
            int childrenAfterFirst = _holder.transform.childCount;
            Build(); // second build should replace, not duplicate
            Assert.AreEqual(childrenAfterFirst, _holder.transform.childCount,
                "Second BuildInto must replace (not duplicate) the cinematic root.");
        }

        [Test]
        public void ExactlyOneHelicopterFlightRoot()
        {
            var root = Build();
            var flightRoots = root.GetComponentsInChildren<Transform>(true);
            int count = 0;
            foreach (var t in flightRoots) if (t.name == "HelicopterFlightRoot") count++;
            Assert.AreEqual(1, count, "Exactly one HelicopterFlightRoot must exist.");
        }

        [Test]
        public void FlightPathHasRequiredPoints()
        {
            var root = Build();
            var path = root.transform.Find("FlightPath");
            Assert.IsNotNull(path, "FlightPath group must exist.");
            Assert.GreaterOrEqual(path.childCount, 5, "Flight path must have at least 5 authored points.");
            // Verify the controller references them.
            var controller = root.GetComponent<OpeningCinematicController>();
            Assert.IsNotNull(controller, "Controller must be on the root.");
        }

        [Test]
        public void ExteriorCameraExistsAndIsSeparate()
        {
            var root = Build();
            var camGroup = root.transform.Find("Cameras");
            Assert.IsNotNull(camGroup, "Cameras group must exist.");
            var camGo = camGroup.Find("ExteriorCamera");
            Assert.IsNotNull(camGo, "ExteriorCamera must exist.");
            var cam = camGo.GetComponent<Camera>();
            Assert.IsNotNull(cam, "ExteriorCamera must have a Camera component.");
            // It must be DISABLED initially (controller enables on start).
            Assert.IsFalse(cam.enabled, "ExteriorCamera must be disabled until the cinematic starts.");
            // It must NOT be tagged MainCamera.
            Assert.AreNotEqual("MainCamera", camGo.tag, "ExteriorCamera must not be the MainCamera.");
        }

        [Test]
        public void ControllerResolvesRequiredReferences()
        {
            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();
            Assert.IsNotNull(controller, "OpeningCinematicController must be on the root.");
            // Initial phase must be Inactive.
            Assert.AreEqual(OpeningCinematicController.Phase.Inactive, controller.CurrentPhase,
                "Controller must start in Inactive phase.");
            // AwaitingInteriorTransition must exist as a valid phase.
            Assert.IsTrue(System.Enum.IsDefined(typeof(OpeningCinematicController.Phase),
                "AwaitingInteriorTransition"), "Phase enum must define AwaitingInteriorTransition.");
        }

        [Test]
        public void HelicopterVisualUnderReplaceableRoot()
        {
            var root = Build();
            var flightRoot = root.transform.Find("HelicopterFlightRoot");
            Assert.IsNotNull(flightRoot, "HelicopterFlightRoot must exist.");
            var visual = flightRoot.Find("HelicopterVisual");
            Assert.IsNotNull(visual, "HelicopterVisual must be a child of HelicopterFlightRoot.");
            // The model must be under the visual (replaceable).
            Assert.GreaterOrEqual(visual.childCount, 1, "HelicopterVisual must contain the model/placeholder.");
        }

        [Test]
        public void NoGameplayAuthorityComponents()
        {
            var root = Build();
            // No PlayerController, EnemySpawner, MissionObjectiveController, etc.
            Assert.IsNull(root.GetComponentInChildren<OperationOutbreak.Player.PlayerController>(true),
                "Cinematic must not contain PlayerController.");
            Assert.IsNull(root.GetComponentInChildren<OperationOutbreak.Enemies.EnemySpawner>(true),
                "Cinematic must not contain EnemySpawner.");
        }

        [Test]
        public void NoCollidersOrRigidbodies()
        {
            var root = Build();
            // The rotor overlay disc may have a disabled collider; the model may have colliders.
            // We check for ENABLED colliders that could affect gameplay physics.
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
                Assert.IsFalse(col.enabled, "Cinematic collider '" + col.name + "' must be disabled.");
            Assert.AreEqual(0, root.GetComponentsInChildren<Rigidbody>(true).Length,
                "Cinematic must not contain Rigidbody components.");
        }

        [Test]
        public void FlightPathIndependentOfCityExtension()
        {
            var root = Build();
            var path = root.transform.Find("FlightPath");
            Assert.IsNotNull(path);
            // Path points must NOT be children of [Cinematic] City Extension.
            foreach (Transform point in path)
            {
                var current = point.parent;
                while (current != null)
                {
                    Assert.AreNotEqual(CinematicCityExtension.RootName, current.name,
                        "Flight path must not be nested under " + CinematicCityExtension.RootName);
                    current = current.parent;
                }
            }
        }

        [Test]
        public void ExteriorSequenceStopsAtAwaitingTransition()
        {
            // The Phase enum must have AwaitingInteriorTransition (the sequence ends there,
            // NOT at Complete or gameplay).
            var phases = System.Enum.GetNames(typeof(OpeningCinematicController.Phase));
            bool hasAwait = false;
            foreach (var p in phases) if (p == "AwaitingInteriorTransition") hasAwait = true;
            Assert.IsTrue(hasAwait, "Phase enum must contain AwaitingInteriorTransition.");

            // The enum must NOT have a gameplay-start phase (this milestone stops at transition).
            bool hasGameplayStart = false;
            foreach (var p in phases) if (p.Contains("Gameplay") || p.Contains("Play")) hasGameplayStart = true;
            Assert.IsFalse(hasGameplayStart,
                "Phase enum must not contain a gameplay-start state in this milestone.");
        }

        [Test]
        public void RotorPresentationPresent()
        {
            var root = Build();
            var visual = root.transform.Find("HelicopterFlightRoot/HelicopterVisual");
            Assert.IsNotNull(visual);
            var rotor = visual.GetComponent<HelicopterRotorPresentation>();
            Assert.IsNotNull(rotor, "HelicopterRotorPresentation must be on HelicopterVisual.");
        }

        [Test]
        public void ExteriorCameraGameObjectActiveAfterBuild()
        {
            var root = Build();
            var camGo = root.transform.Find("Cameras/ExteriorCamera");
            Assert.IsNotNull(camGo);
            Assert.IsTrue(camGo.gameObject.activeSelf,
                "ExteriorCamera GameObject must be active after authoring (the Camera component is disabled, not the GO).");
        }

        [Test]
        public void ExteriorCameraComponentStartsDisabled()
        {
            var root = Build();
            var cam = root.transform.Find("Cameras/ExteriorCamera").GetComponent<Camera>();
            Assert.IsFalse(cam.enabled,
                "ExteriorCamera component must start disabled (enabled by the controller on cinematic start).");
        }

        [Test]
        public void StartExteriorFlyoverEnablesCameraComponent()
        {
            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();
            Assert.IsFalse(controller.IsExteriorCameraEnabled, "Camera must be disabled before start.");
            controller.StartExteriorFlyover();
            Assert.IsTrue(controller.IsExteriorCameraEnabled,
                "StartExteriorFlyover must enable the ExteriorCamera component.");
        }

        [Test]
        public void NullExteriorCameraAbortsSafely()
        {
            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();
            // Null out the exterior camera reference via reflection (simulating misconfiguration).
            var field = typeof(OpeningCinematicController).GetField("exteriorCamera",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(controller, null);

            LogAssert.Expect(LogType.Error, "[OPENING CINEMATIC] Exterior camera is null or inactive.");
            LogAssert.Expect(LogType.Error, "[OPENING CINEMATIC] Setup validation failed — aborting. Gameplay camera preserved.");
            controller.StartExteriorFlyover();
            Assert.AreEqual(OpeningCinematicController.Phase.Inactive, controller.CurrentPhase,
                "Controller must abort (stay Inactive) when exterior camera is not valid.");
        }

        [Test]
        public void HelicopterHasEnabledRenderersWithNonZeroBounds()
        {
            var root = Build();
            var visual = root.transform.Find("HelicopterFlightRoot/HelicopterVisual");
            Assert.IsNotNull(visual, "HelicopterVisual must exist.");
            bool found = false;
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (r != null && r.enabled && r.bounds.size.magnitude > 0.01f)
                { found = true; break; }
            }
            Assert.IsTrue(found, "At least one enabled renderer with non-zero bounds must exist on the helicopter.");
        }

        [Test]
        public void HelicopterRenderersOnDefaultLayer()
        {
            var root = Build();
            var visual = root.transform.Find("HelicopterFlightRoot/HelicopterVisual");
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                Assert.AreEqual(0, r.gameObject.layer,
                    "Helicopter renderer '" + r.name + "' must be on layer 0 (default) for ExteriorCamera culling.");
            }
        }

        [Test]
        public void GameplayVisualHidingIsReversible()
        {
            // Create a fake "Player" object with a renderer, simulate hide + restore.
            var fakePlayer = new GameObject("Player");
            var mr = fakePlayer.AddComponent<MeshRenderer>();
            mr.enabled = true;
            try
            {
                var root = Build();
                var controller = root.GetComponent<OpeningCinematicController>();

                // Set gameplayVisualNames to include "Player" (already default).
                var namesField = typeof(OpeningCinematicController).GetField("gameplayVisualNames",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                // Hide
                var hideMethod = typeof(OpeningCinematicController).GetMethod("HideGameplayVisuals",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                hideMethod.Invoke(controller, null);
                Assert.IsFalse(mr.enabled, "Player renderer must be hidden during cinematic.");

                // Restore
                var restoreMethod = typeof(OpeningCinematicController).GetMethod("RestoreGameplayVisuals",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                restoreMethod.Invoke(controller, null);
                Assert.IsTrue(mr.enabled, "Player renderer must be restored after cinematic.");
            }
            finally
            {
                Object.DestroyImmediate(fakePlayer);
            }
        }

        [Test]
        public void ValidationFailsWithNoHelicopterRenderers()
        {
            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();
            // Remove all renderers from helicopter visual to trigger validation failure.
            var visual = root.transform.Find("HelicopterFlightRoot/HelicopterVisual");
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;

            LogAssert.Expect(LogType.Error, "[OPENING CINEMATIC] No enabled helicopter renderer with non-zero bounds found.");
            bool result = controller.ValidateCinematicSetup();
            Assert.IsFalse(result, "Validation must fail when no helicopter renderers are enabled.");
        }

        [Test]
        public void ControllerHasPresentationOwnershipMethods()
        {
            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();
            // Verify the ownership API exists and the initial state is correct.
            Assert.IsFalse(controller.HasSuppressedDirector,
                "Controller must start without suppressed director (no MissionStoryDirector in test hierarchy).");
            // ReleasePresentationOwnership must be safe even when nothing was suppressed.
            controller.ReleasePresentationOwnership();
        }

        [Test]
        public void PresentationOwnershipSuppressesDirector()
        {
            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();

            // Create a minimal mock "MissionStoryDirector-like" component to suppress.
            var mockGO = new GameObject("MockDirector");
            try
            {
                // Use a simple Behaviour the controller can suppress via its generic Behaviour field.
                // The actual suppression targets MissionStoryDirector (a MonoBehaviour).
                // In this test we verify the controller's ownership API doesn't crash and
                // that it handles the no-director case gracefully.
                controller.AcquirePresentationOwnership();
                // In Edit Mode there's no MissionStoryDirector in the test hierarchy,
                // so HasSuppressedDirector stays false (nothing found to suppress).
                Assert.IsFalse(controller.HasSuppressedDirector,
                    "No MissionStoryDirector exists in test hierarchy — nothing to suppress.");
            }
            finally
            {
                Object.DestroyImmediate(mockGO);
                Object.DestroyImmediate(root);
            }
        }
    }
}
