using NUnit.Framework;
using OperationOutbreak.Cinematic;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Micro task #3 — focused EditMode tests for CinematicHelicopterVisualMotion.
    ///
    /// Verifies:
    /// 1. Visual motion does not move or rotate HelicopterFlightRoot.
    /// 2. Visual motion captures and preserves the authored base local transform.
    /// 3. No cumulative positional drift over hundreds of frames.
    /// 4. No cumulative rotational drift over hundreds of frames.
    /// 5. Zero bob amplitude produces zero bob displacement.
    /// 6. Zero roll amplitude produces zero roll variation.
    /// 7. Startup begins strictly at base transform with zero sudden jump or snap.
    /// 8. Bob remains within configured amplitude bounds.
    /// 9. Roll remains within configured amplitude bounds.
    /// 10. Disabling and resetting cleanly restores the authored local transform.
    /// 11. Rotor child transforms (rotor_up, rotor_tail, mian_body) are not directly modified.
    /// 12. Flight translation logic remains authoritative and unchanged.
    /// 13. Model forward axis (1,0,0) and pitch axis (0,0,-1) produce nose-down pitch.
    /// 14. Camera-follow remains fully compatible and tracks the flight root.
    /// 15. All requested Inspector fields are exposed.
    /// 16. Default values match requested specifications.
    /// 17. Duplicate takeoff pitch is prevented when flight and visual motion coexist.
    /// </summary>
    public sealed class CinematicHelicopterVisualMotionTests
    {
        private GameObject _flightRoot;
        private GameObject _heliModel;

        [SetUp]
        public void SetUp()
        {
            _flightRoot = new GameObject("HelicopterFlightRoot");
            _heliModel = new GameObject("helicopter_rigged");
            _heliModel.transform.SetParent(_flightRoot.transform, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_heliModel != null) Object.DestroyImmediate(_heliModel);
            if (_flightRoot != null) Object.DestroyImmediate(_flightRoot);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(f, field + " must exist on " + target.GetType().Name + ".");
            f.SetValue(target, value);
        }

        private static void Simulate(CinematicHelicopterVisualMotion motion, float seconds, float step = 1f / 60f)
        {
            for (float t = 0f; t < seconds; t += step)
                motion.AdvanceVisualMotion(step);
        }

        // ---- 1. Flight root isolation ----

        [Test]
        public void VisualMotionDoesNotMoveOrRotateFlightRoot()
        {
            _flightRoot.transform.position = new Vector3(10f, 5f, 20f);
            _flightRoot.transform.rotation = Quaternion.Euler(0f, 45f, 0f);

            Vector3 rootPos = _flightRoot.transform.position;
            Quaternion rootRot = _flightRoot.transform.rotation;

            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            Simulate(motion, 5f);

            Assert.AreEqual(rootPos.x, _flightRoot.transform.position.x, 1e-5f, "Flight root position X must not be altered by visual motion.");
            Assert.AreEqual(rootPos.y, _flightRoot.transform.position.y, 1e-5f, "Flight root position Y must not be altered by visual motion.");
            Assert.AreEqual(rootPos.z, _flightRoot.transform.position.z, 1e-5f, "Flight root position Z must not be altered by visual motion.");
            Assert.Less(Quaternion.Angle(rootRot, _flightRoot.transform.rotation), 1e-4f, "Flight root rotation must not be altered by visual motion.");
        }

        // ---- 2. Base local transform capture ----

        [Test]
        public void CapturesAuthoredBaseLocalTransform()
        {
            _heliModel.transform.localPosition = new Vector3(0.5f, 1.2f, -0.3f);
            _heliModel.transform.localRotation = Quaternion.Euler(5f, -10f, 15f);

            Vector3 expectedPos = _heliModel.transform.localPosition;
            Quaternion expectedRot = _heliModel.transform.localRotation;

            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            motion.CaptureBaseState();

            Assert.AreEqual(expectedPos, motion.BaseLocalPosition, "Must capture authored localPosition.");
            Assert.Less(Quaternion.Angle(expectedRot, motion.BaseLocalRotation), 1e-4f, "Must capture authored localRotation.");
        }

        // ---- 3. No cumulative positional drift ----

        [Test]
        public void NoCumulativePositionalDriftOverHundredsOfFrames()
        {
            _heliModel.transform.localPosition = new Vector3(2f, 1f, 0f);
            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();

            Simulate(motion, 10f); // 600 frames

            // Advance by an exact cycle of bob frequency so sine returns to 0
            // Cycle period = 1.0 / bobFrequency
            float period = 1f / motion.BobFrequency;
            float timeToNextFullCycle = period - (motion.Elapsed % period);
            motion.AdvanceVisualMotion(timeToNextFullCycle);

            Assert.AreEqual(motion.BaseLocalPosition.x, _heliModel.transform.localPosition.x, 1e-3f, "X should show zero drift.");
            Assert.AreEqual(motion.BaseLocalPosition.y, _heliModel.transform.localPosition.y, 1e-3f, "Y should return to base at cycle end without drift.");
            Assert.AreEqual(motion.BaseLocalPosition.z, _heliModel.transform.localPosition.z, 1e-3f, "Z should show zero drift.");
        }

        // ---- 4. No cumulative rotational drift ----

        [Test]
        public void NoCumulativeRotationalDriftOverHundredsOfFrames()
        {
            _heliModel.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            SetPrivate(motion, "takeoffPitchDegrees", 0f); // isolate roll

            Simulate(motion, 10f);

            // Advance to next exact roll cycle
            float period = 1f / motion.RollFrequency;
            float timeToNextFullCycle = period - (motion.Elapsed % period);
            motion.AdvanceVisualMotion(timeToNextFullCycle);

            Assert.Less(Quaternion.Angle(_heliModel.transform.localRotation, motion.BaseLocalRotation), 1e-3f,
                "Rotation must not accumulate drift over hundreds of frames.");
        }

        // ---- 5. Zero bob amplitude produces zero displacement ----

        [Test]
        public void ZeroBobAmplitudeProducesZeroBobDisplacement()
        {
            _heliModel.transform.localPosition = new Vector3(1f, 2f, 3f);
            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            SetPrivate(motion, "bobAmplitude", 0f);

            Simulate(motion, 5f);

            Assert.AreEqual(new Vector3(1f, 2f, 3f), _heliModel.transform.localPosition,
                "Zero bob amplitude must produce exactly zero position displacement.");
        }

        // ---- 6. Zero roll amplitude produces zero roll variation ----

        [Test]
        public void ZeroRollAmplitudeProducesZeroRollVariation()
        {
            _heliModel.transform.localRotation = Quaternion.identity;
            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            SetPrivate(motion, "rollAmplitude", 0f);
            SetPrivate(motion, "takeoffPitchDegrees", 0f);

            Simulate(motion, 5f);

            Assert.Less(Quaternion.Angle(Quaternion.identity, _heliModel.transform.localRotation), 1e-4f,
                "Zero roll amplitude and zero pitch must leave localRotation strictly untouched.");
        }

        // ---- 7. Startup begins strictly at base transform ----

        [Test]
        public void StartupBeginsExactlyAtBaseTransformWithoutJumpOrSnap()
        {
            _heliModel.transform.localPosition = new Vector3(-3f, 4f, 5f);
            _heliModel.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);

            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            motion.AdvanceVisualMotion(0f);

            Assert.AreEqual(new Vector3(-3f, 4f, 5f), _heliModel.transform.localPosition,
                "No position jump at t = 0.");
            Assert.Less(Quaternion.Angle(Quaternion.Euler(10f, 20f, 30f), _heliModel.transform.localRotation), 1e-5f,
                "No rotation snap at t = 0.");
        }

        // ---- 8. Bob remains within amplitude bounds ----

        [Test]
        public void BobDisplacementRemainsStrictlyWithinConfiguredAmplitude()
        {
            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            SetPrivate(motion, "bobAmplitude", 0.08f);

            for (int i = 0; i < 600; i++)
            {
                motion.AdvanceVisualMotion(1f / 60f);
                float dist = Vector3.Distance(_heliModel.transform.localPosition, motion.BaseLocalPosition);
                Assert.LessOrEqual(dist, 0.08f + 1e-4f, "Bob displacement must never exceed configured amplitude.");
            }
        }

        // ---- 9. Roll remains within amplitude bounds ----

        [Test]
        public void RollVariationRemainsStrictlyWithinConfiguredAmplitude()
        {
            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            SetPrivate(motion, "rollAmplitude", 1.0f);
            SetPrivate(motion, "takeoffPitchDegrees", 0f); // isolate roll

            for (int i = 0; i < 600; i++)
            {
                motion.AdvanceVisualMotion(1f / 60f);
                float angle = Quaternion.Angle(_heliModel.transform.localRotation, motion.BaseLocalRotation);
                Assert.LessOrEqual(angle, 1.0f + 1e-3f, "Roll angle must never exceed configured amplitude.");
            }
        }

        // ---- 10. Disabling and resetting cleanly restores authored transform ----

        [Test]
        public void DisablingOrResettingRestoresAuthoredLocalTransform()
        {
            _heliModel.transform.localPosition = new Vector3(1f, 2f, 3f);
            _heliModel.transform.localRotation = Quaternion.Euler(0f, 15f, 0f);

            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            Simulate(motion, 2f);

            // In mid-motion, local transform is displaced
            Assert.AreNotEqual(new Vector3(1f, 2f, 3f), _heliModel.transform.localPosition);

            // Reset restores base transform
            motion.ResetVisualMotion();
            Assert.AreEqual(new Vector3(1f, 2f, 3f), _heliModel.transform.localPosition,
                "ResetVisualMotion must restore base local position.");
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 15f, 0f), _heliModel.transform.localRotation), 1e-4f,
                "ResetVisualMotion must restore base local rotation.");

            // Running again and disabling restores base transform
            Simulate(motion, 1.5f);
            motion.MotionEnabled = false;
            Assert.AreEqual(new Vector3(1f, 2f, 3f), _heliModel.transform.localPosition,
                "Disabling MotionEnabled must restore base local position.");
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 15f, 0f), _heliModel.transform.localRotation), 1e-4f,
                "Disabling MotionEnabled must restore base local rotation.");
        }

        // ---- 11. Rotor child transforms are not directly modified ----

        [Test]
        public void RotorChildTransformsAreNotDirectlyModified()
        {
            var body = new GameObject("mian_body");
            var rotorTail = new GameObject("rotor_tail");
            var rotorUp = new GameObject("rotor_up");

            body.transform.SetParent(_heliModel.transform, false);
            rotorTail.transform.SetParent(_heliModel.transform, false);
            rotorUp.transform.SetParent(_heliModel.transform, false);

            body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            body.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            rotorTail.transform.localPosition = new Vector3(-2f, 0.8f, 0f);
            rotorTail.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            rotorUp.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            rotorUp.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            Simulate(motion, 4f);

            Assert.AreEqual(new Vector3(0f, 0.5f, 0f), body.transform.localPosition, "mian_body localPosition must be untouched.");
            Assert.Less(Quaternion.Angle(Quaternion.Euler(-90f, 0f, 0f), body.transform.localRotation), 1e-4f, "mian_body localRotation must be untouched.");

            Assert.AreEqual(new Vector3(-2f, 0.8f, 0f), rotorTail.transform.localPosition, "rotor_tail localPosition must be untouched.");
            Assert.Less(Quaternion.Angle(Quaternion.Euler(-90f, 0f, 0f), rotorTail.transform.localRotation), 1e-4f, "rotor_tail localRotation must be untouched.");

            Assert.AreEqual(new Vector3(0f, 1.2f, 0f), rotorUp.transform.localPosition, "rotor_up localPosition must be untouched.");
            Assert.Less(Quaternion.Angle(Quaternion.Euler(-90f, 0f, 0f), rotorUp.transform.localRotation), 1e-4f, "rotor_up localRotation must be untouched.");

            Object.DestroyImmediate(rotorUp);
            Object.DestroyImmediate(rotorTail);
            Object.DestroyImmediate(body);
        }

        // ---- 12. Flight translation logic remains authoritative and unchanged ----

        [Test]
        public void FlightTranslationLogicRemainsAuthoritativeAndUnchanged()
        {
            var flight = _flightRoot.AddComponent<CinematicHelicopterFlight>();
            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();

            // 7s covers the full Micro Task #5 phase sequence (GroundIdle 1.2s + VerticalLift
            // 1.8s + ForwardAcceleration 2.5s) and reaches Cruise.
            for (float t = 0f; t < 7f; t += 1f / 60f)
            {
                flight.AdvanceFlight(1f / 60f);
                motion.AdvanceVisualMotion(1f / 60f);
            }

            Assert.Greater(flight.DistanceTravelled, 20f, "Flight translation must proceed normally.");
            Assert.Greater(flight.HeightGained, 0.5f, "Flight climb must proceed normally.");
            Assert.IsTrue(flight.IsCruising, "Flight must reach cruising speed.");
            Assert.AreEqual(8f, flight.CurrentSpeed, 0.01f, "Flight cruise speed must reach 8m/s.");
        }

        // ---- 13. Forward axis X produces nose-down pitch ----

        [Test]
        public void ForwardAxisXSupportedAndProducesNoseDownPitch()
        {
            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            SetPrivate(motion, "localForwardAxis", new Vector3(1f, 0f, 0f));
            SetPrivate(motion, "localUpAxis", new Vector3(0f, 1f, 0f));
            SetPrivate(motion, "pitchAxis", new Vector3(0f, 0f, -1f));
            SetPrivate(motion, "takeoffPitchDegrees", 5f);
            SetPrivate(motion, "bobAmplitude", 0f); // isolate pitch
            SetPrivate(motion, "rollAmplitude", 0f); // isolate pitch

            Simulate(motion, 4f); // full cruise, pitch factor = 1

            // Forward vector is (1, 0, 0). Pitching nose down means it should dip towards -Y.
            Vector3 pitchedForward = _heliModel.transform.localRotation * new Vector3(1f, 0f, 0f);
            Assert.Less(pitchedForward.y, -0.05f, "Nose must pitch downward (negative Y component).");
            Assert.Greater(pitchedForward.x, 0.99f, "Forward component remains dominant.");
        }

        // ---- 14. Camera-follow compatibility ----

        [Test]
        public void CameraFollowTracksFlightRootIndependentlyOfVisualMotion()
        {
            var flight = _flightRoot.AddComponent<CinematicHelicopterFlight>();
            SetPrivate(flight, "localForwardAxis", new Vector3(1f, 0f, 0f));

            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();

            var camGo = new GameObject("CinematicCamera");
            try
            {
                var follow = camGo.AddComponent<CinematicHelicopterCameraFollow>();
                follow.Target = _flightRoot.transform;
                SetPrivate(follow, "targetForwardAxis", new Vector3(1f, 0f, 0f));

                for (int i = 0; i < 180; i++)
                {
                    flight.AdvanceFlight(1f / 60f);
                    motion.AdvanceVisualMotion(1f / 60f);
                    follow.UpdateFollow(1f / 60f);
                }

                float dist = Vector3.Distance(camGo.transform.position, _flightRoot.transform.position);
                Assert.Less(dist, 25f, "Camera must keep up with flight root.");
                Assert.Greater(dist, 5f, "Camera must maintain follow distance.");
            }
            finally
            {
                Object.DestroyImmediate(camGo);
            }
        }

        // ---- 15. Inspector fields exposed ----

        [Test]
        public void ExposesRequestedInspectorFields()
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            var t = typeof(CinematicHelicopterVisualMotion);
            foreach (var name in new[]
                     {
                         "takeoffPitchDegrees", "pitchStartDelay", "pitchAccelerationDuration",
                         "bobAmplitude", "bobFrequency", "rollAmplitude", "rollFrequency",
                         "visualBlendDuration", "localForwardAxis", "localUpAxis",
                         "pitchAxis", "rollAxis", "bobAxis", "flight", "motionEnabled", "useUnscaledTime"
                     })
            {
                Assert.IsNotNull(t.GetField(name, F), name + " must be Inspector-exposed.");
            }
        }

        // ---- 16. Default values match requested specifications ----

        [Test]
        public void DefaultValuesMatchRequestedSpec()
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();
            var t = typeof(CinematicHelicopterVisualMotion);

            float pitch = (float)t.GetField("takeoffPitchDegrees", F).GetValue(motion);
            Assert.GreaterOrEqual(pitch, 4f);
            Assert.LessOrEqual(pitch, 6f);

            float bobAmp = (float)t.GetField("bobAmplitude", F).GetValue(motion);
            Assert.GreaterOrEqual(bobAmp, 0.05f);
            Assert.LessOrEqual(bobAmp, 0.10f);

            float bobFreq = (float)t.GetField("bobFrequency", F).GetValue(motion);
            Assert.GreaterOrEqual(bobFreq, 1.0f);
            Assert.LessOrEqual(bobFreq, 1.5f);

            float rollAmp = (float)t.GetField("rollAmplitude", F).GetValue(motion);
            Assert.GreaterOrEqual(rollAmp, 0.5f);
            Assert.LessOrEqual(rollAmp, 1.0f);

            float rollFreq = (float)t.GetField("rollFrequency", F).GetValue(motion);
            Assert.GreaterOrEqual(rollFreq, 0.6f);
            Assert.LessOrEqual(rollFreq, 1.0f);

            float blend = (float)t.GetField("visualBlendDuration", F).GetValue(motion);
            Assert.GreaterOrEqual(blend, 1.0f);
            Assert.LessOrEqual(blend, 1.5f);

            Vector3 fwd = (Vector3)t.GetField("localForwardAxis", F).GetValue(motion);
            Assert.AreEqual(new Vector3(1f, 0f, 0f), fwd);

            Vector3 up = (Vector3)t.GetField("localUpAxis", F).GetValue(motion);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), up);

            Vector3 pitchAx = (Vector3)t.GetField("pitchAxis", F).GetValue(motion);
            Assert.AreEqual(new Vector3(0f, 0f, -1f), pitchAx);

            Vector3 rollAx = (Vector3)t.GetField("rollAxis", F).GetValue(motion);
            Assert.AreEqual(new Vector3(1f, 0f, 0f), rollAx);

            Vector3 bobAx = (Vector3)t.GetField("bobAxis", F).GetValue(motion);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), bobAx);
        }

        // ---- 17. Prevention of duplicate pitch ----

        [Test]
        public void PreventsDuplicatePitchWhenFlightAndVisualMotionCoexist()
        {
            var flight = _flightRoot.AddComponent<CinematicHelicopterFlight>();
            var motion = _heliModel.AddComponent<CinematicHelicopterVisualMotion>();

            // 6s reaches full cruise (Micro Task #5: forward acceleration completes at 5.5s),
            // so the visual pitch factor saturates at 1 like the pre-#5 timing did at 4s.
            for (int i = 0; i < 360; i++)
            {
                flight.AdvanceFlight(1f / 60f);
                motion.AdvanceVisualMotion(1f / 60f);
            }

            // Authoritative flight root must NOT have cosmetic pitch applied (stays at start rotation)
            Assert.Less(Quaternion.Angle(_flightRoot.transform.rotation, Quaternion.identity), 1e-4f,
                "Flight root must NOT apply cosmetic pitch, keeping rotation authoritative.");

            // Child model DOES have cosmetic pitch applied
            float modelAngle = Quaternion.Angle(_heliModel.transform.localRotation, Quaternion.identity);
            Assert.Greater(modelAngle, 3f,
                "Child model must receive cosmetic pitch from CinematicHelicopterVisualMotion.");
        }
    }
}
