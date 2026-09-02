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

        /// <summary>
        /// Steps the flight in small fixed increments, mimicking a real frame loop.
        ///
        /// The requested duration is converted to an EXACT whole-frame count so the simulation
        /// stops at exactly the requested time. A float `for (t = 0; t < seconds; t += step)`
        /// accumulator runs one frame long: float(1/60) is slightly below the exact 1/60, so 72
        /// additions only reach ~1.1999999 (below float(1.2) = 1.20000005) and 180 additions only
        /// reach ~2.9999999 (below 3.0), and the loop keeps going until the NEXT addition finally
        /// crosses the bound. The extra frame then samples the flight PAST the phase boundary under
        /// test — 1.2167s is already inside VerticalLift and 3.0167s already inside
        /// ForwardTransition — which is what leaked 0.000447m of lift into the GroundIdle
        /// assertions and 0.00106 m/s of forward speed into the VerticalLift assertions.
        /// </summary>
        private static void Simulate(CinematicHelicopterFlight flight, float seconds, float step = 1f / 60f)
        {
            int frames = (int)Mathf.Round(seconds / step);
            for (int i = 0; i < frames; i++) flight.AdvanceFlight(step);
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

            // GroundIdle lasts 1.2s = exactly 72 frames at 60fps. Step an EXACT frame count — a
            // float `t < 1.2f` accumulator runs one frame long (see Simulate), which would step
            // into VerticalLift and sample the first tick of the lift ramp (Y = 4.000447).
            int groundIdleFrames = (int)Mathf.Round(1.2f / (1f / 60f));
            for (int frame = 0; frame < groundIdleFrames; frame++)
            {
                flight.AdvanceFlight(1f / 60f);

                Assert.Greater(Vector3.Distance(_root.transform.position, Vector3.zero), 20f,
                    "The helicopter must never teleport toward world origin (t=" + ((frame + 1f) / 60f) + ").");
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

        [Test]
        public void FirstMovementTickOfNewPlaySessionSelfHealsStaleStateWithoutOnEnable()
        {
            // QA Fix #5C regression — reproduces the real Unity failure. With Enter Play Mode
            // Options set to disable Domain/Scene reload, the component's instance fields
            // persist between Play sessions and OnEnable is NOT guaranteed to fire again at
            // Play entry. Manual QA: a finished previous session left _elapsed ~9.4s (stale
            // Cruise), _distance ~41.6m and _rise ~4.0m behind, and the first tick of the new
            // session teleported the root to (41.57841, 4.008937, 0).
            // The FIRST movement tick of a new session must therefore discard the stale state
            // by itself, before any trajectory calculation — no OnEnable, no Awake.

            // The scene restore places the root at its (new) authored transform before Play.
            Vector3 authoredPos = new Vector3(7.5f, 0.25f, -3f);
            Quaternion authoredRot = Quaternion.Euler(0f, 20f, 0f);
            _root.transform.SetPositionAndRotation(authoredPos, authoredRot);

            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            // Stale state persisted from the finished previous Play session (manual QA values).
            SetPrivate(flight, "_elapsed", 9.4f);
            SetPrivate(flight, "_distance", 41.58f);
            SetPrivate(flight, "_forwardClimb", 2.26f);
            SetPrivate(flight, "_rise", 4.01f);
            SetPrivate(flight, "_startPosition", new Vector3(0f, 0f, 0f));
            SetPrivate(flight, "_startRotation", Quaternion.identity);
            SetPrivate(flight, "_initialized", true); // blocks the normal one-shot re-capture

            // A new Play session begins (RuntimeInitializeOnLoadMethod bumps the generation)...
            var counter = typeof(CinematicHelicopterFlight).GetField("_sessionCounter",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            int previousGeneration = (int)counter.GetValue(null);
            SetPrivate(flight, "_playSessionId", previousGeneration); // ...the component still believes it is in that session
            counter.SetValue(null, previousGeneration + 1);           // ...and a new session starts

            // ...but OnEnable is NOT invoked — exactly the configuration that defeated QA #5A.
            // The normal first runtime movement tick must self-heal before moving anything.
            flight.AdvanceFlight(1f / 60f);

            // No teleport: the first frame writes EXACTLY the new authored transform.
            Assert.AreEqual(authoredPos.x, _root.transform.position.x, 1e-4f,
                "No stale-distance teleport (X) on the first tick of a new session.");
            Assert.AreEqual(authoredPos.y, _root.transform.position.y, 1e-4f,
                "No stale-rise teleport (Y) on the first tick of a new session.");
            Assert.AreEqual(authoredPos.z, _root.transform.position.z, 1e-4f,
                "No stale-distance teleport (Z) on the first tick of a new session.");
            Assert.Less(Quaternion.Angle(authoredRot, _root.transform.rotation), 1e-3f,
                "First frame must restore the authored rotation (travel direction re-captured).");

            // Stale session state discarded.
            Assert.AreEqual(FlightPhase.GroundIdle, flight.CurrentPhase,
                "A new session must begin in GroundIdle, not resume a stale Cruise.");
            Assert.Less(flight.Elapsed, 0.1f, "The phase clock must be zeroed for the new session.");
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f, "Stale distance must be discarded.");
            Assert.AreEqual(0f, flight.HeightGained, 1e-4f, "Stale rise must be discarded.");

            // GroundIdle then holds the authored position exactly (71 more frames => exactly 1.2s).
            for (int i = 0; i < 71; i++) flight.AdvanceFlight(1f / 60f);
            Assert.AreEqual(FlightPhase.GroundIdle, flight.CurrentPhase);
            Assert.AreEqual(authoredPos.x, _root.transform.position.x, 1e-4f,
                "GroundIdle must hold the authored position exactly.");
            Assert.AreEqual(authoredPos.y, _root.transform.position.y, 1e-4f);
            Assert.AreEqual(authoredPos.z, _root.transform.position.z, 1e-4f);
        }

        // ---- QA Fix #5D.1: scaled time is the safe default ----

        [Test]
        public void DefaultsToScaledTimeAndScaledPathKeepsTakeoffBehavior()
        {
            // QA diagnostic #5D proved the Play-start jump: with "Use Unscaled Time" ON (the old
            // default), the ~6.4 s editor stall between pressing Play and the first Update was
            // consumed through Time.unscaledDeltaTime in a single first tick (dt = 6.429102 in the
            // trace), which skipped GroundIdle + VerticalLift + ForwardTransition in one giant
            // integration step and teleported the root into Cruise (~51 m, ~4 m up). Scaled time
            // (Time.deltaTime) is therefore the safe default: a normal Play run can never consume
            // the Play-start stall in one tick. The field must still exist so unscaled behavior
            // can be opted into explicitly from the Inspector.
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            var field = typeof(CinematicHelicopterFlight).GetField("useUnscaledTime", F);
            Assert.IsNotNull(field, "The useUnscaledTime field must remain serialized (Inspector opt-in).");
            Assert.IsFalse((bool)field.GetValue(flight),
                "Scaled time must be the safe default (useUnscaledTime = false).");

            // The scaled-time path (AdvanceFlight with normal per-frame dt) must still run the
            // exact 4-phase takeoff with the untouched timings: 1.2s GroundIdle, 1.8s lift to
            // 1.75m, 2.5s acceleration, then 8 m/s Cruise.
            Simulate(flight, 1.2f);  // exact GroundIdle window
            Assert.AreEqual(FlightPhase.GroundIdle, flight.CurrentPhase);
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f, "No forward distance during GroundIdle.");
            Assert.AreEqual(0f, flight.HeightGained, 1e-4f, "No rise during GroundIdle.");
            Assert.AreEqual(0f, _root.transform.position.y, 1e-4f, "The root must stay grounded.");

            Simulate(flight, 1.8f);  // exact VerticalLift window (t = 3.0s)
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f, "VerticalLift must stay purely vertical.");
            Assert.AreEqual(1.75f, flight.HeightGained, 0.01f, "Lift must reach initialLiftHeight.");

            Simulate(flight, 2.5f);  // exact ForwardTransition window (t = 5.5s)
            Simulate(flight, 1.0f);  // 1.0s of Cruise (t = 6.5s)
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase, "Cruise must begin after 5.5s.");
            Assert.AreEqual(8f, flight.CurrentSpeed, 0.01f, "Full cruise speed must be reached.");
            Assert.Greater(flight.DistanceTravelled, 10f, "Cruise must have accumulated forward distance.");
        }

        // ---- Airborne start mode (optional, default OFF) ----
        // startAirborne = true begins the run ALREADY in forward/cruise flight from the
        // authored transform: no ground idle, no vertical lift, no takeoff pitch, no takeoff
        // acceleration staging. The shared movement path (cruise speed, capped gentle rise,
        // straight travel) is unchanged.

        [Test]
        public void DefaultModeRemainsExistingTakeoffBehavior()
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            var field = typeof(CinematicHelicopterFlight).GetField("startAirborne", F);
            Assert.IsNotNull(field, "The startAirborne field must be serialized (Inspector opt-in).");
            Assert.IsFalse((bool)field.GetValue(flight),
                "startAirborne must default to FALSE so existing scenes keep the takeoff behavior.");
            Assert.IsFalse(flight.StartAirborne, "The public StartAirborne view must default to false.");

            // The default component must still run the exact 4-phase takeoff, untouched timings.
            Simulate(flight, 1.2f); // exact GroundIdle window
            Assert.AreEqual(FlightPhase.GroundIdle, flight.CurrentPhase);
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f, "No forward distance during GroundIdle.");
            Assert.AreEqual(0f, flight.HeightGained, 1e-4f, "No rise during GroundIdle.");

            Simulate(flight, 1.8f); // t = 3.0s: VerticalLift complete
            Assert.AreEqual(FlightPhase.VerticalLift, flight.CurrentPhase);
            Assert.AreEqual(1.75f, flight.HeightGained, 0.01f, "Lift must reach initialLiftHeight.");
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f, "VerticalLift must stay purely vertical.");

            Simulate(flight, 2.5f); // t = 5.5s: ForwardTransition complete
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase);
            Assert.AreEqual(8f, flight.CurrentSpeed, 0.01f, "Full cruise speed must be reached.");
        }

        [Test]
        public void AirborneStartSkipsGroundIdle()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;

            flight.AdvanceFlight(1f / 60f); // first valid movement frame

            Assert.IsFalse(flight.IsGroundIdle, "Airborne-start must not spend any time in GroundIdle.");
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase);
            Assert.Greater(flight.DistanceTravelled, 0f,
                "Forward movement must begin on the first frame — the ground idle wait must be skipped.");
        }

        [Test]
        public void AirborneStartSkipsVerticalLiftPhase()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;

            // t = 3.0s is where normal mode has just finished its 1.75m vertical lift. Sample
            // every frame of that window: VerticalLift must never be entered, and no 1.75m
            // initialLiftHeight step may appear in the height.
            for (int i = 0; i < 180; i++)
            {
                flight.AdvanceFlight(1f / 60f);
                Assert.AreNotEqual(FlightPhase.VerticalLift, flight.CurrentPhase,
                    "VerticalLift must never occur in airborne-start mode (frame " + (i + 1) + ").");
            }

            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase);
            Assert.Less(flight.HeightGained, 1.75f,
                "No initialLiftHeight climb: height at t=3s must stay below the normal-mode lift height.");
            Assert.Greater(flight.HeightGained, 0f,
                "The shared gentle cruise rise must still apply (existing cruise behavior preserved).");
        }

        [Test]
        public void AirborneStartBeginsCruiseMovementImmediately()
        {
            // Intended cinematic use: the root is pre-placed slightly outside the LEFT edge of
            // the exterior frame, already at flight altitude.
            _root.transform.position = new Vector3(-12f, 4f, 0f);
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;

            flight.AdvanceFlight(1f / 60f);

            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase, "Airborne-start must begin directly in Cruise.");
            Assert.AreEqual(8f, flight.CurrentSpeed, 1e-4f,
                "Full cruise speed from the first frame — no takeoff acceleration staging.");
            Assert.AreEqual(1f, flight.SpeedFactor, 1e-4f, "The speed factor must be 1 immediately.");
            Assert.Greater(_root.transform.position.x, -12f,
                "The helicopter must already be moving forward (left to right) on the first frame.");
        }

        [Test]
        public void AirborneStartPreservesAuthoredPositionBeforeFirstMovement()
        {
            var authoredPos = new Vector3(28f, 3.5f, -47f);
            var authoredRot = Quaternion.Euler(0f, 35f, 0f);
            _root.transform.SetPositionAndRotation(authoredPos, authoredRot);

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;

            // Before the first VALID movement step (component add, zero-dt tick, negative-dt
            // tick) the authored transform must remain exactly as authored — no teleport or
            // reset to any other position.
            Assert.AreEqual(authoredPos, _root.transform.position, "AddComponent must not move the root.");
            flight.AdvanceFlight(0f);
            Assert.AreEqual(authoredPos, _root.transform.position, "A zero-dt tick must not move the helicopter.");
            flight.AdvanceFlight(-1f);
            Assert.AreEqual(authoredPos, _root.transform.position, "A negative-dt tick must not move the helicopter.");
            Assert.AreEqual(authoredRot, _root.transform.rotation, "The authored rotation must be preserved.");

            // The first valid step must depart FROM the authored transform with normal frame motion.
            flight.AdvanceFlight(1f / 60f);
            float displacement = Vector3.Distance(authoredPos, _root.transform.position);
            Assert.Less(displacement, 0.2f,
                "The first valid step must depart from the authored position, not snap somewhere else.");
        }

        [Test]
        public void AirborneStartHasNoOversizedFirstFrameJump()
        {
            var authoredPos = new Vector3(15f, 2f, -30f);
            var authoredRot = Quaternion.Euler(0f, 20f, 0f);
            _root.transform.SetPositionAndRotation(authoredPos, authoredRot);

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.ApplyTakeoffPitch = true;
            flight.TakeoffPitch = 30f; // exaggerated — must STILL be ignored in airborne mode

            flight.AdvanceFlight(1f / 60f);

            // One frame of cruise + cruise-rise motion: (8 + 1.2*0.35) * (1/60) ~= 0.1404 m.
            // The 0.2 m ceiling is generous yet ~15x below any teleport-scale jump.
            float displacement = Vector3.Distance(authoredPos, _root.transform.position);
            Assert.Less(displacement, 0.2f,
                "No oversized first-frame jump: displacement must be one frame of cruise motion.");

            Assert.Less(Quaternion.Angle(authoredRot, _root.transform.rotation), 0.01f,
                "No rotation snap: the takeoff pitch must not be applied in airborne-start mode.");
        }

        [Test]
        public void AirborneStartRespectsFlightEnabled()
        {
            var authoredPos = new Vector3(9f, 5f, -25f);
            _root.transform.position = authoredPos;

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.FlightEnabled = false;

            // Update() is what honours the flag; Edit Mode does not call it, so invoke it.
            var update = typeof(CinematicHelicopterFlight).GetMethod("Update",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            for (int i = 0; i < 30; i++) update.Invoke(flight, null);

            Assert.AreEqual(authoredPos, _root.transform.position,
                "Airborne-start must stay completely frozen while the flight is disabled.");
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f);
        }

        [Test]
        public void AirborneStartKeepsUnscaledTimeBehaviorUnchanged()
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            // The QA fix #5D.1 safe default must be untouched by the airborne mode: scaled
            // time remains the default, and the opt-in field still exists.
            var field = typeof(CinematicHelicopterFlight).GetField("useUnscaledTime", F);
            Assert.IsNotNull(field, "The useUnscaledTime field must remain serialized (Inspector opt-in).");
            Assert.IsFalse((bool)field.GetValue(flight),
                "Scaled time must remain the safe default even with airborne-start available.");

            // The scaled per-frame movement path must run the airborne cruise: full speed
            // immediately, then steady progress at exactly the cruise rate.
            flight.StartAirborne = true;
            Simulate(flight, 1f);
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase);
            Assert.AreEqual(8f, flight.CurrentSpeed, 0.01f);

            float distanceAfterOneSecond = flight.DistanceTravelled;
            Simulate(flight, 1f);
            Assert.AreEqual(8f, flight.DistanceTravelled - distanceAfterOneSecond, 0.02f,
                "Cruise progress must be steady at the cruise speed under normal per-frame dt.");
        }

        [Test]
        public void DisablingAirborneStartRestoresExistingTakeoffPath()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;

            Simulate(flight, 0.5f); // 0.5s of airborne cruise (t = 0.5s)
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase);
            float cruiseDistance = flight.DistanceTravelled;
            Assert.Greater(cruiseDistance, 3f, "Sanity: the airborne cruise must have moved the helicopter.");

            // Switching the mode off hands the flight back to the standard elapsed-driven
            // takeoff curve. At t = 0.5s that curve is still inside its ground-idle window,
            // so the old path must hold the helicopter exactly as it would from the ground.
            flight.StartAirborne = false;
            Assert.IsFalse(flight.StartAirborne);

            Simulate(flight, 0.2f); // t = 0.7s — still inside the old path's 1.2s idle window
            Assert.AreEqual(FlightPhase.GroundIdle, flight.CurrentPhase,
                "With airborne-start off, the elapsed-driven phase must follow the old takeoff curve again.");
            Assert.AreEqual(cruiseDistance, flight.DistanceTravelled, 1e-4f,
                "The old path's idle window must hold the helicopter — no further forward motion.");
        }

        // ---- Cinematic turn (optional, default OFF) ----
        // Sign convention under test: the forward axis is LOCAL (localForwardAxis, default
        // +X — never assumed to be world Z). POSITIVE turnYawDegrees = visually correct RIGHT
        // turn relative to that axis (rotation about the authored up axis from forward toward
        // the aircraft's right side, up x forward; default axes: +X -> (cos yaw, 0, -sin yaw)).
        // Bank is right-handed about the CURRENT heading: negative angle = right wing down, so
        // a right turn banks right. The path must curve through space (arc integrated along
        // the evolving heading) — not just the model rotating over a straight vector.

        [Test]
        public void TurnFeatureDefaultsAreDisabledAndExistingBehaviorIsUnchanged()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            Assert.IsFalse(flight.EnableTurn, "enableTurn must default to FALSE (no turn).");
            Assert.AreEqual(4f, flight.TurnStartTime, 1e-4f, "turnStartTime must default to 4s.");
            Assert.AreEqual(1.75f, flight.TurnDuration, 1e-4f, "turnDuration must default to 1.75s.");
            Assert.AreEqual(40f, flight.TurnYawDegrees, 1e-4f, "turnYawDegrees must default to 40.");
            Assert.AreEqual(10f, flight.TurnBankDegrees, 1e-4f, "turnBankDegrees must default to 10.");
            Assert.AreEqual(0f, flight.CurrentTurnYawDegrees, 1e-4f, "No yaw with the feature disabled.");
            Assert.AreEqual(0f, flight.CurrentTurnBankDegrees, 1e-4f, "No bank with the feature disabled.");

            // A full default run must behave EXACTLY as before: 4-phase takeoff ending in a
            // straight +X cruise with zero turn contribution.
            Simulate(flight, 6f);
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase, "Default takeoff behavior must be intact.");
            Assert.AreEqual(8f, flight.CurrentSpeed, 0.01f, "Cruise speed must be unchanged.");
            Assert.AreEqual(0f, _root.transform.position.z, 1e-3f, "No lateral (turn) displacement with the feature off.");
            Assert.Greater(_root.transform.position.x, 10f, "Straight forward travel must be intact.");
            Assert.AreEqual(0f, flight.CurrentTurnYawDegrees, 1e-4f, "No turn yaw may appear after a full run.");
            Assert.AreEqual(0f, flight.CurrentTurnBankDegrees, 1e-4f, "No turn bank may appear after a full run.");
        }

        [Test]
        public void BeforeTurnStartTimeTheHeadingAndPathAreUnchanged()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true; // default turn start = 4.0s

            Simulate(flight, 3.9f); // 234 frames -> t = 3.899997 < 4.0: strictly BEFORE the turn

            Assert.AreEqual(0f, flight.CurrentTurnYawDegrees, 1e-4f, "Yaw must be exactly 0 before the start time.");
            Assert.AreEqual(0f, flight.CurrentTurnBankDegrees, 1e-4f, "Bank must be exactly 0 before the start time.");
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _root.transform.rotation), 1e-4f,
                "The authored rotation must be untouched before the turn.");
            Assert.AreEqual(0f, _root.transform.position.z, 1e-3f, "No lateral deviation before the turn.");
            Assert.AreEqual(8f * 3.9f, _root.transform.position.x, 0.05f,
                "The existing steady cruise path along the authored forward axis must be unchanged.");
        }

        [Test]
        public void TurnBeginsOnlyAfterTheConfiguredStartTime()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true; // default turn start = 4.0s

            Simulate(flight, 4f); // EXACTLY 240 frames -> t = 3.999997 < 4.0 (float tick count)
            Assert.AreEqual(0f, flight.CurrentTurnYawDegrees, 1e-4f,
                "The turn must not begin before the configured start time.");
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _root.transform.rotation), 1e-4f,
                "No rotation change before the configured start time.");

            flight.AdvanceFlight(1f / 60f); // frame 241 -> first frame at/after 4.0s
            Assert.Greater(flight.CurrentTurnYawDegrees, 0f,
                "The turn must begin on the first frame after the configured start time.");
            Assert.Greater(Quaternion.Angle(Quaternion.identity, _root.transform.rotation), 0f,
                "The rotation must start moving on the first frame after the start time.");
        }

        [Test]
        public void MidTurnYawEasesBetweenZeroAndTheFinalYaw()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true; // 4.0s + 1.75s window

            Simulate(flight, 4.5f);
            float yawEarly = flight.CurrentTurnYawDegrees;
            Simulate(flight, 0.4f); // t = 4.9s, mid-window
            float yawMid = flight.CurrentTurnYawDegrees;
            Simulate(flight, 0.4f); // t = 5.3s, late window
            float yawLate = flight.CurrentTurnYawDegrees;

            Assert.Greater(yawEarly, 0f, "Yaw must be strictly positive inside the window.");
            Assert.Less(yawEarly, 40f, "Yaw must be strictly below the final yaw inside the window.");
            Assert.Greater(yawMid, yawEarly, "The yaw must ease forward, not snap.");
            Assert.Less(yawMid, 40f, "Yaw must stay below the final yaw inside the window.");
            Assert.Greater(yawLate, yawMid, "The yaw must keep easing toward the final value.");
            Assert.Less(yawLate, 40f, "Yaw must not overshoot the final yaw inside the window.");
        }

        [Test]
        public void MidTurnBankIsNonZeroAndBanksIntoTheRightTurn()
        {
            // Default axes (forward +X, up +Y): the aircraft's right side is -Z. A RIGHT turn
            // (positive yaw) must bank RIGHT — negative roll about the heading, right wing down:
            // the up vector tilts toward -Z.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true;

            Simulate(flight, 4.9f); // mid-window

            float bank = flight.CurrentTurnBankDegrees;
            Assert.Less(bank, 0f,
                "A right turn (positive yaw) must bank RIGHT: negative roll about the heading (right wing down).");
            Assert.Less(bank, -1f, "The mid-turn bank must be a meaningful non-zero lean.");
            Assert.Less(_root.transform.up.z, -0.1f,
                "Geometric check: up must tilt toward the aircraft's right side (-Z) during a right turn.");
            Assert.Greater(_root.transform.up.y, 0.9f, "The tilt must be a bank, not a flip.");
        }

        [Test]
        public void AtEndOfTurnTheFinalYawIsExactlyTheConfiguredYaw()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true; // 4.0s + 1.75s = 5.75s end

            Simulate(flight, 5.75f); // 345 frames -> t = 5.75002: window fully closed

            Assert.AreEqual(40f, flight.CurrentTurnYawDegrees, 0.01f,
                "At the end of the turn the yaw must equal the configured turnYawDegrees.");
            Assert.AreEqual(40f, Quaternion.Angle(Quaternion.identity, _root.transform.rotation), 0.05f,
                "The total rotation must be exactly the final yaw (no residual bank).");
        }

        [Test]
        public void AtEndOfTurnTheBankReturnsToZero()
        {
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true;

            Simulate(flight, 5.8f); // just past the window end (t = 5.80002 > 5.75)

            Assert.AreEqual(0f, flight.CurrentTurnBankDegrees, 1e-4f,
                "The bank must be exactly 0 once the turn is complete.");
            Assert.AreEqual(40f, flight.CurrentTurnYawDegrees, 0.01f,
                "The final yaw must be held after the turn completes.");
            Assert.AreEqual(0f, _root.transform.up.z, 0.001f,
                "No bank residue: up must be vertical again (yaw is about the up axis).");
            Assert.AreEqual(40f, Quaternion.Angle(Quaternion.identity, _root.transform.rotation), 0.05f,
                "End state must be exactly start rotation + final yaw + zero bank.");
        }

        [Test]
        public void AfterTurnTheFlightContinuesAlongTheNewHeading()
        {
            // After the window the yaw is constant, so the helicopter must fly STRAIGHT along
            // the NEW heading — and that heading must be the 40-degree-right one.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true;

            Simulate(flight, 5.75f);
            Vector3 p1 = _root.transform.position;
            Simulate(flight, 2f); // 120 frames of post-turn flight
            Vector3 d = _root.transform.position - p1;

            float horizontal = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            Assert.AreEqual(16f, horizontal, 0.1f,
                "The cruise speed must be preserved along the new heading (2s * 8 m/s).");
            Assert.AreEqual(Mathf.Cos(40f * Mathf.Deg2Rad), d.x / horizontal, 0.01f,
                "Post-turn travel direction must be the NEW heading (40 degrees right of +X).");
            Assert.AreEqual(-Mathf.Sin(40f * Mathf.Deg2Rad), d.z / horizontal, 0.01f,
                "Post-turn travel direction must be the NEW heading (40 degrees right of +X).");
            Assert.Less(d.z, -5f, "The new heading must clearly deviate from the original straight line.");
        }

        [Test]
        public void TheTurnCurvesThePathThroughSpace()
        {
            // A model-rotation-only implementation would keep the position on the straight
            // base line (z = 0 for the default axes). The real path must accumulate lateral
            // displacement as the heading evolves.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true;

            Simulate(flight, 4.9f); // mid-window
            float z1 = _root.transform.position.z;
            Assert.Less(z1, -0.3f,
                "The path must have curved through space (lateral displacement), not just the model rotating.");

            Simulate(flight, 0.5f); // t = 5.4s, still inside the window
            float z2 = _root.transform.position.z;
            Assert.Less(z2, z1 - 0.5f, "The lateral curvature must keep growing through the turn.");
        }

        [Test]
        public void NoPositionalTeleportAtTurnStart()
        {
            // Straddle the 4.0s boundary (frames 236..247 include the turn start at 241):
            // every per-frame displacement must stay at one frame of cruise motion.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true;

            Simulate(flight, 3.9166667f); // 235 frames -> just before the turn start
            Vector3 prev = _root.transform.position;
            for (int i = 0; i < 12; i++)
            {
                flight.AdvanceFlight(1f / 60f);
                Assert.Less(Vector3.Distance(prev, _root.transform.position), 0.2f,
                    "No positional teleport across the turn-start boundary (frame " + (i + 1) + ").");
                prev = _root.transform.position;
            }
        }

        [Test]
        public void NoRotationSnapAtTurnStart()
        {
            // Same boundary window: the eased yaw starts from exactly 0, so the per-frame
            // rotation change must stay tiny (no snap to any intermediate yaw).
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true;

            Simulate(flight, 3.9166667f); // 235 frames -> just before the turn start
            Quaternion prev = _root.transform.rotation;
            for (int i = 0; i < 12; i++)
            {
                flight.AdvanceFlight(1f / 60f);
                float delta = Quaternion.Angle(prev, _root.transform.rotation);
                Assert.Less(delta, 2f,
                    "No rotation snap across the turn-start boundary (frame " + (i + 1) + ").");
                prev = _root.transform.rotation;
            }
        }

        [Test]
        public void ResetFlightRestoresAuthoredStateAndClearsTheTurn()
        {
            var authoredPos = new Vector3(5f, 2f, -3f);
            var authoredRot = Quaternion.Euler(0f, 20f, 0f);
            _root.transform.SetPositionAndRotation(authoredPos, authoredRot);

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true;

            Simulate(flight, 7f); // well past the turn: mid-cruise along the new heading
            Assert.Greater(Quaternion.Angle(authoredRot, _root.transform.rotation), 10f,
                "Sanity: the turn must have happened before the reset.");
            Assert.Greater(Vector3.Distance(authoredPos, _root.transform.position), 1f,
                "Sanity: the helicopter must have moved before the reset.");

            flight.ResetFlight();

            Assert.AreEqual(authoredPos.x, _root.transform.position.x, 1e-4f, "Reset must restore the authored X.");
            Assert.AreEqual(authoredPos.y, _root.transform.position.y, 1e-4f, "Reset must restore the authored Y.");
            Assert.AreEqual(authoredPos.z, _root.transform.position.z, 1e-4f, "Reset must restore the authored Z.");
            Assert.AreEqual(0f, Quaternion.Angle(authoredRot, _root.transform.rotation), 0.001f,
                "Reset must restore the authored rotation — no residual yaw.");
            Assert.AreEqual(0f, flight.CurrentTurnYawDegrees, 1e-4f, "Turn progress must be zeroed by the reset.");
            Assert.AreEqual(0f, flight.CurrentTurnBankDegrees, 1e-4f, "No residual bank may survive the reset.");
            Assert.AreEqual(0f, flight.Elapsed, 1e-4f, "The shared flight clock must be zeroed.");

            flight.AdvanceFlight(1f / 60f);
            Assert.AreEqual(0f, Quaternion.Angle(authoredRot, _root.transform.rotation), 0.001f,
                "After a reset the flight must NOT resume the old turn on the next frame.");
        }

        [Test]
        public void DisabledFlightPreventsTheTurn()
        {
            var authoredPos = new Vector3(9f, 5f, -25f);
            _root.transform.position = authoredPos;

            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true;
            flight.FlightEnabled = false;

            // Update() is what honours the flag; Edit Mode does not call it, so invoke it.
            var update = typeof(CinematicHelicopterFlight).GetMethod("Update",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            for (int i = 0; i < 300; i++) update.Invoke(flight, null); // 5s — well past the turn window

            Assert.AreEqual(authoredPos, _root.transform.position,
                "A disabled flight must not move, turn, or rotate.");
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _root.transform.rotation), 1e-4f,
                "A disabled flight must not rotate the root.");
            Assert.AreEqual(0f, flight.CurrentTurnYawDegrees, 1e-4f,
                "The flight clock must not advance while disabled (turn must not progress).");
            Assert.AreEqual(0f, flight.Elapsed, 1e-4f, "The shared flight clock must stay frozen.");
        }

        [Test]
        public void TurnFeatureLeavesScaledTimeDefaultAndPerFrameCruiseUnchanged()
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();

            var field = typeof(CinematicHelicopterFlight).GetField("useUnscaledTime", F);
            Assert.IsNotNull(field, "The useUnscaledTime field must remain serialized (Inspector opt-in).");
            Assert.IsFalse((bool)field.GetValue(flight),
                "Scaled time must remain the safe default with the turn feature available.");

            // With the turn enabled, the per-frame scaled-time path before the turn window
            // must still be a steady 8 m/s cruise.
            flight.StartAirborne = true;
            flight.EnableTurn = true;
            Simulate(flight, 1f);
            float d1 = flight.DistanceTravelled;
            Simulate(flight, 1f);
            Assert.AreEqual(8f, d1, 0.02f, "First second must be steady cruise with the turn feature on.");
            Assert.AreEqual(8f, flight.DistanceTravelled - d1, 0.02f,
                "Second second must be steady cruise with the turn feature on.");
        }

        [Test]
        public void AirborneStartAndTurnWorkTogether()
        {
            // Airborne start (no takeoff staging) + the turn on the same clock: cruise from
            // frame one, no lift step, then the turn plays out on the shared elapsed clock.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true;

            flight.AdvanceFlight(1f / 60f);
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase, "Airborne-start must remain Cruise with the turn on.");
            Assert.AreEqual(8f, flight.CurrentSpeed, 1e-4f, "Full cruise speed from the first frame.");

            Simulate(flight, 3f); // t = 3.0167s
            Assert.Less(flight.HeightGained, 1.75f,
                "No initialLiftHeight step may appear (airborne-start semantics preserved).");

            Simulate(flight, 2.75f); // t = 5.7667s: turn window closed
            Assert.AreEqual(40f, flight.CurrentTurnYawDegrees, 0.01f, "The turn must complete on the shared clock.");
            Assert.AreEqual(0f, flight.CurrentTurnBankDegrees, 1e-4f, "The bank must be back at zero.");
            Assert.Less(_root.transform.position.z, -4f, "The curved path must be present with airborne start.");
        }

        [Test]
        public void NormalTakeoffAndTurnRemainBackwardCompatible()
        {
            // The turn window (4.0s..5.75s) overlaps the normal takeoff's ForwardTransition
            // (3.0s..5.5s): the existing takeoff staging must run exactly as before, and the
            // turn must layer on top of it without changing the pre-turn path.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.EnableTurn = true; // normal takeoff (startAirborne = false)

            Simulate(flight, 1f);
            Assert.AreEqual(FlightPhase.GroundIdle, flight.CurrentPhase, "GroundIdle must be intact.");
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f, "No forward travel during GroundIdle.");
            Assert.AreEqual(0f, flight.HeightGained, 1e-4f, "No rise during GroundIdle.");

            Simulate(flight, 2f); // t = 3.0s: VerticalLift complete
            Assert.AreEqual(1.75f, flight.HeightGained, 0.01f, "The vertical lift must run exactly as before.");
            Assert.AreEqual(0f, flight.DistanceTravelled, 1e-4f, "VerticalLift must stay purely vertical.");

            Simulate(flight, 4f); // t = 7.0s: cruise along the new heading
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase, "The flight must reach Cruise.");
            Assert.AreEqual(40f, flight.CurrentTurnYawDegrees, 0.01f, "The turn must complete after the takeoff.");
            Assert.Greater(flight.DistanceTravelled, 0.5f,
                "The pre-turn forward travel must have accumulated along the old path.");
            Assert.Greater(_root.transform.position.x, 10f, "The flight must still progress forward overall.");
            Assert.Less(_root.transform.position.z, -2f, "The turn must curve the normal-takeoff path too.");
        }

        [Test]
        public void ZeroOrNegativeTurnValuesAreClampedSafely()
        {
            // turnDuration = 0 or negative must clamp to the safe minimum window (0.05s) —
            // never a divide-by-zero, NaN, or inverted window.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            flight.EnableTurn = true;
            flight.TurnDuration = 0f;
            Assert.DoesNotThrow(() => Simulate(flight, 5f), "A zero turnDuration must not throw.");
            Assert.AreEqual(40f, flight.CurrentTurnYawDegrees, 0.01f,
                "The turn must still complete (clamped to the minimum window) with a zero duration.");
            Assert.AreEqual(0f, flight.CurrentTurnBankDegrees, 1e-4f, "The bank must be back at zero after the clamped turn.");

            var go2 = new GameObject("TurnRoot2");
            try
            {
                var flight2 = go2.AddComponent<CinematicHelicopterFlight>();
                flight2.StartAirborne = true;
                flight2.EnableTurn = true;
                flight2.TurnDuration = -5f;
                Assert.DoesNotThrow(() => Simulate(flight2, 5f), "A negative turnDuration must not throw.");
                Assert.AreEqual(40f, flight2.CurrentTurnYawDegrees, 0.01f,
                    "The turn must still complete (clamped) with a negative duration.");
            }
            finally { Object.DestroyImmediate(go2); }

            var go3 = new GameObject("TurnRoot3");
            try
            {
                var flight3 = go3.AddComponent<CinematicHelicopterFlight>();
                flight3.StartAirborne = true;
                flight3.EnableTurn = true;
                flight3.TurnStartTime = -2f;
                Simulate(flight3, 1f);
                Assert.Greater(flight3.CurrentTurnYawDegrees, 0f,
                    "A negative turnStartTime must clamp to 0 — the turn is already in progress.");
                Assert.Less(flight3.CurrentTurnYawDegrees, 40f,
                    "The clamped turn must still ease toward the final yaw, not snap to it.");
            }
            finally { Object.DestroyImmediate(go3); }
        }

        [Test]
        public void PositiveTurnYawIsARightTurnRelativeToTheLocalForwardAxis()
        {
            // Sign-convention proof on a NON-default axis: forward = -Z (root at identity),
            // so the aircraft's right side is -X. A positive turnYawDegrees must bank toward
            // -X (right wing down) and curve the path toward -X — proving the convention is
            // relative to the configured LOCAL forward axis, not hardcoded to world Z.
            _root.transform.position = Vector3.zero;
            var flight = _root.AddComponent<CinematicHelicopterFlight>();
            flight.LocalForwardAxis = new Vector3(0f, 0f, -1f);
            flight.StartAirborne = true;
            flight.EnableTurn = true;

            Simulate(flight, 4.9f); // mid-window
            Assert.Less(_root.transform.up.x, -0.1f,
                "Right bank relative to the -Z forward: up must tilt toward the aircraft's right side (-X).");
            Assert.Greater(_root.transform.up.y, 0.9f, "The tilt must be a bank, not a flip.");

            Simulate(flight, 2.1f); // t = 7.0s, after the turn
            Assert.Less(_root.transform.position.x, -0.5f,
                "The path must curve to the aircraft's right (-X) relative to the -Z forward axis.");
            Assert.Less(_root.transform.position.z, -1f,
                "The helicopter must keep flying along its forward axis (-Z).");
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
                         "verticalRiseSpeed", "takeoffPitch", "localForwardAxis",
                         "useUnscaledTime", "startAirborne",
                         "enableTurn", "turnStartTime", "turnDuration",
                         "turnYawDegrees", "turnBankDegrees"
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
            Assert.IsFalse((bool)ft.GetField("startAirborne", F).GetValue(flight),
                "startAirborne must default to false (existing takeoff behavior preserved).");
            Assert.IsFalse((bool)ft.GetField("enableTurn", F).GetValue(flight),
                "enableTurn must default to false (no turn = existing straight flight).");
            Assert.AreEqual(4f, (float)ft.GetField("turnStartTime", F).GetValue(flight), 1e-4f);
            Assert.AreEqual(1.75f, (float)ft.GetField("turnDuration", F).GetValue(flight), 1e-4f);
            Assert.AreEqual(40f, (float)ft.GetField("turnYawDegrees", F).GetValue(flight), 1e-4f);
            Assert.AreEqual(10f, (float)ft.GetField("turnBankDegrees", F).GetValue(flight), 1e-4f);

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
