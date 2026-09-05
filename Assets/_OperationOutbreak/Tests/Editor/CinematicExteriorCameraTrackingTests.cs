using NUnit.Framework;
using OperationOutbreak.Cinematic;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Tests for <see cref="CinematicExteriorCameraTracking"/> — the exterior camera must be an
    /// INDEPENDENT camera position (never parented to / rigidly carried by HelicopterFlightRoot),
    /// preserve the authored opening composition within its configured limits, and keep the
    /// helicopter visible in the intended shot while it flies through the frame.
    /// </summary>
    public sealed class CinematicExteriorCameraTrackingTests
    {
        private GameObject _flightGo;
        private GameObject _camGo;

        [SetUp]
        public void SetUp()
        {
            _flightGo = new GameObject("HelicopterFlightRoot");
            _camGo = new GameObject("ExteriorCamera");
        }

        [TearDown]
        public void TearDown()
        {
            if (_flightGo != null) Object.DestroyImmediate(_flightGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
        }

        private const int Frames = 600; // 10 seconds at 60fps
        private const float Dt = 1f / 60f;

        /// <summary>
        /// Standard fixture: an airborne-start helicopter at (-18, 3, 0) flying +X at 8 m/s (the
        /// current exterior configuration), and an INDEPENDENT camera at (-25, 4, -6) whose
        /// authored composition looks down the flight path — the helicopter starts just outside
        /// the frame and flies through it.
        /// </summary>
        private (CinematicHelicopterFlight, CinematicExteriorCameraTracking, Vector3, Quaternion) MakeFixture()
        {
            _flightGo.transform.position = new Vector3(-18f, 3f, 0f);
            var flight = _flightGo.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;

            Vector3 authoredPos = new Vector3(-25f, 4f, -6f);
            Quaternion authoredRot = Quaternion.LookRotation(new Vector3(25f, -1f, 6f));
            _camGo.transform.SetPositionAndRotation(authoredPos, authoredRot);

            var tracking = _camGo.AddComponent<CinematicExteriorCameraTracking>();
            tracking.Target = flight.transform;
            _camGo.AddComponent<Camera>(); // a real camera component; no camera settings are changed

            return (flight, tracking, authoredPos, authoredRot);
        }

        [Test]
        public void ExteriorCameraIsNeverAChildOfTheFlightRoot()
        {
            var (flight, tracking, _, _) = MakeFixture();
            for (int i = 0; i < Frames; i++)
            {
                flight.AdvanceFlight(Dt);
                tracking.UpdateTracking(Dt);
                Assert.IsNull(_camGo.transform.parent,
                    "The exterior camera must never be parented to the flight root (frame " + (i + 1) + ").");
                Assert.AreNotEqual(_flightGo.transform, _camGo.transform.parent,
                    "The exterior camera must never be a child of HelicopterFlightRoot (frame " + (i + 1) + ").");
            }
        }

        [Test]
        public void CameraPreservesAuthoredCompositionWithinConfiguredLimits()
        {
            var (flight, tracking, authoredPos, authoredRot) = MakeFixture();
            for (int i = 0; i < Frames; i++)
            {
                flight.AdvanceFlight(Dt);
                tracking.UpdateTracking(Dt);
                Vector3 p = _camGo.transform.position;
                Assert.LessOrEqual(Vector3.Distance(p, authoredPos), tracking.MaxPositionDrift + 1e-3f,
                    "The camera must never drift more than maxPositionDrift from its authored position (frame " + (i + 1) + ").");
                Assert.LessOrEqual(Quaternion.Angle(_camGo.transform.rotation, authoredRot),
                    tracking.MaxTrackingAngle + 0.05f,
                    "The camera must never turn more than maxTrackingAngle from its authored orientation (frame " + (i + 1) + ").");
            }
        }

        [Test]
        public void TrackingIsGentleWithNoRotationSnap()
        {
            var (flight, tracking, _, _) = MakeFixture();
            tracking.UpdateTracking(Dt); // capture the authored pose
            Quaternion prev = _camGo.transform.rotation;
            for (int i = 1; i < Frames; i++)
            {
                flight.AdvanceFlight(Dt);
                tracking.UpdateTracking(Dt);
                float delta = Quaternion.Angle(prev, _camGo.transform.rotation);
                Assert.Less(delta, 0.5f,
                    "Per-frame rotation must be gentle (no snap, independent-camera feel) (frame " + (i + 1) + ").");
                prev = _camGo.transform.rotation;
            }
        }

        [Test]
        public void TrackingIsRealNotAFrozenStaticCamera()
        {
            var (flight, tracking, _, authoredRot) = MakeFixture();
            for (int i = 0; i < Frames; i++)
            {
                flight.AdvanceFlight(Dt);
                tracking.UpdateTracking(Dt);
            }
            float totalOff = Quaternion.Angle(_camGo.transform.rotation, authoredRot);
            Assert.Greater(totalOff, 2f,
                "The camera must actually turn toward the helicopter over the flight (gentle look-at), not be frozen.");
            Assert.Less(totalOff, tracking.MaxTrackingAngle + 0.05f,
                "...and the total turn must still respect the tracking limit.");
        }

        [Test]
        public void HelicopterRemainsVisibleInTheShotAfterEnteringTheFrame()
        {
            var (flight, tracking, _, _) = MakeFixture();
            bool entered = false;
            for (int i = 0; i < Frames; i++)
            {
                flight.AdvanceFlight(Dt);
                tracking.UpdateTracking(Dt);
                Vector3 toHeli = _flightGo.transform.position - _camGo.transform.position;
                float offset = Vector3.Angle(_camGo.transform.forward, toHeli);
                if (!entered)
                {
                    if (offset < 25f) entered = true; // inside the intended shot (50-degree-FOV half-angle)
                }
                else
                {
                    Assert.Less(offset, 25f,
                        "Once inside the frame, the helicopter must stay in the intended shot (frame " + (i + 1) + ").");
                }
            }
            Assert.IsTrue(entered, "The helicopter must enter the camera frame during the exterior flight.");
        }

        [Test]
        public void CameraHoldsAuthoredPoseWhenThereIsNoTarget()
        {
            var (flight, tracking, authoredPos, authoredRot) = MakeFixture();
            tracking.Target = null;
            for (int i = 0; i < 120; i++)
            {
                flight.AdvanceFlight(Dt);
                Assert.DoesNotThrow(() => tracking.UpdateTracking(Dt),
                    "A missing target must be safe (frame " + (i + 1) + ").");
                Assert.AreEqual(authoredPos, _camGo.transform.position,
                    "No target: the camera position stays exactly as authored (frame " + (i + 1) + ").");
                Assert.AreEqual(authoredRot, _camGo.transform.rotation,
                    "No target: the camera rotation stays exactly as authored (frame " + (i + 1) + ").");
            }
        }

        [Test]
        public void ZeroAndNegativeDeltaTimeAreSafe()
        {
            var (flight, tracking, authoredPos, authoredRot) = MakeFixture();
            Assert.DoesNotThrow(() => tracking.UpdateTracking(0f), "Zero dt must be safe.");
            Assert.DoesNotThrow(() => tracking.UpdateTracking(-1f), "Negative dt must be safe.");
            Assert.AreEqual(authoredPos, _camGo.transform.position, "Zero/negative dt must not move the camera.");
            Assert.AreEqual(authoredRot, _camGo.transform.rotation, "Zero/negative dt must not rotate the camera.");
        }

        [Test]
        public void DisabledTrackingFreezesTheCameraExactly()
        {
            var (flight, tracking, _, _) = MakeFixture();
            for (int i = 0; i < 60; i++)
            {
                flight.AdvanceFlight(Dt);
                tracking.UpdateTracking(Dt);
            }
            Vector3 frozenPos = _camGo.transform.position;
            Quaternion frozenRot = _camGo.transform.rotation;

            tracking.TrackingEnabled = false;
            for (int i = 0; i < 120; i++)
            {
                flight.AdvanceFlight(Dt);
                tracking.UpdateTracking(Dt);
                Assert.AreEqual(frozenPos, _camGo.transform.position,
                    "Disabled tracking must not move the camera (frame " + (i + 1) + ").");
                Assert.AreEqual(frozenRot, _camGo.transform.rotation,
                    "Disabled tracking must not rotate the camera (frame " + (i + 1) + ").");
            }
        }
    }
}
