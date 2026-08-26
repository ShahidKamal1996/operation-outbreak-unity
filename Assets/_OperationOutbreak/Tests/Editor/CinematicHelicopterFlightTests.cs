using NUnit.Framework;
using OperationOutbreak.Cinematic;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Micro task #2 — lightweight structural tests for straight takeoff + camera follow.
    ///
    /// These cover the manual QA points that can be checked deterministically: no start jump,
    /// no rotation snap, the hold phase, gradual acceleration, straight-line travel, gentle rise,
    /// and continuous camera following. Visual approval is still the real acceptance gate.
    /// </summary>
    public sealed class CinematicHelicopterFlightTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("HelicopterFlightRoot");

        [TearDown]
        public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(f, field + " must exist on " + target.GetType().Name + ".");
            f.SetValue(target, value);
        }

        /// <summary>Steps the flight in small fixed increments, mimicking a real frame loop.</summary>
        private static void Simulate(CinematicHelicopterFlight flight, float seconds, float step = 1f / 60f)
        {
            for (float t = 0f; t < seconds; t += step) flight.AdvanceFlight(step);
        }

        // ---- flight: startup safety ----

        [Test]
        public void DoesNotMoveOrRotateOnFirstFrame()
        {
            // QA points 6 and 7: no sudden position jump, no sudden rotation snap.
            _root.transform.position = new Vector3(12f, 3f, -40f);
            _root.transform.rotation = Quaternion.Euler(0f, 35f, 0f);

            Vector3 pos = _root.transform.position;
            Quaternion rot = _root.transform.rotation;

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.AdvanceFlight(1f / 60f);

            Assert.AreEqual(pos.x, _root.transform.position.x, 1e-4f, "No X jump on the first frame.");
            Assert.AreEqual(pos.y, _root.transform.position.y, 1e-4f, "No Y jump on the first frame.");
            Assert.AreEqual(pos.z, _root.transform.position.z, 1e-4f, "No Z jump on the first frame.");
            Assert.Less(Quaternion.Angle(rot, _root.transform.rotation), 0.01f,
                "No rotation snap on the first frame.");
        }

        [Test]
        public void StaysStillDuringStartDelay()
        {
            // QA point 2: the helicopter waits briefly.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            SetPrivate(flight, "startDelay", 0.75f);

            Simulate(flight, 0.7f);

            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f,
                "The helicopter must remain stationary during the start delay.");
            Assert.AreEqual(0f, flight.HeightGained, 1e-4f,
                "The helicopter must not rise during the start delay.");
        }

        [Test]
        public void AccelerationIsGradualNotInstant()
        {
            // QA point 3: gradual acceleration.
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            SetPrivate(flight, "startDelay", 0.75f);
            SetPrivate(flight, "verticalLiftDuration", 0f);
            SetPrivate(flight, "accelerationDuration", 2.5f);
            SetPrivate(flight, "cruiseSpeed", 8f);

            Simulate(flight, 0.85f);           // just after the hold
            float early = flight.CurrentSpeed;

            Simulate(flight, 1.2f);            // mid acceleration
            float mid = flight.CurrentSpeed;

            Assert.Less(early, 8f * 0.25f, "Speed must start low, not snap to cruise.");
            Assert.Greater(mid, early, "Speed must build progressively.");
            Assert.Less(mid, 8f, "Speed must not exceed cruise during acceleration.");
        }

        [Test]
        public void ReachesCruiseSpeedAfterAccelerationWindow()
        {
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            SetPrivate(flight, "startDelay", 0.75f);
            SetPrivate(flight, "verticalLiftDuration", 0f);
            SetPrivate(flight, "accelerationDuration", 2.5f);
            SetPrivate(flight, "cruiseSpeed", 8f);

            Simulate(flight, 4f);

            Assert.IsTrue(flight.IsCruising, "The flight must reach the cruise phase.");
            Assert.AreEqual(8f, flight.CurrentSpeed, 0.01f, "Cruise speed must be reached.");
            Assert.AreEqual(1f, flight.SpeedFactor, 0.001f, "Speed factor must saturate at 1.");
        }

        // ---- Micro Task #5: Explicit 4-Phase Takeoff Tests ----

        [Test]
        public void GroundIdleKeepsHelicopterCompletelyStationary()
        {
            _root.transform.position = new Vector3(10f, 0f, 20f);
            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            Simulate(flight, 1.0f);

            Assert.AreEqual(FlightPhase.GroundIdle, flight.CurrentPhase);
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f, "No forward distance during GroundIdle.");
            Assert.AreEqual(0f, flight.HeightGained, 1e-4f, "No vertical lift during GroundIdle.");
            Assert.AreEqual(0f, flight.CurrentSpeed, 1e-4f, "Speed must be zero during GroundIdle.");
        }

        [Test]
        public void VerticalLiftRisesToTargetHeightWithZeroForwardDisplacement()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            // GroundIdle is 1.2s, VerticalLift is 1.8s. Total to end of lift = 3.0s.
            Simulate(flight, 3.0f);

            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f,
                "Forward displacement must remain strictly zero throughout VerticalLift.");
            Assert.AreEqual(1.75f, flight.HeightGained, 0.01f,
                "Helicopter must rise to initialLiftHeight (1.75m) at end of VerticalLift.");
            Assert.AreEqual(0f, flight.CurrentSpeed, 1e-4f,
                "Forward speed must remain zero throughout VerticalLift.");
        }

        [Test]
        public void ForwardTransitionAcceleratesOnlyAfterVerticalLift()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            // Simulate to end of VerticalLift (3.0s)
            Simulate(flight, 3.0f);
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f);

            // Step into ForwardTransition (3.0s to 5.5s)
            Simulate(flight, 1.25f); // t = 4.25s (mid acceleration)
            Assert.AreEqual(FlightPhase.ForwardTransition, flight.CurrentPhase);
            Assert.Greater(flight.DistanceTravelled, 0.5f, "Forward travel begins during ForwardTransition.");
            Assert.Greater(flight.CurrentSpeed, 2f, "Forward speed builds during ForwardTransition.");
            Assert.Greater(flight.HeightGained, 1.75f, "Climb continues above initial lift height.");

            // Step to Cruise (t > 5.5s)
            Simulate(flight, 2.0f); // t = 6.25s
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase);
            Assert.AreEqual(8f, flight.CurrentSpeed, 0.01f, "Full cruise speed reached.");
        }

        [Test]
        public void FlightPhasesEnumReportsCorrectPhaseContinuity()
        {
            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            flight.AdvanceFlight(0.5f);
            Assert.AreEqual(FlightPhase.GroundIdle, flight.CurrentPhase);

            flight.AdvanceFlight(1.0f); // t = 1.5s
            Assert.AreEqual(FlightPhase.VerticalLift, flight.CurrentPhase);

            flight.AdvanceFlight(2.0f); // t = 3.5s
            Assert.AreEqual(FlightPhase.ForwardTransition, flight.CurrentPhase);

            flight.AdvanceFlight(3.0f); // t = 6.5s
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase);
        }

        // ---- Micro Task #5A: runtime start-transform regressions ----
        // The helicopter must ALWAYS begin from its authored scene transform at the exact moment
        // Play begins: no teleport to world origin, zero displacement during GroundIdle, and a
        // vertical lift that starts from the captured authored position.

        [Test]
        public void PreservesAuthoredNonZeroRootPositionAndRotationOnFirstFrame()
        {
            // Regression 1 + 2: a non-zero authored root position AND rotation must be preserved
            // verbatim on the very first frame — nothing may snap toward world origin.
            var authoredPos = new Vector3(28f, 3.5f, -47f);
            var authoredRot = Quaternion.Euler(0f, 35f, 0f);
            _root.transform.SetPositionAndRotation(authoredPos, authoredRot);

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.AdvanceFlight(1f / 60f);

            Assert.AreEqual(authoredPos.x, _root.transform.position.x, 1e-4f,
                "Authored X must be preserved on the first frame.");
            Assert.AreEqual(authoredPos.y, _root.transform.position.y, 1e-4f,
                "Authored Y must be preserved on the first frame.");
            Assert.AreEqual(authoredPos.z, _root.transform.position.z, 1e-4f,
                "Authored Z must be preserved on the first frame.");
            Assert.Less(Quaternion.Angle(authoredRot, _root.transform.rotation), 0.001f,
                "Authored rotation must be preserved on the first frame.");
            Assert.Greater(Vector3.Distance(_root.transform.position, Vector3.zero), 20f,
                "The root must remain far from world origin exactly as authored.");
        }

        [Test]
        public void NeverTeleportsToWorldOriginDuringGroundIdle()
        {
            // Regression 5: even though the authored position is far from the origin, every frame
            // of the whole GroundIdle phase must keep the root EXACTLY at the authored position.
            var authoredPos = new Vector3(25f, 4f, -60f);
            _root.transform.position = authoredPos;

            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            for (float t = 0f; t < 1.2f; t += 1f / 60f)
            {
                flight.AdvanceFlight(1f / 60f);

                Assert.Greater(Vector3.Distance(_root.transform.position, Vector3.zero), 20f,
                    "The helicopter must never teleport toward world origin (t=" + t + ").");
            }

            Assert.AreEqual(authoredPos.x, _root.transform.position.x, 1e-4f);
            Assert.AreEqual(authoredPos.y, _root.transform.position.y, 1e-4f);
            Assert.AreEqual(authoredPos.z, _root.transform.position.z, 1e-4f);
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f, "No forward distance during GroundIdle.");
            Assert.AreEqual(0f, flight.HeightGained, 1e-4f, "No height gained during GroundIdle.");
        }

        [Test]
        public void GroundIdleProducesExactlyZeroDisplacement()
        {
            // Regression 3: for the ENTIRE GroundIdle phase (1.2s), the root position and rotation
            // must remain exactly the captured authored transform. The child model's local
            // transform must also remain untouched (regression 6).
            var authoredPos = new Vector3(10f, 0f, 20f);
            var authoredRot = Quaternion.Euler(0f, 30f, 0f);
            _root.transform.SetPositionAndRotation(authoredPos, authoredRot);

            var model = new GameObject("helicopter_rigged");
            model.transform.SetParent(_root.transform, false);
            model.transform.localPosition = new Vector3(0.1f, 0.2f, 0.3f);
            model.transform.localRotation = Quaternion.Euler(0f, 15f, 0f);
            var childPos = model.transform.localPosition;
            var childRot = model.transform.localRotation;

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            Simulate(flight, 1.18f); // comfortably inside the 1.2s GroundIdle window

            Assert.AreEqual(FlightPhase.GroundIdle, flight.CurrentPhase,
                "Phase must still be GroundIdle within the idle window.");
            Assert.AreEqual(authoredPos.x, _root.transform.position.x, 1e-4f, "Zero X displacement.");
            Assert.AreEqual(authoredPos.y, _root.transform.position.y, 1e-4f, "Zero Y displacement.");
            Assert.AreEqual(authoredPos.z, _root.transform.position.z, 1e-4f, "Zero Z displacement.");
            Assert.Less(Quaternion.Angle(authoredRot, _root.transform.rotation), 0.001f,
                "Zero rotation displacement during GroundIdle.");
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f);
            Assert.AreEqual(0f, flight.HeightGained, 1e-4f);

            Assert.AreEqual(childPos, model.transform.localPosition,
                "Flight must not change the child model's local position during GroundIdle.");
            Assert.AreEqual(childRot, model.transform.localRotation,
                "Flight must not change the child model's local rotation during GroundIdle.");

            Object.DestroyImmediate(model);
        }

        [Test]
        public void VerticalLiftBeginsFromCapturedAuthoredPosition()
        {
            // Regression 4: once GroundIdle ends, the lift must be PURELY vertical from the
            // captured authored position — horizontal coordinates stay fixed, height rises
            // smoothly from the authored ground height. No jump, no horizontal drift.
            var authoredPos = new Vector3(5f, 0f, -8f);
            _root.transform.position = authoredPos;

            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            Simulate(flight, 1.2f);  // full GroundIdle
            Simulate(flight, 0.5f);  // 0.5s into VerticalLift

            Assert.AreEqual(FlightPhase.VerticalLift, flight.CurrentPhase);
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f,
                "VerticalLift must have ZERO forward displacement.");
            Assert.AreEqual(authoredPos.x, _root.transform.position.x, 1e-3f,
                "Lift must stay exactly above the authored X position.");
            Assert.AreEqual(authoredPos.z, _root.transform.position.z, 1e-3f,
                "Lift must stay exactly above the authored Z position.");
            Assert.Greater(_root.transform.position.y, authoredPos.y + 0.05f,
                "Height must begin rising from the authored ground position.");
            Assert.Less(_root.transform.position.y, authoredPos.y + 1.75f,
                "Height must still be below the full lift height mid-lift.");
        }

        [Test]
        public void NewPlaySessionRecapturesAuthoredTransformAndZeroesPhaseClock()
        {
            // Regression for the reported bug: with Enter Play Mode Options (Domain/Scene reload
            // disabled) the previous session's elapsed time and accumulated distance persist. A
            // new Play session must discard them, re-capture the CURRENT authored transform, and
            // restart at GroundIdle — never resuming far away.
            var authoredPos = new Vector3(30f, 0f, -50f);
            _root.transform.position = authoredPos;

            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            // First Play session: fly far away (stale state a second session would inherit).
            Simulate(flight, 6f);
            Assert.Greater(Vector3.Distance(_root.transform.position, authoredPos), 10f,
                "Sanity: the helicopter must have travelled away during the first session.");
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase);
            Assert.Greater(flight.Elapsed, 5f);

            // Between sessions the user re-positions the helicopter in the Scene (edit mode).
            _root.transform.SetPositionAndRotation(authoredPos, Quaternion.Euler(0f, 35f, 0f));

            // Unity enters Play Mode again without a domain/scene reload: the session counter is
            // bumped (RuntimeInitializeOnLoadMethod) and OnEnable is invoked on the component.
            var t = typeof(CinematicHelicopterFlight);
            var counter = t.GetField("_sessionCounter",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            counter.SetValue(null, (int)counter.GetValue(null) + 1);
            var onEnable = t.GetMethod("OnEnable",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            onEnable.Invoke(flight, null);

            // First frame of the new session must begin GroundIdle at the authored transform.
            flight.AdvanceFlight(1f / 60f);

            Assert.AreEqual(FlightPhase.GroundIdle, flight.CurrentPhase,
                "A new Play session must restart from GroundIdle, not resume mid-flight.");
            Assert.Less(flight.Elapsed, 0.1f, "The phase clock must be zeroed for the new session.");
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f,
                "Stale distance from the previous session must be discarded.");
            Assert.AreEqual(0f, flight.HeightGained, 1e-4f,
                "Stale rise from the previous session must be discarded.");
            Assert.AreEqual(authoredPos.x, _root.transform.position.x, 1e-4f,
                "Root must return to the CURRENT authored position, never a stale one.");
            Assert.AreEqual(authoredPos.y, _root.transform.position.y, 1e-4f);
            Assert.AreEqual(authoredPos.z, _root.transform.position.z, 1e-4f);
        }

        [Test]
        public void MidPlayComponentToggleDoesNotRecaptureStartState()
        {
            // The session guard must only fire on a NEW Play session. Toggling the component
            // (or its GameObject) mid-Play calls OnEnable too and must NOT re-base the flight
            // onto the current mid-flight position.
            _root.transform.position = new Vector3(0f, 0f, 0f);

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            Simulate(flight, 4f);

            float elapsedBefore = flight.Elapsed;
            Vector3 posBefore = _root.transform.position;
            Assert.Greater(elapsedBefore, 3.5f, "Sanity: flight must be mid-cruise.");

            var onEnable = typeof(CinematicHelicopterFlight).GetMethod("OnEnable",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            onEnable.Invoke(flight, null); // same session: must be a no-op

            flight.AdvanceFlight(1f / 60f);

            Assert.Greater(flight.Elapsed, elapsedBefore,
                "Elapsed time must keep advancing, not restart from zero.");
            Assert.Greater(Vector3.Distance(_root.transform.position, Vector3.zero), 1f,
                "The flight must continue from its current position, not jump back to start.");
            Assert.Greater(Vector3.Distance(_root.transform.position, posBefore), 0f,
                "The flight must keep moving forward from where it was.");
        }

        // ---- flight: straight line + rise ----

        [Test]
        public void FliesStraightAlongAuthoredForwardWithoutDrift()
        {
            // QA point 4: flies straight forward. Rotate the root so a naive world-axis
            // implementation would fail this. Verified forward axis is (1, 0, 0), so with a
            // yaw-90 root the travel direction is world -Z.
            _root.transform.position = Vector3.zero;
            _root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            Simulate(flight, 5f);

            Vector3 p = _root.transform.position;

            // Yaw 90 means the verified local forward (1,0,0) points along world -Z.
            Assert.Less(p.z, -1f, "Must travel along the authored forward direction.");
            Assert.AreEqual(0f, p.x, 0.01f, "Must not drift sideways — flight must be straight.");
        }

        [Test]
        public void RisesGentlyAndIsCappedByMaxRiseHeight()
        {
            // QA point 5: rises slightly (and does not climb forever).
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            SetPrivate(flight, "maxRiseHeight", 2f);

            Simulate(flight, 30f);

            Assert.Greater(flight.HeightGained, 0f, "The helicopter must gain some height.");
            Assert.LessOrEqual(flight.HeightGained, 2f + 1e-3f,
                "Rise must be capped by maxRiseHeight so it levels off.");
        }

        [Test]
        public void PitchDoesNotBendTheFlightPath()
        {
            // The cosmetic pitch must never steer the helicopter into the ground.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            SetPrivate(flight, "takeoffPitch", 30f);   // exaggerated
            SetPrivate(flight, "maxRiseHeight", 0f);   // uncapped, isolate the pitch effect

            Simulate(flight, 6f);

            Assert.GreaterOrEqual(_root.transform.position.y, 0f,
                "A nose-down cosmetic pitch must not drag the flight path downward.");
            Assert.Greater(_root.transform.position.x, 1f,
                "Forward travel must still occur (verified forward axis is (1, 0, 0)).");
        }

        [Test]
        public void ForwardAxisIsInspectorAdjustable()
        {
            // The user must be able to correct direction without a code change.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            SetPrivate(flight, "localForwardAxis", new Vector3(0f, 0f, -1f));

            Simulate(flight, 5f);

            Assert.Less(_root.transform.position.z, -1f,
                "Flipping Local Forward Axis must reverse the travel direction.");
        }

        [Test]
        public void DisabledFlightDoesNotMove()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.FlightEnabled = false;

            // Update() is what honours the flag; Edit Mode does not call it, so invoke it.
            var update = typeof(CinematicHelicopterFlight).GetMethod("Update",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            for (int i = 0; i < 10; i++) update.Invoke(flight, null);

            Assert.AreEqual(Vector3.zero, _root.transform.position,
                "A disabled flight must not move the helicopter.");
        }

        [Test]
        public void ResetReturnsToAuthoredStartTransform()
        {
            var start = new Vector3(5f, 2f, -3f);
            _root.transform.position = start;

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            Simulate(flight, 4f);
            Assert.AreNotEqual(start, _root.transform.position);

            flight.ResetFlight();

            Assert.AreEqual(start.x, _root.transform.position.x, 1e-4f);
            Assert.AreEqual(start.y, _root.transform.position.y, 1e-4f);
            Assert.AreEqual(start.z, _root.transform.position.z, 1e-4f);
        }

        // ---- flight: the model/rotors are carried, never driven ----

        [Test]
        public void ChildModelKeepsItsLocalTransformAndRotorComponent()
        {
            // The flight root must move the model WITHOUT altering its local transform, so the
            // manually verified rotor setup from micro task #1 is untouched.
            var model = new GameObject("helicopter_rigged");
            model.transform.SetParent(_root.transform, false);
            model.transform.localPosition = new Vector3(0.1f, 0.2f, 0.3f);
            model.transform.localRotation = Quaternion.Euler(0f, 15f, 0f);
            model.transform.localScale = new Vector3(2f, 2f, 2f);

            var rotorSpin = model.AddComponent<CinematicHelicopterRotorSpin>();

            Vector3 localPos = model.transform.localPosition;
            Quaternion localRot = model.transform.localRotation;
            Vector3 localScale = model.transform.localScale;

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            Simulate(flight, 4f);

            Assert.AreEqual(localPos, model.transform.localPosition,
                "Flight must not change the model's local position.");
            Assert.AreEqual(localRot, model.transform.localRotation,
                "Flight must not change the model's local rotation.");
            Assert.AreEqual(localScale, model.transform.localScale,
                "Flight must not change the model's local scale.");
            Assert.IsNotNull(rotorSpin, "The rotor spin component must remain intact.");
            Assert.AreNotEqual(Vector3.zero, _root.transform.position,
                "The root should still have moved, carrying the model with it.");
        }

        // ---- camera follow ----

        [Test]
        public void CameraSnapsBehindAboveAndToTheSideOnFirstUpdate()
        {
            // Snap-On-Start path: the camera jumps straight to the chase pose on the first
            // update. (Micro Task #5 introduced the takeoff transition, where Snap On Start is
            // FALSE and the camera instead holds its authored shot — covered separately.)
            _root.transform.position = Vector3.zero;
            _root.transform.rotation = Quaternion.identity;

            var camGo = new GameObject("CinematicCamera");
            try
            {
                var follow = camGo.AddComponent<CinematicHelicopterCameraFollow>();
                follow.Target = _root.transform;
                follow.SnapOnStart = true;
                follow.UpdateFollow(1f / 60f);

                Vector3 p = camGo.transform.position;
                Assert.Less(p.x, 0f, "Camera must sit BEHIND the helicopter (verified forward +X, so behind is -X).");
                Assert.Greater(p.y, 0f, "Camera must sit ABOVE the helicopter.");
                Assert.AreNotEqual(0f, p.z, "Camera must be offset to one SIDE.");
            }
            finally { Object.DestroyImmediate(camGo); }
        }

        [Test]
        public void CameraKeepsHelicopterInFrontOfIt()
        {
            // QA point 10: the helicopter stays clearly visible (in front of the camera).
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            SetPrivate(flight, "localForwardAxis", new Vector3(1f, 0f, 0f));

            var camGo = new GameObject("CinematicCamera");
            try
            {
                var follow = camGo.AddComponent<CinematicHelicopterCameraFollow>();
                follow.Target = _root.transform;

                for (int i = 0; i < 300; i++)
                {
                    flight.AdvanceFlight(1f / 60f);
                    follow.UpdateFollow(1f / 60f);
                }

                Vector3 toHeli = _root.transform.position - camGo.transform.position;
                float dot = Vector3.Dot(camGo.transform.forward, toHeli.normalized);
                Assert.Greater(dot, 0.5f,
                    "The helicopter must remain in front of the camera throughout the flight.");
            }
            finally { Object.DestroyImmediate(camGo); }
        }

        [Test]
        public void CameraTracksContinuouslyWithoutFallingBehind()
        {
            // QA point 8: continuous following — distance must stay bounded, not grow forever.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            SetPrivate(flight, "localForwardAxis", new Vector3(1f, 0f, 0f));

            var camGo = new GameObject("CinematicCamera");
            try
            {
                var follow = camGo.AddComponent<CinematicHelicopterCameraFollow>();
                follow.Target = _root.transform;

                for (int i = 0; i < 600; i++)
                {
                    flight.AdvanceFlight(1f / 60f);
                    follow.UpdateFollow(1f / 60f);
                }

                float distance = Vector3.Distance(camGo.transform.position, _root.transform.position);
                Assert.Less(distance, 40f,
                    "The camera must keep up with the helicopter rather than trailing away.");
            }
            finally { Object.DestroyImmediate(camGo); }
        }

        [Test]
        public void CameraWithNoTargetIsSafeAndDoesNotMove()
        {
            var camGo = new GameObject("CinematicCamera");
            try
            {
                camGo.transform.position = new Vector3(1f, 2f, 3f);
                var follow = camGo.AddComponent<CinematicHelicopterCameraFollow>();

                Assert.DoesNotThrow(() => follow.UpdateFollow(1f / 60f),
                    "An unassigned target must be skipped, not throw.");
                Assert.AreEqual(new Vector3(1f, 2f, 3f), camGo.transform.position,
                    "The camera must not move when it has no target.");
            }
            finally { Object.DestroyImmediate(camGo); }
        }

        [Test]
        public void CameraDoesNotCreateOrDisableAnyCamera()
        {
            // The component must use the existing camera only.
            int before = Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            var camGo = new GameObject("CinematicCamera");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                var follow = camGo.AddComponent<CinematicHelicopterCameraFollow>();
                follow.Target = _root.transform;

                float fov = cam.fieldOfView;
                for (int i = 0; i < 30; i++) follow.UpdateFollow(1f / 60f);

                int after = Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

                Assert.AreEqual(before + 1, after,
                    "Follow must not spawn additional cameras (only the one added by this test).");
                Assert.IsTrue(cam.enabled, "Follow must not disable the existing camera.");
                Assert.AreEqual(fov, cam.fieldOfView, 1e-4f, "Follow must not change camera settings.");
            }
            finally { Object.DestroyImmediate(camGo); }
        }

        [Test]
        public void ExposesRequestedInspectorFields()
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            var flight = typeof(CinematicHelicopterFlight);
            foreach (var name in new[]
                     {
                         "startDelay", "accelerationDuration", "cruiseSpeed",
                         "verticalRiseSpeed", "takeoffPitch", "localForwardAxis"
                     })
                Assert.IsNotNull(flight.GetField(name, F), name + " must be Inspector-exposed.");

            var cam = typeof(CinematicHelicopterCameraFollow);
            foreach (var name in new[]
                     {
                         "followDistance", "heightOffset", "sideOffset",
                         "positionDamping", "rotationDamping", "lookAheadDistance", "lookHeight"
                     })
                Assert.IsNotNull(cam.GetField(name, F), name + " must be Inspector-exposed.");
        }

        [Test]
        public void DefaultValuesMatchRequestedSpec()
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            var ft = typeof(CinematicHelicopterFlight);
            Assert.AreEqual(0.75f, (float)ft.GetField("startDelay", F).GetValue(flight), 1e-4f);
            Assert.AreEqual(2.5f, (float)ft.GetField("accelerationDuration", F).GetValue(flight), 1e-4f);
            Assert.AreEqual(8f, (float)ft.GetField("cruiseSpeed", F).GetValue(flight), 1e-4f);
            Assert.AreEqual(1.2f, (float)ft.GetField("verticalRiseSpeed", F).GetValue(flight), 1e-4f);
            Assert.AreEqual(4f, (float)ft.GetField("takeoffPitch", F).GetValue(flight), 1e-4f);

            var camGo = new GameObject("CinematicCamera");
            try
            {
                var follow = camGo.AddComponent<CinematicHelicopterCameraFollow>();
                var ct = typeof(CinematicHelicopterCameraFollow);
                Assert.AreEqual(11f, (float)ct.GetField("followDistance", F).GetValue(follow), 1e-4f);
                Assert.AreEqual(3.5f, (float)ct.GetField("heightOffset", F).GetValue(follow), 1e-4f);
                Assert.AreEqual(3.5f, (float)ct.GetField("sideOffset", F).GetValue(follow), 1e-4f);
                Assert.AreEqual(5f, (float)ct.GetField("positionDamping", F).GetValue(follow), 1e-4f);
                Assert.AreEqual(5f, (float)ct.GetField("rotationDamping", F).GetValue(follow), 1e-4f);
                Assert.AreEqual(2f, (float)ct.GetField("lookAheadDistance", F).GetValue(follow), 1e-4f);
                Assert.AreEqual(1f, (float)ct.GetField("lookHeight", F).GetValue(follow), 1e-4f);
                Assert.AreEqual(new Vector3(1f, 0f, 0f), (Vector3)ct.GetField("targetForwardAxis", F).GetValue(follow));
            }
            finally { Object.DestroyImmediate(camGo); }
        }
    }
}
