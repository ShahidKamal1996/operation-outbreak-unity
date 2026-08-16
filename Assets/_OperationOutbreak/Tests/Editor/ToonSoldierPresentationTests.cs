using NUnit.Framework;
using OperationOutbreak.Player;
using OperationOutbreak.Weapons;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1P.5 QA fix #2 - EditMode tests for the presentation-only aim and
    /// muzzle binding. They pin the invariants the QA failures broke:
    ///   - visual aim rotates ONLY the presentation pivot, never the Player gameplay root;
    ///   - the pure yaw maths maps left/right/forward targets correctly and never jitters
    ///     or takes the long way around;
    ///   - muzzle binding re-parents the EXISTING authoritative MuzzlePoint (same
    ///     instance - no duplicate muzzle, no duplicate gameplay weapon) and skips
    ///     cleanly when the soldier fallback is inactive or the rig is missing.
    /// </summary>
    public sealed class ToonSoldierPresentationTests
    {
        // ===================================================== aim maths (pure)

        [Test]
        public void PlanarYaw_ForwardTargetIsZero()
        {
            Assert.AreEqual(0f, ToonSoldierPresentationAim.ComputePlanarYaw(Vector3.zero, new Vector3(0f, 0f, 5f)), 0.0001f,
                "A target straight ahead must produce forward yaw.");
        }

        [Test]
        public void PlanarYaw_LeftTargetIsNegative()
        {
            Assert.AreEqual(-45f, ToonSoldierPresentationAim.ComputePlanarYaw(Vector3.zero, new Vector3(-5f, 0f, 5f)), 0.0001f,
                "A target to the left must produce a negative yaw (left turn).");
        }

        [Test]
        public void PlanarYaw_RightTargetIsPositive()
        {
            Assert.AreEqual(45f, ToonSoldierPresentationAim.ComputePlanarYaw(Vector3.zero, new Vector3(5f, 0f, 5f)), 0.0001f,
                "A target to the right must produce a positive yaw (right turn).");
        }

        [Test]
        public void PlanarYaw_BehindIsOneEighty()
        {
            Assert.AreEqual(180f, Mathf.Abs(ToonSoldierPresentationAim.ComputePlanarYaw(Vector3.zero, new Vector3(0f, 0f, -5f))), 0.0001f,
                "A target behind must resolve to a 180 degree turn, not 0.");
        }

        [Test]
        public void TurnToward_ClampsToMaxDelta()
        {
            Assert.AreEqual(15f, ToonSoldierPresentationAim.TurnToward(10f, 20f, 5f, 0.5f), 0.0001f,
                "A single step must never exceed the max delta.");
        }

        [Test]
        public void TurnToward_TakesTheShortestPathAcrossTheWrap()
        {
            // From 179 to -179 the shortest route is 2 degrees THROUGH the +-180 wrap,
            // landing exactly on the target orientation. That orientation may come back
            // as 181 (= -179 + 360): Euler angles are periodic, and 181 and -179 are the
            // SAME orientation, so this test compares angular difference, never raw
            // floats. A 358-degree "long way" would show up as |moved| > 180.
            float result = ToonSoldierPresentationAim.TurnToward(179f, -179f, 5f, 0.5f);

            AssertAngularlyEquivalent(-179f, result, 1e-3f,
                "The result must BE the target orientation (181 is -179 + 360).");

            float moved = Mathf.DeltaAngle(179f, result);
            Assert.Less(Mathf.Abs(moved), 180f,
                "The turn must take the 2-degree short way through the wrap, not the 358-degree long way.");
            Assert.LessOrEqual(Mathf.Abs(moved), 5f + 1e-4f,
                "A single step must never exceed maxDelta, whichever representation comes back.");
        }

        [Test]
        public void TurnToward_WrapBoundaries_TakeTheShortDirectionAndRespectMaxDelta()
        {
            // Explicit boundary matrix from the QA brief. For each pair, assert:
            //  - movement never exceeds maxDelta,
            //  - movement is always the SHORT direction (never ~358 degrees),
            //  - the result never moves AWAY from the target,
            //  - when the target is reachable in one step, the result lands ON it.
            float[,] cases =
            {
                { 179f, -179f },
                { -179f, 179f },
                { 170f, -170f },
                { -170f, 170f },
            };

            for (int i = 0; i < cases.GetLength(0); i++)
            {
                float current = cases[i, 0];
                float desired = cases[i, 1];

                float result = ToonSoldierPresentationAim.TurnToward(current, desired, 5f, 0.5f);

                float moved = Mathf.Abs(Mathf.DeltaAngle(current, result));
                Assert.LessOrEqual(moved, 5f + 1e-4f,
                    $"{current} -> {desired}: one step must never exceed maxDelta.");
                Assert.Less(moved, 180f,
                    $"{current} -> {desired}: must take the short direction, not the long way around.");

                float remainingBefore = Mathf.Abs(Mathf.DeltaAngle(current, desired));
                float remainingAfter = Mathf.Abs(Mathf.DeltaAngle(result, desired));
                Assert.LessOrEqual(remainingAfter, remainingBefore + 1e-4f,
                    $"{current} -> {desired}: the step must not move away from the target.");

                if (remainingBefore <= 5f + 1e-4f)
                {
                    AssertAngularlyEquivalent(desired, result, 1e-3f,
                        $"{current} -> {desired}: a reachable target must be hit exactly " +
                        "(raw output may be the equivalent angle plus/minus 360).");
                }
            }
        }

        /// <summary>
        /// Euler angles are periodic: values differing by 360 represent the same
        /// orientation (181 == -179). Assertions must compare angular difference via
        /// DeltaAngle, never raw floats.
        /// </summary>
        private static void AssertAngularlyEquivalent(
            float expected, float actual, float tolerance, string message)
        {
            Assert.LessOrEqual(
                Mathf.Abs(Mathf.DeltaAngle(expected, actual)),
                tolerance,
                message + $" (expected {expected}, got {actual})");
        }

        [Test]
        public void TurnToward_SnapsWithinEpsilon()
        {
            Assert.AreEqual(20f, ToonSoldierPresentationAim.TurnToward(19.8f, 20f, 5f, 0.5f), 0.0001f,
                "Within the snap epsilon the pivot must settle exactly on the desired yaw.");
        }

        // ===================================================== aim application

        [Test]
        public void ApplyAim_RotatesPivotButNeverThePlayerRoot()
        {
            GameObject playerRoot = new GameObject("Player");
            GameObject pivotObject = new GameObject("ToonSoldierVisual");
            pivotObject.transform.SetParent(playerRoot.transform, false);

            ToonSoldierPresentationAim aim = playerRoot.AddComponent<ToonSoldierPresentationAim>();

            // Inject the serialized pivot (private) through the Unity editor API.
            var so = new SerializedObject(aim);
            so.FindProperty("presentationPivot").objectReferenceValue = pivotObject.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                GameObject leftTarget = new GameObject("TargetLeft");
                leftTarget.transform.position = new Vector3(-5f, 0f, 5f);

                aim.ApplyAim(leftTarget.transform, 1f); // 1s step: max delta > needed turn

                float pivotYaw = NormalizeEuler(pivotObject.transform.localEulerAngles.y);
                Assert.AreEqual(-45f, pivotYaw, 0.5f,
                    "The presentation pivot must face the left target.");
                Assert.AreEqual(Quaternion.identity, playerRoot.transform.rotation,
                    "The Player gameplay root must never be rotated by presentation aim.");
            }
            finally
            {
                Object.DestroyImmediate(playerRoot);
            }
        }

        [Test]
        public void ApplyAim_ReturnsToForwardWhenThereIsNoTarget()
        {
            GameObject playerRoot = new GameObject("Player");
            GameObject pivotObject = new GameObject("ToonSoldierVisual");
            pivotObject.transform.SetParent(playerRoot.transform, false);

            ToonSoldierPresentationAim aim = playerRoot.AddComponent<ToonSoldierPresentationAim>();
            var so = new SerializedObject(aim);
            so.FindProperty("presentationPivot").objectReferenceValue = pivotObject.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                GameObject rightTarget = new GameObject("TargetRight");
                rightTarget.transform.position = new Vector3(5f, 0f, 5f);

                aim.ApplyAim(rightTarget.transform, 1f);
                Assert.AreEqual(45f, NormalizeEuler(pivotObject.transform.localEulerAngles.y), 0.5f);

                aim.ApplyAim(null, 1f);
                Assert.AreEqual(0f, NormalizeEuler(pivotObject.transform.localEulerAngles.y), 0.5f,
                    "With no target the soldier must ease back to the forward pose.");
            }
            finally
            {
                Object.DestroyImmediate(playerRoot);
            }
        }

        // ===================================================== muzzle binding

        [Test]
        public void ShouldBind_FollowsTheFallbackRules()
        {
            Assert.IsTrue(WeaponMuzzleSocketBinder.ShouldBind(true, true, true),
                "Active soldier with a valid humanoid animator and hand bone must bind.");
            Assert.IsFalse(WeaponMuzzleSocketBinder.ShouldBind(false, true, true),
                "An inactive soldier (Carl/prototype fallback) must never bind.");
            Assert.IsFalse(WeaponMuzzleSocketBinder.ShouldBind(true, false, true),
                "Missing humanoid animator must never bind.");
            Assert.IsFalse(WeaponMuzzleSocketBinder.ShouldBind(true, true, false),
                "Missing hand bone must never bind.");
        }

        [Test]
        public void AttachMuzzleToSocket_ReparentsTheExistingMuzzleWithoutAddingAnything()
        {
            GameObject socketObject = new GameObject("Bip001 R Hand");
            GameObject muzzleObject = new GameObject("MuzzlePoint");
            muzzleObject.transform.position = new Vector3(0f, 1.25f, 5f);

            Transform muzzle = muzzleObject.transform;
            Vector3 offset = new Vector3(0f, 0f, 0.6f);

            WeaponMuzzleSocketBinder.AttachMuzzleToSocket(
                muzzle, socketObject.transform, offset, Vector3.zero);

            Assert.AreEqual(socketObject.transform, muzzle.parent,
                "The EXISTING MuzzlePoint must be parented to the socket - no new muzzle.");
            Assert.AreEqual(offset, muzzle.localPosition,
                "The authored barrel-tip offset must be applied in socket-local space.");
            Assert.AreEqual(Quaternion.identity, muzzle.localRotation);
            Assert.IsNull(socketObject.GetComponent<WeaponController>(),
                "Binding must never introduce a duplicate gameplay weapon authority.");

            // Child first: the muzzle is parented to the socket, so the socket is
            // destroyed last (DestroyImmediate twice on one object would log an error).
            Object.DestroyImmediate(muzzleObject);
            Object.DestroyImmediate(socketObject);
        }

        [Test]
        public void TryBind_DoesNothingWhenSoldierRootIsInactive()
        {
            GameObject soldierRoot = new GameObject("ToonSoldier_demo");
            GameObject muzzleObject = new GameObject("MuzzlePoint");
            soldierRoot.SetActive(false);

            var binder = muzzleObject.AddComponent<WeaponMuzzleSocketBinder>();
            var so = new SerializedObject(binder);
            so.FindProperty("soldierVisualRoot").objectReferenceValue = soldierRoot.transform;
            so.FindProperty("muzzlePoint").objectReferenceValue = muzzleObject.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            bool bound = binder.TryBind();

            Assert.IsFalse(bound, "An inactive soldier visual must not bind the muzzle.");
            Assert.IsNull(muzzleObject.transform.parent,
                "The muzzle must keep its authored hierarchy position on fallback.");

            Object.DestroyImmediate(soldierRoot);
            Object.DestroyImmediate(muzzleObject);
        }

        /// <summary>Unity euler angles are 0..360; normalise to -180..180 for sign assertions.</summary>
        private static float NormalizeEuler(float eulerY)
        {
            return Mathf.Repeat(eulerY + 180f, 360f) - 180f;
        }
    }
}
