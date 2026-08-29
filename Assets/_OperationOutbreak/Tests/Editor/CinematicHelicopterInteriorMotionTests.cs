using NUnit.Framework;
using OperationOutbreak.Cinematic;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Interior Micro Task #2 — tests for <see cref="CinematicHelicopterInteriorMotion"/>:
    /// authored-pose capture, drift-free determinism, bounded motion, disabled behavior,
    /// child/player isolation, and clean restore on disable.
    /// </summary>
    public sealed class CinematicHelicopterInteriorMotionTests
    {
        private GameObject _parent;
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("InteriorParent");
            _root = new GameObject("HelicopterInterior_Manual");
            _root.transform.SetParent(_parent.transform, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_parent != null) Object.DestroyImmediate(_parent);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(f, field + " must exist on " + target.GetType().Name + ".");
            f.SetValue(target, value);
        }

        private CinematicHelicopterInteriorMotion AddMotion() =>
            _root.AddComponent<CinematicHelicopterInteriorMotion>();

        // ---- authored pose capture ----

        [Test]
        public void CapturesAuthoredLocalPoseAtStartupAndStartsExactlyAtAuthoredPose()
        {
            // The root must be usable with ANY authored local pose: the captured base is the
            // local pose, and at t = 0 the motion offset is exactly zero.
            _parent.transform.SetPositionAndRotation(new Vector3(5f, 2f, -7f), Quaternion.Euler(10f, 30f, -5f));
            _root.transform.localPosition = new Vector3(0.5f, 0.25f, -1.25f);
            _root.transform.localRotation = Quaternion.Euler(3f, -8f, 2f);
            var authoredPos = _root.transform.localPosition;
            var authoredRot = _root.transform.localRotation;

            var motion = AddMotion();

            // Adding the component must not move anything.
            Assert.AreEqual(authoredPos, _root.transform.localPosition,
                "AddComponent must not move the root.");
            Assert.Less(Quaternion.Angle(authoredRot, _root.transform.localRotation), 0.001f);

            // First movement frame at t = 0: the root sits EXACTLY at the authored pose.
            motion.AdvanceMotion(0f);
            Assert.AreEqual(authoredPos.x, _root.transform.localPosition.x, 1e-5f,
                "Captured pose X must be the authored local X.");
            Assert.AreEqual(authoredPos.y, _root.transform.localPosition.y, 1e-5f,
                "Captured pose Y must be the authored local Y.");
            Assert.AreEqual(authoredPos.z, _root.transform.localPosition.z, 1e-5f,
                "Captured pose Z must be the authored local Z.");
            Assert.Less(Quaternion.Angle(authoredRot, _root.transform.localRotation), 0.0001f,
                "At t = 0 the root must be exactly at its authored rotation.");
            Assert.AreEqual(0f, motion.Elapsed, 1e-5f, "No time may have elapsed at startup.");
        }

        // ---- no cumulative drift ----

        [Test]
        public void NeverAccumulatesDriftOverLongRun()
        {
            // The root starts at the identity authored pose; over a long run its deviation from
            // the authored pose must stay bounded (no accumulation) and must not grow: the
            // largest deviation seen in the LAST 10s must not exceed the largest deviation in
            // the FIRST 10s.
            var motion = AddMotion();

            const int framesPerSecond = 60;
            int totalFrames = 120 * framesPerSecond; // 120 s
            float maxMagnitude = 0f;
            float maxFirst10s = 0f;
            float maxLast10s = 0f;

            for (int frame = 0; frame < totalFrames; frame++)
            {
                motion.AdvanceMotion(1f / 60f);
                Vector3 p = _root.transform.localPosition;
                float magnitude = p.magnitude;
                maxMagnitude = Mathf.Max(maxMagnitude, magnitude);

                if (frame < 10 * framesPerSecond) maxFirst10s = Mathf.Max(maxFirst10s, magnitude);
                if (frame >= totalFrames - 10 * framesPerSecond) maxLast10s = Mathf.Max(maxLast10s, magnitude);

                // Bounded at every frame, per axis (the guaranteed envelope):
                // |ΔY| ≤ bob 0.025 + vibration 0.006, |ΔX| ≤ forward 0.01 + vibration 0.006.
                Assert.LessOrEqual(Mathf.Abs(p.y), 0.025f + 0.006f + 1e-4f,
                    "Vertical offset may never exceed bob + vibration (frame " + frame + ").");
                Assert.LessOrEqual(Mathf.Abs(p.x), 0.01f + 0.006f + 1e-4f,
                    "Forward/back offset may never exceed forward + vibration (frame " + frame + ").");
            }

            Assert.Greater(maxFirst10s, 0.02f,
                "Sanity: with default amplitudes the cabin must actually move (peak bob ~0.025m).");
            Assert.LessOrEqual(maxLast10s, maxFirst10s + 1e-3f,
                "Deviation must not grow over time — the motion is bounded around the authored pose, never accumulated.");
        }

        // ---- deterministic motion ----

        [Test]
        public void MotionIsDeterministicForSameElapsedTime()
        {
            // Two identically authored roots advanced with identical dt sequences must produce
            // identical poses at every sampled frame — the pose is a pure function of elapsed
            // time, independent of which component instance is driving it.
            var authoredLocal = new Vector3(0.25f, 0.1f, -0.5f);
            var authoredLocalRot = Quaternion.Euler(1.5f, 7f, -0.5f);
            _parent.transform.SetPositionAndRotation(new Vector3(3f, -2f, 8f), Quaternion.Euler(12f, -40f, 4f));
            _root.transform.localPosition = authoredLocal;
            _root.transform.localRotation = authoredLocalRot;

            var otherParent = new GameObject("OtherParent");
            var otherRoot = new GameObject("OtherInteriorRoot");
            try
            {
                otherRoot.transform.SetParent(otherParent.transform, false);
                otherParent.transform.SetPositionAndRotation(_parent.transform.position, _parent.transform.rotation);
                otherRoot.transform.localPosition = authoredLocal;
                otherRoot.transform.localRotation = authoredLocalRot;

                var motionA = _root.AddComponent<CinematicHelicopterInteriorMotion>();
                var motionB = otherRoot.AddComponent<CinematicHelicopterInteriorMotion>();

                for (int frame = 0; frame < 600; frame++)
                {
                    motionA.AdvanceMotion(1f / 60f);
                    motionB.AdvanceMotion(1f / 60f);

                    if (frame % 60 == 59)
                    {
                        Assert.AreEqual(_root.transform.localPosition.x, otherRoot.transform.localPosition.x, 1e-5f,
                            "Same elapsed time must give the same X offset (t=" + (frame + 1f) / 60f + "s).");
                        Assert.AreEqual(_root.transform.localPosition.y, otherRoot.transform.localPosition.y, 1e-5f,
                            "Same elapsed time must give the same Y offset (t=" + (frame + 1f) / 60f + "s).");
                        Assert.AreEqual(_root.transform.localPosition.z, otherRoot.transform.localPosition.z, 1e-5f,
                            "Same elapsed time must give the same Z offset (t=" + (frame + 1f) / 60f + "s).");
                        Assert.Less(Quaternion.Angle(_root.transform.localRotation, otherRoot.transform.localRotation),
                            0.0001f, "Same elapsed time must give the same rotation offset.");
                    }
                }

                // History independence: reaching the same elapsed time by two 300-frame steps
                // (instead of one 600-frame run) must land on the same pose. The control root
                // must be based on the AUTHORED pose — not on _root's current (already moved)
                // pose, which would stack a second motion offset on top.
                GameObject thirdParent = new GameObject("ThirdParent");
                try
                {
                    var thirdRoot = new GameObject("ThirdInteriorRoot");
                    thirdRoot.transform.SetParent(thirdParent.transform, false);
                    thirdParent.transform.SetPositionAndRotation(_parent.transform.position, _parent.transform.rotation);
                    thirdRoot.transform.localPosition = authoredLocal;
                    thirdRoot.transform.localRotation = authoredLocalRot;

                    var motionC = thirdRoot.AddComponent<CinematicHelicopterInteriorMotion>();
                    for (int frame = 0; frame < 300; frame++) motionC.AdvanceMotion(1f / 60f);
                    for (int frame = 0; frame < 300; frame++) motionC.AdvanceMotion(1f / 60f);

                    Assert.AreEqual(_root.transform.localPosition.x, thirdRoot.transform.localPosition.x, 1e-4f,
                        "Split advancement to the same elapsed time must land on the same pose (X).");
                    Assert.AreEqual(_root.transform.localPosition.y, thirdRoot.transform.localPosition.y, 1e-4f,
                        "Split advancement to the same elapsed time must land on the same pose (Y).");
                    Assert.Less(Quaternion.Angle(_root.transform.localRotation, thirdRoot.transform.localRotation),
                        0.001f, "Split advancement to the same elapsed time must land on the same pose (rotation).");
                }
                finally { Object.DestroyImmediate(thirdParent); }
            }
            finally
            {
                if (otherParent != null) Object.DestroyImmediate(otherParent);
            }
        }

        // ---- configured bounds ----

        [Test]
        public void MotionStaysWithinConfiguredBounds()
        {
            // With custom (larger) amplitudes, every frame's offset must stay inside the
            // configured envelope: Y within bob+vibration, X within forward+vibration,
            // Z untouched, and rotation within roll+pitch.
            var motion = AddMotion();
            SetPrivate(motion, "bobAmplitude", 0.05f);
            SetPrivate(motion, "bobFrequency", 1.7f);
            SetPrivate(motion, "rollAmplitude", 1.0f);
            SetPrivate(motion, "rollFrequency", 1.1f);
            SetPrivate(motion, "pitchAmplitude", 0.6f);
            SetPrivate(motion, "pitchFrequency", 0.9f);
            SetPrivate(motion, "forwardAmplitude", 0.02f);
            SetPrivate(motion, "forwardFrequency", 0.7f);
            SetPrivate(motion, "microVibrationAmplitude", 0.01f);
            SetPrivate(motion, "microVibrationFrequency", 9.0f);

            for (int frame = 0; frame < 900; frame++)
            {
                motion.AdvanceMotion(1f / 60f);
                Vector3 p = _root.transform.localPosition;

                Assert.LessOrEqual(Mathf.Abs(p.y), 0.05f + 0.01f + 1e-4f,
                    "Vertical offset must stay within bob + vibration (frame " + frame + ").");
                Assert.LessOrEqual(Mathf.Abs(p.x), 0.02f + 0.01f + 1e-4f,
                    "Forward/back offset must stay within forward + vibration (frame " + frame + ").");
                Assert.Less(Mathf.Abs(p.z), 1e-5f,
                    "No Z offset may ever be applied (frame " + frame + ").");
                Assert.LessOrEqual(Quaternion.Angle(Quaternion.identity, _root.transform.localRotation),
                    1.0f + 0.6f + 0.001f,
                    "Rotation offset must stay within roll + pitch (frame " + frame + ").");
            }
        }

        // ---- disabled behavior ----

        [Test]
        public void DisabledMotionDoesNotMoveRoot()
        {
            _root.transform.localPosition = new Vector3(1.5f, -0.5f, 2.25f);
            _root.transform.localRotation = Quaternion.Euler(2f, 55f, -1f);
            var authoredPos = _root.transform.localPosition;
            var authoredRot = _root.transform.localRotation;

            var motion = AddMotion();
            motion.MotionEnabled = false;

            for (int frame = 0; frame < 120; frame++)
            {
                motion.AdvanceMotion(1f / 60f);
                Assert.AreEqual(authoredPos.x, _root.transform.localPosition.x, 1e-5f,
                    "Disabled motion must keep the root exactly at the authored X (frame " + frame + ").");
                Assert.AreEqual(authoredPos.y, _root.transform.localPosition.y, 1e-5f,
                    "Disabled motion must keep the root exactly at the authored Y (frame " + frame + ").");
                Assert.AreEqual(authoredPos.z, _root.transform.localPosition.z, 1e-5f,
                    "Disabled motion must keep the root exactly at the authored Z (frame " + frame + ").");
                Assert.Less(Quaternion.Angle(authoredRot, _root.transform.localRotation), 0.0001f,
                    "Disabled motion must keep the authored rotation (frame " + frame + ").");
            }
            Assert.AreEqual(0f, motion.Elapsed, 1e-5f, "Disabled motion must not advance the clock.");

            // Toggling back on must move, and toggling off must SNAP back to the authored pose
            // (never freeze at a mid-offset).
            motion.MotionEnabled = true;
            for (int frame = 0; frame < 60; frame++) motion.AdvanceMotion(1f / 60f);
            Assert.Greater(Vector3.Distance(_root.transform.localPosition, authoredPos), 0.001f,
                "Sanity: enabled motion must move the root.");
            motion.MotionEnabled = false;
            motion.AdvanceMotion(1f / 60f);
            Assert.AreEqual(authoredPos.x, _root.transform.localPosition.x, 1e-5f,
                "Re-disabling must restore the authored X exactly.");
            Assert.AreEqual(authoredPos.y, _root.transform.localPosition.y, 1e-5f,
                "Re-disabling must restore the authored Y exactly.");
            Assert.AreEqual(authoredPos.z, _root.transform.localPosition.z, 1e-5f,
                "Re-disabling must restore the authored Z exactly.");
            Assert.Less(Quaternion.Angle(authoredRot, _root.transform.localRotation), 0.0001f,
                "Re-disabling must restore the authored rotation exactly.");
        }

        // ---- children are carried, never driven ----

        [Test]
        public void ChildLocalTransformsAreUntouchedByMotion()
        {
            // Every interior object (cabin, bench, lights, player, rifle) lives UNDER the root;
            // they must ride along untouched: local pos/rot/scale unchanged, no reparenting.
            _root.transform.localPosition = new Vector3(0.75f, 0f, -1.5f);
            _root.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);

            var shell = new GameObject("CabinShell");
            shell.transform.SetParent(_root.transform, false);
            shell.transform.localPosition = new Vector3(0f, 0f, 0.35f);
            shell.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            shell.transform.localScale = new Vector3(2f, 2f, 2f);

            var bench = new GameObject("Bench");
            bench.transform.SetParent(_root.transform, false);
            bench.transform.localPosition = new Vector3(-0.8f, 0.4f, 0f);
            bench.transform.localScale = new Vector3(1f, 1.5f, 3f);

            var light = new GameObject("CabinLight");
            light.transform.SetParent(_root.transform, false);
            light.transform.localPosition = new Vector3(0.2f, 2.1f, -0.4f);
            light.transform.localRotation = Quaternion.Euler(45f, 0f, 10f);
            light.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

            var shellPos = shell.transform.localPosition;
            var shellRot = shell.transform.localRotation;
            var shellScale = shell.transform.localScale;
            var benchPos = bench.transform.localPosition;
            var benchScale = bench.transform.localScale;
            var lightPos = light.transform.localPosition;
            var lightRot = light.transform.localRotation;
            var lightScale = light.transform.localScale;

            var motion = AddMotion();
            for (int frame = 0; frame < 300; frame++) motion.AdvanceMotion(1f / 60f);

            Assert.AreEqual(shellPos, shell.transform.localPosition, "CabinShell local position must be untouched.");
            Assert.AreEqual(shellRot, shell.transform.localRotation, "CabinShell local rotation must be untouched.");
            Assert.AreEqual(shellScale, shell.transform.localScale, "CabinShell local scale must be untouched.");
            Assert.AreEqual(benchPos, bench.transform.localPosition, "Bench local position must be untouched.");
            Assert.AreEqual(benchScale, bench.transform.localScale, "Bench local scale must be untouched.");
            Assert.AreEqual(lightPos, light.transform.localPosition, "CabinLight local position must be untouched.");
            Assert.AreEqual(lightRot, light.transform.localRotation, "CabinLight local rotation must be untouched.");
            Assert.AreEqual(lightScale, light.transform.localScale, "CabinLight local scale must be untouched.");

            Assert.AreEqual(3, _root.transform.childCount, "No children may be added or removed.");
            Assert.AreSame(_root.transform, shell.transform.parent, "No reparenting (shell).");
            Assert.AreSame(_root.transform, bench.transform.parent, "No reparenting (bench).");
            Assert.AreSame(_root.transform, light.transform.parent, "No reparenting (light).");
            Assert.AreSame(_parent.transform, _root.transform.parent, "The root itself must not be reparented.");
        }

        // ---- any authored world pose ----

        [Test]
        public void PreservesAuthoredNonZeroRootTransformAtAnyPoseInWorld()
        {
            // The manually authored root may live anywhere, at any orientation (world or local).
            // The motion must orbit its authored WORLD pose: near the authored transform
            // always, never snapping toward the origin or drifting away.
            var parentPos = new Vector3(-12.5f, 3.2f, 40f);
            var parentRot = Quaternion.Euler(-4f, 117.5f, 1.25f);
            _parent.transform.SetPositionAndRotation(parentPos, parentRot);
            var authoredLocal = new Vector3(2.5f, 0f, -1.75f);
            var authoredLocalRot = Quaternion.Euler(0f, 20f, 0f);
            _root.transform.localPosition = authoredLocal;
            _root.transform.localRotation = authoredLocalRot;

            var authoredWorldPos = _parent.transform.TransformPoint(authoredLocal);
            var authoredWorldRot = parentRot * authoredLocalRot;

            var motion = AddMotion();

            for (int frame = 1; frame <= 300; frame++)
            {
                motion.AdvanceMotion(1f / 60f);
                float worldDistance = Vector3.Distance(_root.transform.position, authoredWorldPos);
                Assert.LessOrEqual(worldDistance, 0.025f + 0.006f + 0.01f + 0.005f,
                    "The root must stay within its motion envelope of the authored WORLD pose (frame " + frame + ").");
                // The authored WORLD pose is ~41.6m from the world origin; the root must stay
                // there — it must never snap toward the origin. (Measuring the offset from the
                // authored pose can never exceed the small configured amplitudes, so the
                // origin-distance is the correct liveness check.)
                float originDistance = Vector3.Distance(_root.transform.position, Vector3.zero);
                Assert.Greater(originDistance, 30f,
                    "The root must never snap toward the world origin (frame " + frame + ").");
            }

            Assert.Less(Quaternion.Angle(_root.transform.rotation, authoredWorldRot), 0.45f + 0.25f + 0.05f,
                "The authored WORLD rotation must be preserved up to the configured sway envelope.");
        }

        // ---- no player-specific logic ----

        [Test]
        public void ContainsNoPlayerSpecificLogic()
        {
            // The motion must not search for, target, or modify anything player-related. A
            // full interior-like child set (including a player with an Animator and a rifle)
            // must come out of the run byte-identical in local pose, naming, and parentage.
            _root.transform.localPosition = new Vector3(0f, 0f, 0f);

            var player = new GameObject("Player");
            player.transform.SetParent(_root.transform, false);
            player.transform.localPosition = new Vector3(-1.5f, 0.5f, 0f);
            var animator = player.AddComponent<Animator>();

            var rifle = new GameObject("Rifle");
            rifle.transform.SetParent(_root.transform, false);
            rifle.transform.localPosition = new Vector3(-1.35f, 0.9f, 0.1f);
            rifle.transform.localRotation = Quaternion.Euler(8f, 12f, -3f);

            var cabinCamera = new GameObject("CabinCamera");
            cabinCamera.transform.SetParent(_root.transform, false);
            cabinCamera.transform.localPosition = new Vector3(0.5f, 1.25f, -1.35f);
            cabinCamera.transform.localRotation = Quaternion.Euler(12f, -4f, 0f);

            var playerPos = player.transform.localPosition;
            var riflePos = rifle.transform.localPosition;
            var rifleRot = rifle.transform.localRotation;
            var camPos = cabinCamera.transform.localPosition;
            var camRot = cabinCamera.transform.localRotation;
            bool animatorWasEnabled = animator.enabled;

            var motion = AddMotion();
            for (int frame = 0; frame < 300; frame++) motion.AdvanceMotion(1f / 60f);

            Assert.AreEqual(playerPos, player.transform.localPosition,
                "The seated player's local transform must never be modified (no player logic).");
            Assert.AreEqual(riflePos, rifle.transform.localPosition, "The rifle's local position must never be modified.");
            Assert.AreEqual(rifleRot, rifle.transform.localRotation, "The rifle's local rotation must never be modified.");
            Assert.AreEqual(camPos, cabinCamera.transform.localPosition, "The cabin camera's local position must never be modified.");
            Assert.AreEqual(camRot, cabinCamera.transform.localRotation, "The cabin camera's local rotation must never be modified.");
            Assert.IsTrue(animator.enabled == animatorWasEnabled,
                "The seated animation (Animator) must be left exactly as found.");
            Assert.AreEqual(3, _root.transform.childCount, "No children may be added or removed.");
            Assert.AreSame(_root.transform, player.transform.parent, "The player must not be reparented.");
            Assert.AreSame(_root.transform, rifle.transform.parent, "The rifle must not be reparented.");
            Assert.AreSame(_root.transform, cabinCamera.transform.parent, "The camera must not be reparented.");

            // Structural check: the component holds no references to any Transform/GameObject/
            // Component — it cannot target the player, a camera, or any other scene object.
            foreach (var field in typeof(CinematicHelicopterInteriorMotion).GetFields(
                         System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            {
                Assert.IsFalse(typeof(Component).IsAssignableFrom(field.FieldType),
                    "Field '" + field.Name + "' must not reference a Component (no player/camera targeting).");
                Assert.IsFalse(typeof(GameObject).IsAssignableFrom(field.FieldType),
                    "Field '" + field.Name + "' must not reference a GameObject (no player/camera targeting).");
            }
        }

        // ---- clean restore on disable ----

        [Test]
        public void RestoresAuthoredPoseCleanlyWhenDisabled()
        {
            // The production contract: when motion is DISABLED, the root must return exactly to
            // its captured authored local position/rotation — never left at a mid-offset.
            // This exercises the real disable path (component.enabled = false -> OnDisable).
            // Component destruction is deliberately NOT substituted: in the Unity 6000.5.7f1
            // EditMode runner, Object.DestroyImmediate on a single component does not reliably
            // invoke the restore callbacks (observed: the root was left at its last mid-offset
            // pose), so destruction is not a valid stand-in for the disable contract.
            _root.transform.localPosition = new Vector3(2.75f, 0.5f, -1.1f);
            _root.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
            var authoredPos = _root.transform.localPosition;
            var authoredRot = _root.transform.localRotation;

            var motion = AddMotion();
            for (int frame = 0; frame < 60; frame++) motion.AdvanceMotion(1f / 60f);
            Assert.Greater(Vector3.Distance(_root.transform.localPosition, authoredPos), 0.001f,
                "Sanity: the root must be mid-offset before the component is disabled (t≈1s, peak bob ~0.02m).");

            motion.enabled = false; // the production disable path — triggers OnDisable

            Assert.AreEqual(authoredPos.x, _root.transform.localPosition.x, 1e-5f,
                "On disable the root must be restored to the authored X exactly.");
            Assert.AreEqual(authoredPos.y, _root.transform.localPosition.y, 1e-5f,
                "On disable the root must be restored to the authored Y exactly.");
            Assert.AreEqual(authoredPos.z, _root.transform.localPosition.z, 1e-5f,
                "On disable the root must be restored to the authored Z exactly.");
            Assert.Less(Quaternion.Angle(authoredRot, _root.transform.localRotation), 0.0001f,
                "On disable the root must be restored to the authored rotation exactly.");
        }
    }
}
