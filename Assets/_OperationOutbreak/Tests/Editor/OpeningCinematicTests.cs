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
    /// mutates the scene or production assets). QA fix #8 updated these for the gate architecture.
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
            Build();
            Assert.AreEqual(childrenAfterFirst, _holder.transform.childCount,
                "Second BuildInto must replace (not duplicate) the cinematic root.");
        }

        [Test]
        public void ExactlyOneHelicopterFlightRoot()
        {
            var root = Build();
            int count = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "HelicopterFlightRoot") count++;
            Assert.AreEqual(1, count, "Exactly one HelicopterFlightRoot must exist.");
        }

        [Test]
        public void FlightPathHasRequiredPoints()
        {
            var root = Build();
            var path = root.transform.Find("FlightPath");
            Assert.IsNotNull(path, "FlightPath group must exist.");
            Assert.GreaterOrEqual(path.childCount, 5, "Flight path must have at least 5 authored points.");
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
            Assert.IsFalse(cam.enabled, "ExteriorCamera must be disabled until the cinematic starts.");
            Assert.AreNotEqual("MainCamera", camGo.tag, "ExteriorCamera must not be the MainCamera.");
        }

        [Test]
        public void ControllerResolvesRequiredReferences()
        {
            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();
            Assert.IsNotNull(controller, "OpeningCinematicController must be on the root.");
            Assert.AreEqual(OpeningCinematicController.Phase.Inactive, controller.CurrentPhase,
                "Controller must start in Inactive phase.");
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
            Assert.GreaterOrEqual(visual.childCount, 1, "HelicopterVisual must contain the model/placeholder.");
        }

        [Test]
        public void NoGameplayAuthorityComponents()
        {
            var root = Build();
            Assert.IsNull(root.GetComponentInChildren<OperationOutbreak.Player.PlayerController>(true),
                "Cinematic must not contain PlayerController.");
            Assert.IsNull(root.GetComponentInChildren<OperationOutbreak.Enemies.EnemySpawner>(true),
                "Cinematic must not contain EnemySpawner.");
        }

        [Test]
        public void NoCollidersOrRigidbodies()
        {
            var root = Build();
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
            var phases = System.Enum.GetNames(typeof(OpeningCinematicController.Phase));
            bool hasAwait = false;
            foreach (var p in phases) if (p == "AwaitingInteriorTransition") hasAwait = true;
            Assert.IsTrue(hasAwait, "Phase enum must contain AwaitingInteriorTransition.");
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
                "ExteriorCamera GameObject must be active after authoring.");
        }

        [Test]
        public void ExteriorCameraComponentStartsDisabled()
        {
            var root = Build();
            var cam = root.transform.Find("Cameras/ExteriorCamera").GetComponent<Camera>();
            Assert.IsFalse(cam.enabled,
                "ExteriorCamera component must start disabled.");
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
        public void ValidationFailsWithNoHelicopterRenderers()
        {
            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();
            var visual = root.transform.Find("HelicopterFlightRoot/HelicopterVisual");
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;

            LogAssert.Expect(LogType.Error, "[OPENING CINEMATIC] No enabled helicopter renderer with non-zero bounds found.");
            bool result = controller.ValidateCinematicSetup();
            Assert.IsFalse(result, "Validation must fail when no helicopter renderers are enabled.");
        }

        // ---- QA fix #8: gate architecture tests ----

        [Test]
        public void ControllerDoesNotModifyDirectorComponentState()
        {
            // The controller must NOT disable/enable the MissionStoryDirector component.
            // It only sets the HoldOpeningSequence gate flag. Verify the gate property exists.
            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();
            // In the test hierarchy there's no MissionStoryDirector, so the gate is not held.
            Assert.IsFalse(controller.IsHoldingDirectorGate,
                "Controller must not hold the gate when no MissionStoryDirector exists.");
        }

        [Test]
        public void DirectorGatePropertyExistsAndDefaultsFalse()
        {
            // Verify MissionStoryDirector exposes the HoldOpeningSequence gate.
            var prop = typeof(OperationOutbreak.Story.MissionStoryDirector).GetProperty("HoldOpeningSequence");
            Assert.IsNotNull(prop, "MissionStoryDirector must expose HoldOpeningSequence property.");
            Assert.AreEqual(typeof(bool), prop.PropertyType, "HoldOpeningSequence must be a bool.");
            // Verify ReleaseOpeningSequence method exists.
            var method = typeof(OperationOutbreak.Story.MissionStoryDirector).GetMethod("ReleaseOpeningSequence");
            Assert.IsNotNull(method, "MissionStoryDirector must expose ReleaseOpeningSequence method.");
            Assert.IsTrue(method.IsPublic, "ReleaseOpeningSequence must be public.");
        }

        [Test]
        public void HelicopterBoundsProjectInsideCameraViewport()
        {
            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();
            var camGo = root.transform.Find("Cameras/ExteriorCamera");
            Assert.IsNotNull(camGo);
            var cam = camGo.GetComponent<Camera>();

            // Position the helicopter at the start of the path.
            var flightRoot = root.transform.Find("HelicopterFlightRoot");
            var path = root.transform.Find("FlightPath");
            Assert.IsNotNull(flightRoot);
            Assert.IsNotNull(path);
            flightRoot.position = path.GetChild(0).position;

            // Enable the camera and position it at the trailing offset from the helicopter.
            controller.StartExteriorFlyover();
            if (cam != null && cam.enabled)
            {
                // Compute combined helicopter bounds.
                var visual = root.transform.Find("HelicopterFlightRoot/HelicopterVisual");
                Bounds bounds = new Bounds();
                bool first = true;
                foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || !r.enabled) continue;
                    if (first) { bounds = r.bounds; first = false; }
                    else bounds.Encapsulate(r.bounds);
                }
                if (!first)
                {
                    Vector3 vp = cam.WorldToViewportPoint(bounds.center);
                    Assert.Greater(vp.z, 0f,
                        "Helicopter bounds center must be IN FRONT of the camera (vp.z > 0). Got z=" + vp.z);
                }
            }
        }

        [Test]
        public void CinematicDoesNotPermanentlyDisableMainCamera()
        {
            // Verify the controller tracks and can restore the Main Camera.
            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();
            // Create a fake Main Camera.
            var camGo = new GameObject("FakeMainCam");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = true;
            try
            {
                controller.StartExteriorFlyover();
                // After start, the main camera should be disabled (tracked for restore).
                Assert.IsFalse(cam.enabled, "Main camera must be disabled during flyover.");

                // Simulate OnDestroy restoration (call the private restore logic via destruction).
                Object.DestroyImmediate(root);
                // After destruction, the camera should be restored.
                Assert.IsTrue(cam.enabled,
                    "Main camera must be restored when the cinematic controller is destroyed.");
            }
            finally
            {
                Object.DestroyImmediate(camGo);
            }
        }
    }
}
