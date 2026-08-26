using NUnit.Framework;
using OperationOutbreak.Cinematic;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Micro task #4 — focused EditMode tests for CinematicHelicopterCameraFollow polish.
    ///
    /// Verifies:
    /// 1. Camera stays behind the target relative to targetForwardAxis.
    /// 2. Camera stays above the target.
    /// 3. Side offset remains consistent in rear three-quarter composition.
    /// 4. Cosmetic child rotation (helicopter_rigged) does not affect camera basis.
    /// 5. Target forward axis (1,0,0) is supported.
    /// 6. Camera tracking distance remains tightly bounded throughout flight.
    /// 7. No first-frame jump when snapOnStart is enabled.
    /// 8. Damping remains strictly frame-rate independent.
    /// 9. Null target is safe and does not throw or move.
    /// 10. Camera follow does not create or modify unrelated objects/cameras.
    /// 11. All requested Inspector fields are exposed.
    /// 12. Default values match the polished cinematic specifications.
    /// </summary>
    public sealed class CinematicHelicopterCameraFollowTests
    {
        private GameObject _target;
        private GameObject _cameraGo;
        private CinematicHelicopterCameraFollow _follow;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("HelicopterFlightRoot");
            _cameraGo = new GameObject("CinematicCamera");
            _follow = _cameraGo.AddComponent<CinematicHelicopterCameraFollow>();
            _follow.Target = _target.transform;
            _follow.SnapOnStart = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_cameraGo != null) Object.DestroyImmediate(_cameraGo);
            if (_target != null) Object.DestroyImmediate(_target);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(f, field + " must exist on " + target.GetType().Name + ".");
            f.SetValue(target, value);
        }

        // ---- 1. Camera stays behind target ----

        [Test]
        public void CameraStaysBehindTargetRelativeToForwardAxis()
        {
            _target.transform.position = new Vector3(5f, 10f, 15f);
            _target.transform.rotation = Quaternion.identity;

            _follow.UpdateFollow(1f / 60f);

            Vector3 toCamera = _cameraGo.transform.position - _target.transform.position;
            Vector3 forward = _target.transform.TransformDirection(new Vector3(1f, 0f, 0f)).normalized;

            float dot = Vector3.Dot(toCamera, forward);
            Assert.Less(dot, -5f, "Camera must sit behind the target along the forward travel axis.");
        }

        // ---- 2. Camera stays above target ----

        [Test]
        public void CameraStaysAboveTarget()
        {
            _target.transform.position = new Vector3(0f, 5f, 0f);
            _target.transform.rotation = Quaternion.identity;

            _follow.UpdateFollow(1f / 60f);

            Vector3 toCamera = _cameraGo.transform.position - _target.transform.position;
            Vector3 up = _target.transform.TransformDirection(Vector3.up).normalized;

            float dot = Vector3.Dot(toCamera, up);
            Assert.Greater(dot, 2f, "Camera must sit above the target.");
        }

        // ---- 3. Side offset remains consistent ----

        [Test]
        public void SideOffsetRemainsConsistentInRearThreeQuarter()
        {
            _target.transform.position = Vector3.zero;
            _target.transform.rotation = Quaternion.identity;

            _follow.UpdateFollow(1f / 60f);

            Vector3 toCamera = _cameraGo.transform.position - _target.transform.position;
            Vector3 forward = _target.transform.TransformDirection(new Vector3(1f, 0f, 0f)).normalized;
            Vector3 up = _target.transform.TransformDirection(Vector3.up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;

            float sideDot = Vector3.Dot(toCamera, right);
            Assert.Greater(sideDot, 2f, "Camera must be offset to the side, forming a stable 3/4 composition.");
        }

        // ---- 4. Child rotation does not affect camera basis ----

        [Test]
        public void CosmeticChildRotationDoesNotAffectCameraBasis()
        {
            var childModel = new GameObject("helicopter_rigged");
            childModel.transform.SetParent(_target.transform, false);

            try
            {
                // Baseline with neutral child
                _follow.UpdateFollow(1f / 60f);
                Vector3 baselinePos = _cameraGo.transform.position;
                Quaternion baselineRot = _cameraGo.transform.rotation;

                // Heavily rotate and translate child model (mimicking pitch, bob, roll)
                childModel.transform.localPosition = new Vector3(1f, -0.5f, 2f);
                childModel.transform.localRotation = Quaternion.Euler(30f, 45f, -60f);

                _follow.RequestSnap();
                _follow.UpdateFollow(1f / 60f);

                Assert.AreEqual(baselinePos.x, _cameraGo.transform.position.x, 1e-4f,
                    "Child model transform must not affect camera follow position.");
                Assert.AreEqual(baselinePos.y, _cameraGo.transform.position.y, 1e-4f,
                    "Child model transform must not affect camera follow position.");
                Assert.AreEqual(baselinePos.z, _cameraGo.transform.position.z, 1e-4f,
                    "Child model transform must not affect camera follow position.");
                Assert.Less(Quaternion.Angle(baselineRot, _cameraGo.transform.rotation), 1e-4f,
                    "Child model transform must not affect camera follow rotation.");
            }
            finally
            {
                Object.DestroyImmediate(childModel);
            }
        }

        // ---- 5. Target forward axis (1,0,0) supported ----

        [Test]
        public void TargetForwardAxisXSupportedAndPreservesRearPerspective()
        {
            _target.transform.position = Vector3.zero;
            _target.transform.rotation = Quaternion.identity;

            // Default targetForwardAxis is (1, 0, 0)
            _follow.UpdateFollow(1f / 60f);

            // Along +X travel, camera must be at negative X
            Assert.Less(_cameraGo.transform.position.x, -8f,
                "With targetForwardAxis=(1,0,0), camera X must be negative (behind the helicopter).");
        }

        // ---- 6. Camera tracking distance remains bounded ----

        [Test]
        public void CameraTrackingDistanceRemainsBoundedDuringFlight()
        {
            var flight = _target.AddComponent<CinematicHelicopterFlight>();
            SetPrivate(flight, "localForwardAxis", new Vector3(1f, 0f, 0f));

            // Initial snap
            _follow.UpdateFollow(1f / 60f);

            for (int i = 0; i < 600; i++)
            {
                flight.AdvanceFlight(1f / 60f);
                _follow.UpdateFollow(1f / 60f);

                float dist = Vector3.Distance(_cameraGo.transform.position, _target.transform.position);
                Assert.Less(dist, 16f, "Camera must not fall behind or allow target to shrink excessively.");
                Assert.Greater(dist, 8f, "Camera must maintain safe distance from target.");
            }
        }

        // ---- 7. No first-frame jump when snapOnStart enabled ----

        [Test]
        public void NoFirstFrameJumpWhenSnapOnStartEnabled()
        {
            _target.transform.position = new Vector3(20f, 0f, -10f);
            _cameraGo.transform.position = new Vector3(500f, 500f, 500f); // far away parked position

            _follow.SnapOnStart = true;
            _follow.UpdateFollow(1f / 60f);

            float dist = Vector3.Distance(_cameraGo.transform.position, _target.transform.position);
            Assert.Less(dist, 15f, "Camera must snap directly to ideal follow pose on first update.");
        }

        // ---- 8. Damping is framerate independent ----

        [Test]
        public void DampingRemainsFramerateIndependent()
        {
            // Two identical camera rigs chasing a moving target
            var camA = new GameObject("CamA");
            var camB = new GameObject("CamB");
            try
            {
                var followA = camA.AddComponent<CinematicHelicopterCameraFollow>();
                var followB = camB.AddComponent<CinematicHelicopterCameraFollow>();
                followA.Target = _target.transform;
                followB.Target = _target.transform;

                // Snap both to start
                followA.UpdateFollow(1f / 60f);
                followB.UpdateFollow(1f / 60f);

                // Move target
                _target.transform.position = new Vector3(5f, 1f, 0f);

                // FollowA takes 1 step of 0.04s (25 fps)
                followA.UpdateFollow(0.04f);

                // FollowB takes 2 steps of 0.02s (50 fps)
                followB.UpdateFollow(0.02f);
                followB.UpdateFollow(0.02f);

                float diff = Vector3.Distance(camA.transform.position, camB.transform.position);
                Assert.Less(diff, 1e-4f, "Exponential damping must produce identical results across different framerates.");
            }
            finally
            {
                Object.DestroyImmediate(camA);
                Object.DestroyImmediate(camB);
            }
        }

        // ---- 9. Null target is safe ----

        [Test]
        public void NullTargetIsSafeAndDoesNotThrowOrMove()
        {
            _follow.Target = null;
            _cameraGo.transform.position = new Vector3(12f, 34f, 56f);
            Quaternion rot = _cameraGo.transform.rotation;

            Assert.DoesNotThrow(() => _follow.UpdateFollow(1f / 60f));
            Assert.AreEqual(new Vector3(12f, 34f, 56f), _cameraGo.transform.position);
            Assert.AreEqual(rot, _cameraGo.transform.rotation);
        }

        // ---- 10. Does not create or modify unrelated objects ----

        [Test]
        public void CameraScriptDoesNotCreateOrDisableAnyCameraOrObjects()
        {
            int objectCountBefore = Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            var cam = _cameraGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;

            for (int i = 0; i < 30; i++)
            {
                _follow.UpdateFollow(1f / 60f);
            }

            int objectCountAfter = Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            Assert.AreEqual(objectCountBefore, objectCountAfter, "Camera follow must not create additional game objects.");
            Assert.AreEqual(60f, cam.fieldOfView, 1e-4f, "Camera follow must not alter FOV.");
            Assert.IsTrue(cam.enabled, "Camera follow must not disable camera component.");
        }

        // ---- 11. Exposes requested Inspector fields ----

        [Test]
        public void ExposesRequestedInspectorFields()
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            var t = typeof(CinematicHelicopterCameraFollow);
            foreach (var name in new[]
                     {
                         "target", "followDistance", "heightOffset", "sideOffset",
                         "lookAheadDistance", "lookHeight", "positionDamping", "rotationDamping",
                         "targetForwardAxis", "targetUpAxis", "stableRearThreeQuarter",
                         "enableTakeoffTransition", "takeoffCameraHoldDuration", "takeoffCameraBlendDuration",
                         "snapOnStart", "followEnabled", "useUnscaledTime"
                     })
            {
                Assert.IsNotNull(t.GetField(name, F), name + " must be Inspector-exposed.");
            }
        }

        // ---- 12. Default values match polished specification ----

        [Test]
        public void DefaultValuesMatchPolishedSpec()
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            var t = typeof(CinematicHelicopterCameraFollow);

            float dist = (float)t.GetField("followDistance", F).GetValue(_follow);
            Assert.GreaterOrEqual(dist, 10f);
            Assert.LessOrEqual(dist, 12f);

            float height = (float)t.GetField("heightOffset", F).GetValue(_follow);
            Assert.GreaterOrEqual(height, 3f);
            Assert.LessOrEqual(height, 4f);

            float side = (float)t.GetField("sideOffset", F).GetValue(_follow);
            Assert.GreaterOrEqual(side, 3f);
            Assert.LessOrEqual(side, 4f);

            float lookAhead = (float)t.GetField("lookAheadDistance", F).GetValue(_follow);
            Assert.GreaterOrEqual(lookAhead, 1f);
            Assert.LessOrEqual(lookAhead, 3f);

            float lookH = (float)t.GetField("lookHeight", F).GetValue(_follow);
            Assert.AreEqual(1f, lookH, 1e-4f);

            float posDamp = (float)t.GetField("positionDamping", F).GetValue(_follow);
            Assert.GreaterOrEqual(posDamp, 4f);
            Assert.LessOrEqual(posDamp, 6f);

            float rotDamp = (float)t.GetField("rotationDamping", F).GetValue(_follow);
            Assert.GreaterOrEqual(rotDamp, 4f);
            Assert.LessOrEqual(rotDamp, 6f);

            Vector3 fwd = (Vector3)t.GetField("targetForwardAxis", F).GetValue(_follow);
            Assert.AreEqual(new Vector3(1f, 0f, 0f), fwd);

            Vector3 up = (Vector3)t.GetField("targetUpAxis", F).GetValue(_follow);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), up);

            bool stable = (bool)t.GetField("stableRearThreeQuarter", F).GetValue(_follow);
            Assert.IsTrue(stable);

            float holdDur = (float)t.GetField("takeoffCameraHoldDuration", F).GetValue(_follow);
            Assert.AreEqual(1.2f, holdDur, 1e-4f);

            float blendDur = (float)t.GetField("takeoffCameraBlendDuration", F).GetValue(_follow);
            Assert.AreEqual(2.5f, blendDur, 1e-4f);

            bool takeoffTransition = (bool)t.GetField("enableTakeoffTransition", F).GetValue(_follow);
            Assert.IsTrue(takeoffTransition);

            bool snap = (bool)t.GetField("snapOnStart", F).GetValue(_follow);
            Assert.IsFalse(snap, "snapOnStart must default to false for the cinematic takeoff presentation.");
        }

        // ---- Micro Task #5: Takeoff Camera Transition Tests ----

        [Test]
        public void CameraPreservesAuthoredShotDuringGroundHold()
        {
            var cam = new GameObject("TestCam");
            try
            {
                var authoredPos = new Vector3(-8f, 1.5f, -6f);
                var authoredRot = Quaternion.Euler(5f, 35f, 0f);
                cam.transform.SetPositionAndRotation(authoredPos, authoredRot);

                var follow = cam.AddComponent<CinematicHelicopterCameraFollow>();
                follow.Target = _target.transform;
                follow.SnapOnStart = false;
                follow.EnableTakeoffTransition = true;
                follow.TakeoffCameraHoldDuration = 1.2f;

                // Step 1.0s (within hold phase)
                for (float t = 0f; t < 1.0f; t += 1f / 60f)
                {
                    follow.UpdateFollow(1f / 60f);
                }

                Assert.AreEqual(0f, follow.TakeoffBlendWeight, 1e-4f, "Blend weight must be zero during ground hold.");
                Assert.AreEqual(authoredPos.x, cam.transform.position.x, 1e-4f, "Position must match authored ground shot.");
                Assert.AreEqual(authoredPos.y, cam.transform.position.y, 1e-4f, "Position must match authored ground shot.");
                Assert.AreEqual(authoredPos.z, cam.transform.position.z, 1e-4f, "Position must match authored ground shot.");
                Assert.Less(Quaternion.Angle(authoredRot, cam.transform.rotation), 1e-4f, "Rotation must match authored ground shot.");
            }
            finally
            {
                Object.DestroyImmediate(cam);
            }
        }

        [Test]
        public void CameraSmoothlyBlendsFromAuthoredShotToChaseWithoutJump()
        {
            var cam = new GameObject("TestCam");
            try
            {
                var authoredPos = new Vector3(-8f, 1.5f, -6f);
                var authoredRot = Quaternion.Euler(5f, 35f, 0f);
                cam.transform.SetPositionAndRotation(authoredPos, authoredRot);

                var follow = cam.AddComponent<CinematicHelicopterCameraFollow>();
                follow.Target = _target.transform;
                follow.SnapOnStart = false;
                follow.EnableTakeoffTransition = true;
                follow.TakeoffCameraHoldDuration = 1.2f;
                follow.TakeoffCameraBlendDuration = 2.5f;

                // Step to mid-blend: 1.2s hold + 1.25s (half of blend) = 2.45s
                for (float t = 0f; t < 2.45f; t += 1f / 60f)
                {
                    follow.UpdateFollow(1f / 60f);
                }

                Assert.Greater(follow.TakeoffBlendWeight, 0.2f, "Blend weight must progress during transition.");
                Assert.Less(follow.TakeoffBlendWeight, 0.8f, "Blend weight must be mid-way during transition.");

                // Camera position must be intermediate between authored and chase pose
                Assert.AreNotEqual(authoredPos, cam.transform.position, "Camera must have moved away from authored shot.");
            }
            finally
            {
                Object.DestroyImmediate(cam);
            }
        }

        [Test]
        public void CameraReachesFullChaseFramingAfterBlend()
        {
            var cam = new GameObject("TestCam");
            try
            {
                var authoredPos = new Vector3(-8f, 1.5f, -6f);
                cam.transform.position = authoredPos;

                var follow = cam.AddComponent<CinematicHelicopterCameraFollow>();
                follow.Target = _target.transform;
                follow.SnapOnStart = false;
                follow.EnableTakeoffTransition = true;
                follow.TakeoffCameraHoldDuration = 1.2f;
                follow.TakeoffCameraBlendDuration = 2.5f;

                // Step past blend window: 1.2s + 2.5s + 0.5s = 4.2s
                for (float t = 0f; t < 4.2f; t += 1f / 60f)
                {
                    follow.UpdateFollow(1f / 60f);
                }

                Assert.AreEqual(1f, follow.TakeoffBlendWeight, 1e-4f, "Blend weight must saturate at 1.0 after blend duration.");

                // Distance to target should now match chase follow distance (~11-12m)
                float dist = Vector3.Distance(cam.transform.position, _target.transform.position);
                Assert.Less(dist, 14f, "Camera must be in full chase follow mode.");
                Assert.Greater(dist, 10f, "Camera must maintain rear 3/4 follow distance.");
            }
            finally
            {
                Object.DestroyImmediate(cam);
            }
        }

        // ---- Micro Task #5A: runtime authored-shot regression ----
        // With Snap On Start = false the camera must preserve its authored transform throughout
        // the entire GroundIdle phase, even when a NEW Play session starts without a domain/scene
        // reload (Unity Enter Play Mode Options) and stale chase state persists from the previous
        // session.

        [Test]
        public void NewPlaySessionPreservesAuthoredCameraTransformThroughGroundIdle()
        {
            var cam = new GameObject("TestCam");
            try
            {
                var authoredPos = new Vector3(-8f, 1.5f, -6f);
                var authoredRot = Quaternion.Euler(5f, 35f, 0f);
                cam.transform.SetPositionAndRotation(authoredPos, authoredRot);

                var follow = cam.AddComponent<CinematicHelicopterCameraFollow>();
                follow.Target = _target.transform;
                follow.SnapOnStart = false;
                follow.EnableTakeoffTransition = true;
                follow.TakeoffCameraHoldDuration = 1.2f;

                // First Play session: run well past the hold into full chase so the camera leaves
                // the authored shot (stale state a second session would inherit).
                for (float t = 0f; t < 4.2f; t += 1f / 60f)
                {
                    follow.UpdateFollow(1f / 60f);
                }

                Assert.AreEqual(1f, follow.TakeoffBlendWeight, 1e-4f, "Sanity: first session reached full chase.");
                Assert.AreNotEqual(authoredPos, cam.transform.position, "Sanity: camera left the authored shot.");

                // Between sessions the user re-positions the camera in the Scene (edit mode).
                cam.transform.SetPositionAndRotation(authoredPos, authoredRot);

                // Unity enters Play Mode again without a domain/scene reload: the session counter
                // is bumped (RuntimeInitializeOnLoadMethod) and OnEnable is invoked on the camera.
                var t = typeof(CinematicHelicopterCameraFollow);
                var counter = t.GetField("_sessionCounter",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                counter.SetValue(null, (int)counter.GetValue(null) + 1);
                var onEnable = t.GetMethod("OnEnable",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                onEnable.Invoke(follow, null);

                // Whole GroundIdle hold (1.2s): the camera must stay EXACTLY on the authored shot.
                for (float t = 0f; t < 1.0f; t += 1f / 60f)
                {
                    follow.UpdateFollow(1f / 60f);
                }

                Assert.AreEqual(0f, follow.TakeoffBlendWeight, 1e-4f,
                    "Blend weight must be zero during the new session's ground hold.");
                Assert.AreEqual(authoredPos.x, cam.transform.position.x, 1e-4f,
                    "Camera X must match the authored ground shot.");
                Assert.AreEqual(authoredPos.y, cam.transform.position.y, 1e-4f,
                    "Camera Y must match the authored ground shot.");
                Assert.AreEqual(authoredPos.z, cam.transform.position.z, 1e-4f,
                    "Camera Z must match the authored ground shot.");
                Assert.Less(Quaternion.Angle(authoredRot, cam.transform.rotation), 1e-4f,
                    "Camera rotation must match the authored ground shot.");
            }
            finally
            {
                Object.DestroyImmediate(cam);
            }
        }
    }
}
