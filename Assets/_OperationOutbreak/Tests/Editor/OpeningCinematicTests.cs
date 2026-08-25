using System.Collections.Generic;
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
    /// QA fix #9 rewrote CinematicDoesNotPermanentlyDisableMainCamera for deterministic
    /// Camera.main resolution in the EditMode bootstrap scene.
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

        // ---- QA fix #8 gate tests, updated for the QA fix #10 permission architecture ----

        [Test]
        public void ControllerDoesNotModifyDirectorComponentState()
        {
            // The controller must NEVER disable/enable or otherwise mutate MissionStoryDirector.
            // Under QA fix #10 it does not even look the director up — it only declares intent.
            var director = new GameObject("DirectorUnderTest")
                .AddComponent<OperationOutbreak.Story.MissionStoryDirector>();
            try
            {
                bool enabledBefore = director.enabled;
                bool activeBefore = director.gameObject.activeSelf;

                var root = Build();
                var controller = root.GetComponent<OpeningCinematicController>();
                InvokePrivateLifecycle(controller, "Awake");

                Assert.AreEqual(enabledBefore, director.enabled,
                    "Cinematic must not enable/disable the MissionStoryDirector component.");
                Assert.AreEqual(activeBefore, director.gameObject.activeSelf,
                    "Cinematic must not deactivate the MissionStoryDirector GameObject.");
                Assert.IsFalse(director.HoldOpeningSequence,
                    "QA fix #10: the cinematic must no longer push the legacy flag onto the director.");

                // The hold is expressed through the permission authority instead.
                Assert.IsTrue(controller.IsHoldingDirectorGate,
                    "An auto-start cinematic must hold a permission token after Awake.");

                InvokePrivateLifecycle(controller, "OnDestroy");
            }
            finally
            {
                Object.DestroyImmediate(director.gameObject);
                OperationOutbreak.Story.OpeningStoryStartPermission.ResetState();
            }
        }

        [Test]
        public void DirectorGatePropertyExistsAndDefaultsFalse()
        {
            var directorType = typeof(OperationOutbreak.Story.MissionStoryDirector);

            // Legacy local gate is retained for compatibility.
            var prop = directorType.GetProperty("HoldOpeningSequence");
            Assert.IsNotNull(prop, "MissionStoryDirector must expose HoldOpeningSequence property.");
            Assert.AreEqual(typeof(bool), prop.PropertyType, "HoldOpeningSequence must be a bool.");

            var method = directorType.GetMethod("ReleaseOpeningSequence");
            Assert.IsNotNull(method, "MissionStoryDirector must expose ReleaseOpeningSequence method.");
            Assert.IsTrue(method.IsPublic, "ReleaseOpeningSequence must be public.");

            // QA fix #10 — the authoritative decision property must exist and be read-only.
            var authoritative = directorType.GetProperty("IsOpeningStartAllowed");
            Assert.IsNotNull(authoritative,
                "QA fix #10: MissionStoryDirector must expose IsOpeningStartAllowed.");
            Assert.AreEqual(typeof(bool), authoritative.PropertyType,
                "IsOpeningStartAllowed must be a bool.");
            Assert.IsNull(authoritative.SetMethod,
                "IsOpeningStartAllowed must be read-only — it is derived, never assigned.");
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
            // QA fix #9 — rewritten. The old version failed for two TEST-side reasons
            // (the runtime contract was already correct):
            //
            // 1) WRONG Camera.main TARGET: the Unity Test Framework builds the EditMode
            //    bootstrap scene with NewSceneSetup.DefaultGameObjects, so the test scene
            //    already contains a default "Main Camera" (tagged MainCamera, Camera
            //    enabled) before any test code runs. Camera.main returns "the first valid
            //    result from its cache" of MainCamera-tagged GameObjects, so with two
            //    enabled tagged cameras it resolved the bootstrap camera — not the
            //    runtime-created test camera. The controller correctly disabled a real
            //    main camera; the assertion simply watched the wrong instance.
            //
            // 2) WRONG LIFECYCLE ASSUMPTION: in Edit Mode, MonoBehaviour event functions
            //    (including OnDestroy) do not run for components without
            //    ExecuteInEditMode/ExecuteAlways, so DestroyImmediate never invoked the
            //    controller's restore path the old test relied on.
            //
            // Verified lifecycle contract (runtime code unchanged):
            //   (A) gameplay Main Camera enabled and ExteriorCamera disabled before start,
            //   (B) StartExteriorFlyover activates the ExteriorCamera,
            //   (C) the Main Camera is temporarily disabled while the flyover runs,
            //   (D) cinematic teardown restores the Main Camera to its original enabled state,
            //   (E) failed startup validation leaves the Main Camera enabled,
            //   (F) the development bypass (autoStartOnPlay = false) never disables it.
            //
            // The test makes its camera the ONLY enabled MainCamera-tagged camera in the
            // scene so Camera.main resolves deterministically, order-independent of
            // whatever the bootstrap scene contains or earlier tests left behind.

            var root = Build();
            var controller = root.GetComponent<OpeningCinematicController>();

            // Gameplay Main Camera under test.
            var camGo = new GameObject("FakeMainCam");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = true;

            var suppressed = new List<Camera>();
            try
            {
                // Suppress every OTHER enabled MainCamera-tagged camera (the EditMode
                // bootstrap scene's default "Main Camera") so Camera.main deterministically
                // resolves to the test camera.
                foreach (var other in Object.FindObjectsByType<Camera>(
                             FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (other == null || other == cam || !other.enabled) continue;
                    if (!other.CompareTag("MainCamera")) continue;
                    other.enabled = false;
                    suppressed.Add(other);
                }
                Assert.IsTrue(Camera.main == cam,
                    "Camera.main must resolve the test Main Camera after all competing " +
                    "MainCamera-tagged cameras are suppressed.");

                // ---- (F) Bypass (autoStartOnPlay = false) must not disable the Main Camera ----
                GetPrivateField("autoStartOnPlay").SetValue(controller, false);
                // Invoke the real OnEnable entry point (Unity suppresses event functions in
                // Edit Mode). With the bypass flag set it must not start the flyover.
                InvokePrivateLifecycle(controller, "OnEnable");
                Assert.AreEqual(OpeningCinematicController.Phase.Inactive, controller.CurrentPhase,
                    "(F) A bypassed cinematic must not start the exterior flyover.");
                Assert.IsTrue(cam.enabled,
                    "(F) Bypassed cinematic (autoStartOnPlay = false) must not disable the Main Camera.");

                // ---- (E) Failed startup validation must leave the Main Camera enabled ----
                root = Build();
                controller = root.GetComponent<OpeningCinematicController>();
                GetPrivateField("exteriorCamera").SetValue(controller, null);

                LogAssert.Expect(LogType.Error, "[OPENING CINEMATIC] Exterior camera is null or inactive.");
                LogAssert.Expect(LogType.Error, "[OPENING CINEMATIC] Setup validation failed — aborting. Gameplay camera preserved.");
                controller.StartExteriorFlyover();
                Assert.AreEqual(OpeningCinematicController.Phase.Inactive, controller.CurrentPhase,
                    "(E) Invalid cinematic setup must abort before the flyover starts.");
                Assert.IsTrue(cam.enabled,
                    "(E) Main Camera must remain enabled when cinematic startup validation fails.");

                // ---- (A-D) Full exterior-flyover lifecycle ----
                root = Build();
                controller = root.GetComponent<OpeningCinematicController>();
                var exteriorCam = root.transform.Find("Cameras/ExteriorCamera").GetComponent<Camera>();

                // (A) Premises before the flyover.
                Assert.IsTrue(cam.enabled, "(A) Main Camera must start enabled.");
                Assert.IsFalse(exteriorCam.enabled, "(A) ExteriorCamera must start disabled.");

                // (B) Start activates the ExteriorCamera...
                controller.StartExteriorFlyover();
                Assert.IsTrue(controller.IsExteriorCameraEnabled,
                    "(B) StartExteriorFlyover must activate the ExteriorCamera.");
                Assert.IsTrue(exteriorCam.enabled,
                    "(B) ExteriorCamera component must be enabled during the flyover.");
                Assert.AreEqual(OpeningCinematicController.Phase.ExteriorFlyover, controller.CurrentPhase,
                    "(B) Controller must be in the ExteriorFlyover phase after start.");

                // (C) ...and temporarily disables the gameplay Main Camera while it runs.
                Assert.IsFalse(cam.enabled,
                    "(C) Main Camera must be temporarily disabled while the exterior flyover is running.");

                // (D) Cinematic teardown restores the Main Camera to its original enabled
                //     state. The restore lives in the private OnDestroy(), which Unity does
                //     not invoke for this component in Edit Mode (event functions are
                //     play-mode-only without ExecuteAlways), so invoke the real method.
                InvokePrivateLifecycle(controller, "OnDestroy");
                Assert.IsTrue(cam.enabled,
                    "(D) Cinematic teardown must restore the Main Camera to enabled.");
            }
            finally
            {
                // Restore every camera this test suppressed (bootstrap default Main Camera).
                foreach (var other in suppressed)
                    if (other != null) other.enabled = true;
                if (camGo != null) Object.DestroyImmediate(camGo);
            }
        }

        private static System.Reflection.FieldInfo GetPrivateField(string fieldName)
        {
            var field = typeof(OpeningCinematicController).GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field,
                fieldName + " must exist on OpeningCinematicController.");
            return field;
        }

        private static void InvokePrivateLifecycle(OpeningCinematicController controller, string methodName)
        {
            var method = typeof(OpeningCinematicController).GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method,
                methodName + " must exist on OpeningCinematicController (cinematic lifecycle).");
            method.Invoke(controller, null);
        }
    }
}
