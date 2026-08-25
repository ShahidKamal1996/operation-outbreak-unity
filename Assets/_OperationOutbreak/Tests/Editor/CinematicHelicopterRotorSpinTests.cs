using NUnit.Framework;
using OperationOutbreak.Cinematic;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Micro task #1 — lightweight structural tests for the rotor spin component.
    ///
    /// These verify the SAFETY CONTRACT (only rotor localRotation is ever written) and the
    /// Inspector surface. They deliberately do not test spin visuals or timing.
    /// </summary>
    public sealed class CinematicHelicopterRotorSpinTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("helicopter_rigged");

        [TearDown]
        public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(f, field + " must exist on CinematicHelicopterRotorSpin.");
            f.SetValue(target, value);
        }

        /// <summary>Unity does not run Update in Edit Mode; invoke the real method directly.</summary>
        private static void Tick(CinematicHelicopterRotorSpin spin)
        {
            var update = typeof(CinematicHelicopterRotorSpin).GetMethod("Update",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(update, "Update must exist.");
            update.Invoke(spin, null);
        }

        private CinematicHelicopterRotorSpin BuildRig(out Transform main, out Transform tail)
        {
            // Mirror the authored hierarchy, including the -90 X local rotation.
            var body = new GameObject("mian_body").transform;
            body.SetParent(_root.transform, false);
            body.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            main = new GameObject("rotor_up").transform;
            main.SetParent(_root.transform, false);
            main.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            tail = new GameObject("rotor_tail").transform;
            tail.SetParent(_root.transform, false);
            tail.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var spin = _root.AddComponent<CinematicHelicopterRotorSpin>();
            SetPrivate(spin, "mainRotor", main);
            SetPrivate(spin, "tailRotor", tail);
            return spin;
        }

        [Test]
        public void ExposesRequiredInspectorFields()
        {
            var t = typeof(CinematicHelicopterRotorSpin);
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            Assert.IsNotNull(t.GetField("mainRotor", F), "mainRotor must be serialized.");
            Assert.IsNotNull(t.GetField("tailRotor", F), "tailRotor must be serialized.");
            Assert.IsNotNull(t.GetField("mainRotorAxis", F), "mainRotorAxis must be Inspector-adjustable.");
            Assert.IsNotNull(t.GetField("tailRotorAxis", F), "tailRotorAxis must be Inspector-adjustable.");
            Assert.IsNotNull(t.GetField("mainRotorSpeed", F), "mainRotorSpeed must be serialized.");
            Assert.IsNotNull(t.GetField("tailRotorSpeed", F), "tailRotorSpeed must be serialized.");
        }

        [Test]
        public void DefaultSpeedsMatchRequestedValues()
        {
            var spin = _root.AddComponent<CinematicHelicopterRotorSpin>();
            var t = typeof(CinematicHelicopterRotorSpin);
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            Assert.AreEqual(1500f, (float)t.GetField("mainRotorSpeed", F).GetValue(spin), 0.01f);
            Assert.AreEqual(2200f, (float)t.GetField("tailRotorSpeed", F).GetValue(spin), 0.01f);
        }

        [Test]
        public void SpinChangesOnlyRotationNeverPositionOrScale()
        {
            var spin = BuildRig(out Transform main, out Transform tail);

            Vector3 rootPos = _root.transform.position;
            Quaternion rootRot = _root.transform.rotation;
            Vector3 rootScale = _root.transform.localScale;

            Vector3 mainPos = main.localPosition, mainScale = main.localScale;
            Vector3 tailPos = tail.localPosition, tailScale = tail.localScale;
            Quaternion mainRotBefore = main.localRotation;

            // Edit Mode does not drive a real frame loop, so Time.unscaledDeltaTime may legitimately
            // be 0 here. Capture whether any time actually elapsed so the "did it rotate" assertion
            // below cannot become flaky on a zero-length frame.
            bool timeAdvanced = Time.unscaledDeltaTime > 0f;

            for (int i = 0; i < 5; i++) Tick(spin);

            // The helicopter root must be completely untouched.
            Assert.AreEqual(rootPos, _root.transform.position, "Root position must not change.");
            Assert.AreEqual(rootRot, _root.transform.rotation, "Root rotation must not change.");
            Assert.AreEqual(rootScale, _root.transform.localScale, "Root scale must not change.");

            // Rotor positions/scales must be untouched — only rotation may change.
            Assert.AreEqual(mainPos, main.localPosition, "Main rotor position must not change.");
            Assert.AreEqual(mainScale, main.localScale, "Main rotor scale must not change.");
            Assert.AreEqual(tailPos, tail.localPosition, "Tail rotor position must not change.");
            Assert.AreEqual(tailScale, tail.localScale, "Tail rotor scale must not change.");

            // The invariant under test is "position/scale never change". Only assert that rotation
            // DID change when the frame actually had a non-zero duration.
            if (timeAdvanced)
                Assert.AreNotEqual(mainRotBefore, main.localRotation,
                    "The main rotor should actually have rotated.");
        }

        [Test]
        public void BodyTransformIsNeverModified()
        {
            var spin = BuildRig(out _, out _);
            Transform body = _root.transform.Find("mian_body");
            Assert.IsNotNull(body);

            Quaternion rot = body.localRotation;
            Vector3 pos = body.localPosition;
            Vector3 scale = body.localScale;

            for (int i = 0; i < 5; i++) Tick(spin);

            Assert.AreEqual(rot, body.localRotation, "mian_body rotation must never change.");
            Assert.AreEqual(pos, body.localPosition, "mian_body position must never change.");
            Assert.AreEqual(scale, body.localScale, "mian_body scale must never change.");
        }

        [Test]
        public void NullRotorsAreSafe()
        {
            var spin = _root.AddComponent<CinematicHelicopterRotorSpin>();
            Assert.DoesNotThrow(() => Tick(spin),
                "Unassigned rotor references must be skipped, not throw.");
        }

        [Test]
        public void ZeroAxisIsSafeAndDoesNotCorruptRotation()
        {
            var spin = BuildRig(out Transform main, out _);
            SetPrivate(spin, "mainRotorAxis", Vector3.zero);

            Quaternion before = main.localRotation;
            Assert.DoesNotThrow(() => Tick(spin), "A zero axis must not throw.");

            Assert.AreEqual(before, main.localRotation,
                "A zero axis must leave the rotation untouched rather than producing NaN.");
        }

        [Test]
        public void SpinCanBeDisabled()
        {
            var spin = BuildRig(out Transform main, out Transform tail);
            spin.SpinEnabled = false;

            Quaternion mainBefore = main.localRotation;
            Quaternion tailBefore = tail.localRotation;
            for (int i = 0; i < 5; i++) Tick(spin);

            Assert.AreEqual(mainBefore, main.localRotation, "Disabled spin must not rotate the main rotor.");
            Assert.AreEqual(tailBefore, tail.localRotation, "Disabled spin must not rotate the tail rotor.");
        }

        [Test]
        public void AxisIsNormalizedSoMagnitudeDoesNotAffectSpeed()
        {
            // A long axis vector must not multiply the effective spin rate.
            var spinA = BuildRig(out Transform mainA, out _);
            SetPrivate(spinA, "mainRotorAxis", new Vector3(0f, 0f, 1f));
            SetPrivate(spinA, "useUnscaledTime", false);

            var holderB = new GameObject("rigB");
            try
            {
                var mainB = new GameObject("rotor_up_b").transform;
                mainB.SetParent(holderB.transform, false);
                var spinB = holderB.AddComponent<CinematicHelicopterRotorSpin>();
                SetPrivate(spinB, "mainRotor", mainB);
                SetPrivate(spinB, "mainRotorAxis", new Vector3(0f, 0f, 100f));
                SetPrivate(spinB, "useUnscaledTime", false);

                mainA.localRotation = Quaternion.identity;
                mainB.localRotation = Quaternion.identity;

                Tick(spinA);
                Tick(spinB);

                Assert.AreEqual(Quaternion.Angle(Quaternion.identity, mainA.localRotation),
                                Quaternion.Angle(Quaternion.identity, mainB.localRotation), 0.01f,
                    "Axis magnitude must not affect spin speed — the axis must be normalized.");
            }
            finally
            {
                Object.DestroyImmediate(holderB);
            }
        }
    }
}
