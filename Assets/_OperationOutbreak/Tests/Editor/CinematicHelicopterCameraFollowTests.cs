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
        }
    }
}
