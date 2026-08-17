using NUnit.Framework;
using OperationOutbreak.Player;
using OperationOutbreak.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1P.5 QA fixes #2-#9 - EditMode tests for the presentation-only aim and
    /// muzzle follow. They pin the invariants the QA failures broke:
    ///   - visual aim rotates ONLY the presentation pivot, never the Player gameplay root;
    ///   - the pure yaw maths maps left/right/forward targets correctly and never jitters
    ///     or takes the long way around;
    ///   - the muzzle FOLLOWS the soldier socket but is never re-parented (its parent
    ///     under the Weapon never changes - single authority, no duplicates, no
    ///     SetParent-during-deactivation errors);
    ///   - the obsolete prototype weapon visual is hidden exactly when the Toon Soldier
    ///     is active and bound, and restored for the Carl/prototype fallback.
    /// The fixture-level TearDown fails any test during which Unity logs an unexpected
    /// error - which is what would catch the "Cannot set the parent ... while activating
    /// or deactivating" regression class permanently.
    /// </summary>
    public sealed class ToonSoldierPresentationTests
    {
        [TearDown]
        public void FailOnUnexpectedUnityErrors()
        {
            LogAssert.NoUnexpectedReceived();
        }

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

        // ============================================= QA fix #8/#9 - follow architecture

        [Test]
        public void WriteFollowPose_MovesTheSingleMuzzleToTheSocketWithoutChangingParent()
        {
            GameObject weaponRoot = new GameObject("Weapon");
            GameObject muzzleObject = new GameObject("MuzzlePoint");
            muzzleObject.transform.SetParent(weaponRoot.transform, false);
            muzzleObject.transform.localPosition = new Vector3(0f, 0.25f, 0.75f);

            GameObject socketObject = new GameObject("ToonSoldierMuzzleSocket");
            socketObject.transform.position = new Vector3(-0.9f, 1.15f, 4.2f);
            socketObject.transform.rotation = Quaternion.Euler(0f, 33f, 0f);

            try
            {
                Transform originalParent = muzzleObject.transform.parent;

                WeaponMuzzleSocketBinder.WriteFollowPose(
                    muzzleObject.transform, socketObject.transform);

                Assert.AreEqual(socketObject.transform.position, muzzleObject.transform.position,
                    "The follow tick must place the muzzle exactly at the socket.");
                Assert.AreEqual(socketObject.transform.rotation, muzzleObject.transform.rotation,
                    "The follow tick must orient the muzzle exactly like the socket.");
                Assert.AreEqual(originalParent, muzzleObject.transform.parent,
                    "Follow must NEVER change the muzzle's parent - the single authoritative " +
                    "MuzzlePoint stays owned by the Weapon (this is what removes the " +
                    "SetParent-during-deactivation error class).");
                Assert.IsNull(socketObject.GetComponent<WeaponController>(),
                    "Binding must never introduce a duplicate gameplay weapon authority.");
            }
            finally
            {
                Object.DestroyImmediate(muzzleObject);
                Object.DestroyImmediate(weaponRoot);
                Object.DestroyImmediate(socketObject);
            }
        }

        [Test]
        public void ShouldHidePrototypeWeapon_HidesOnlyWhenSoldierActiveAndBound()
        {
            Assert.IsTrue(
                WeaponMuzzleSocketBinder.ShouldHidePrototypeWeapon(true),
                "With the Toon Soldier visual active, the obsolete prototype gun must be " +
                "hidden - the soldier's skinned rifle is the only visible weapon.");
            Assert.IsFalse(
                WeaponMuzzleSocketBinder.ShouldHidePrototypeWeapon(false),
                "The Carl/prototype fallback must keep the old prototype gun visible, as it " +
                "was before the Toon Soldier integration.");
        }

        [Test]
        public void RefreshPrototypeWeaponVisibility_DisablesRendererWhileSoldierActive()
        {
            // Reproduces the QA fix #11 runtime failure: a scene with the soldier visual
            // active and the obsolete prototype gun renderer enabled. The binder must
            // disable the renderer even though muzzle binding is NOT involved at all.
            GameObject soldierRoot = new GameObject("ToonSoldier_demo"); // active
            GameObject weaponModel = new GameObject("WeaponModel");
            MeshRenderer renderer = weaponModel.AddComponent<MeshRenderer>();

            var binder = new GameObject("Binder").AddComponent<WeaponMuzzleSocketBinder>();
            var so = new SerializedObject(binder);
            so.FindProperty("soldierVisualRoot").objectReferenceValue = soldierRoot.transform;
            so.FindProperty("prototypeWeaponRoot").objectReferenceValue = weaponModel.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                Assert.IsTrue(renderer.enabled, "Precondition: the prototype renderer starts enabled.");

                binder.RefreshPrototypeWeaponVisibility();

                Assert.IsFalse(renderer.enabled,
                    "With the Toon Soldier visual active, the prototype gun's renderer must be " +
                    "disabled - this is the exact runtime failure QA fix #11 reported.");
            }
            finally
            {
                Object.DestroyImmediate(binder.gameObject);
                Object.DestroyImmediate(weaponModel);
                Object.DestroyImmediate(soldierRoot);
            }
        }

        [Test]
        public void RefreshPrototypeWeaponVisibility_RestoresRendererWhenSoldierInactive()
        {
            // Carl/prototype fallback: the soldier visual layer is inactive, so the old
            // prototype gun must become visible again.
            GameObject soldierRoot = new GameObject("ToonSoldier_demo");
            soldierRoot.SetActive(false);

            GameObject weaponModel = new GameObject("WeaponModel");
            MeshRenderer renderer = weaponModel.AddComponent<MeshRenderer>();

            var binder = new GameObject("Binder").AddComponent<WeaponMuzzleSocketBinder>();
            var so = new SerializedObject(binder);
            so.FindProperty("soldierVisualRoot").objectReferenceValue = soldierRoot.transform;
            so.FindProperty("prototypeWeaponRoot").objectReferenceValue = weaponModel.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                // First apply the hide (as if the soldier had been active), then toggle.
                soldierRoot.SetActive(true);
                binder.RefreshPrototypeWeaponVisibility();
                Assert.IsFalse(renderer.enabled, "Precondition: hidden while the soldier is active.");

                soldierRoot.SetActive(false);
                binder.RefreshPrototypeWeaponVisibility();

                Assert.IsTrue(renderer.enabled,
                    "When the soldier visual is inactive (Carl/prototype fallback), the " +
                    "prototype gun's renderer must be restored.");
            }
            finally
            {
                Object.DestroyImmediate(binder.gameObject);
                Object.DestroyImmediate(weaponModel);
                Object.DestroyImmediate(soldierRoot);
            }
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
            Assert.IsFalse(binder.IsBound, "The binder must report itself unbound.");

            Object.DestroyImmediate(soldierRoot);
            Object.DestroyImmediate(muzzleObject);
        }

        [Test]
        public void TryBind_WithActiveSoldierButNoAvatarFailsGracefully()
        {
            GameObject soldierRoot = new GameObject("ToonSoldier_demo");
            GameObject muzzleObject = new GameObject("MuzzlePoint");
            // No Animator / avatar on the soldier root: the humanoid rig is missing.

            var binder = muzzleObject.AddComponent<WeaponMuzzleSocketBinder>();
            var so = new SerializedObject(binder);
            so.FindProperty("soldierVisualRoot").objectReferenceValue = soldierRoot.transform;
            so.FindProperty("muzzlePoint").objectReferenceValue = muzzleObject.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            bool bound = binder.TryBind();

            Assert.IsFalse(bound, "A missing humanoid avatar must refuse to bind.");
            Assert.IsNull(muzzleObject.transform.parent,
                "The muzzle must keep its authored hierarchy position.");

            Object.DestroyImmediate(soldierRoot);
            Object.DestroyImmediate(muzzleObject);
        }

        // =================================== QA fix #6 - hand-cluster muzzle measurement

        [Test]
        public void TryPickMuzzleFromHandCluster_SelectsTheMuzzleTipNotTheFace()
        {
            GameObject meshObject = new GameObject("MESH_Infantry");
            GameObject handObject = new GameObject("Bip001 R Hand");
            handObject.transform.position = new Vector3(0.3f, 1.0f, 0.2f);

            try
            {
                // Synthetic deformed point cloud reproducing the QA failure geometry:
                // a hand-rigid rifle whose muzzle is the farthest hand-cluster vertex,
                // and a helmet/face vertex that is further FORWARD but weighted to
                // another bone. The old global forward-most heuristic picked the face;
                // the hand-cluster filter must pick the muzzle.
                Vector3[] vertices =
                {
                    new Vector3(0.3f, 1.0f, 0.2f),    // hand center itself
                    new Vector3(0.31f, 1.02f, 0.5f),  // grip, near the hand (hand cluster)
                    new Vector3(0.32f, 1.05f, 0.75f), // muzzle tip (hand cluster, farthest)
                    new Vector3(0.30f, 1.60f, 1.40f), // helmet/face - global forward-most, NOT hand
                };

                BoneWeight handWeight = new BoneWeight
                {
                    boneIndex0 = 1,
                    weight0 = 1f,
                };
                BoneWeight otherWeight = new BoneWeight
                {
                    boneIndex0 = 0,
                    weight0 = 1f,
                };
                BoneWeight[] weights =
                {
                    handWeight,  // hand center
                    handWeight,  // grip
                    handWeight,  // muzzle
                    otherWeight, // helmet/face
                };

                bool found = WeaponMuzzleSocketBinder.TryPickMuzzleFromHandCluster(
                    vertices, weights, 1, meshObject.transform, handObject.transform,
                    out Vector3 handLocalMuzzle);

                Assert.IsTrue(found, "The hand-rigid rifle cluster must yield a muzzle.");

                Vector3 expected = new Vector3(0.02f, 0.05f, 0.55f);
                Assert.LessOrEqual(
                    Vector3.Distance(expected, handLocalMuzzle),
                    1e-4f,
                    "The muzzle must come from the farthest hand-cluster vertex " +
                    $"(expected {expected}, got {handLocalMuzzle}).");

                Vector3 faceHandLocal = new Vector3(0f, 0.6f, 1.2f);
                Assert.Greater(
                    Vector3.Distance(faceHandLocal, handLocalMuzzle),
                    0.3f,
                    "The measured muzzle must NOT be the face/helmet vertex - that was " +
                    "exactly the QA failure the old global forward-most heuristic caused.");
            }
            finally
            {
                Object.DestroyImmediate(meshObject);
                Object.DestroyImmediate(handObject);
            }
        }

        [Test]
        public void TryPickMuzzleFromHandCluster_FailsGracefullyOnInvalidInput()
        {
            GameObject meshObject = new GameObject("Mesh");
            GameObject handObject = new GameObject("Hand");

            BoneWeight[] oneWeight = { new BoneWeight { boneIndex0 = 1, weight0 = 1f } };
            Vector3[] oneVertex = { Vector3.zero };

            try
            {
                Assert.IsFalse(
                    WeaponMuzzleSocketBinder.TryPickMuzzleFromHandCluster(
                        null, oneWeight, 1, meshObject.transform, handObject.transform, out _),
                    "Null vertices must fail gracefully.");
                Assert.IsFalse(
                    WeaponMuzzleSocketBinder.TryPickMuzzleFromHandCluster(
                        oneVertex, null, 1, meshObject.transform, handObject.transform, out _),
                    "Null weights must fail gracefully.");
                Assert.IsFalse(
                    WeaponMuzzleSocketBinder.TryPickMuzzleFromHandCluster(
                        oneVertex, new BoneWeight[0], 1, meshObject.transform,
                        handObject.transform, out _),
                    "A vertex/weight length mismatch must fail gracefully.");
                Assert.IsFalse(
                    WeaponMuzzleSocketBinder.TryPickMuzzleFromHandCluster(
                        oneVertex, oneWeight, -1, meshObject.transform,
                        handObject.transform, out _),
                    "An unresolved hand bone index must fail gracefully.");
            }
            finally
            {
                Object.DestroyImmediate(meshObject);
                Object.DestroyImmediate(handObject);
            }
        }

        [Test]
        public void IsHandRigid_RequiresDominantWeightOnTheHandBone()
        {
            BoneWeight handRigid = new BoneWeight { boneIndex0 = 1, weight0 = 1f };
            BoneWeight mixed = new BoneWeight
            {
                boneIndex0 = 1, weight0 = 0.5f,
                boneIndex1 = 2, weight1 = 0.5f,
            };
            BoneWeight other = new BoneWeight { boneIndex0 = 0, weight0 = 1f };

            Assert.IsTrue(
                WeaponMuzzleSocketBinder.IsHandRigid(handRigid, 1, 0.9f),
                "A vertex fully weighted to the hand bone is hand-rigid.");
            Assert.IsFalse(
                WeaponMuzzleSocketBinder.IsHandRigid(mixed, 1, 0.9f),
                "A vertex split across two bones must not count as hand-rigid rifle geometry.");
            Assert.IsFalse(
                WeaponMuzzleSocketBinder.IsHandRigid(other, 1, 0.9f),
                "A vertex weighted to another bone (helmet/face) must be excluded.");
        }

        [Test]
        public void Unbind_IsSafeAndNeverReparentsTheMuzzle()
        {
            GameObject weaponRoot = new GameObject("Weapon");
            GameObject muzzleObject = new GameObject("MuzzlePoint");
            muzzleObject.transform.SetParent(weaponRoot.transform, false);
            muzzleObject.transform.localPosition = new Vector3(0f, 0.25f, 0.75f);

            var binder = muzzleObject.AddComponent<WeaponMuzzleSocketBinder>();
            var so = new SerializedObject(binder);
            so.FindProperty("muzzlePoint").objectReferenceValue = muzzleObject.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                // Soldier root is null -> binding is refused, exactly the Carl/prototype
                // fallback path. TryBind internally runs the fallback Unbind.
                binder.TryBind();

                Assert.AreEqual(weaponRoot.transform, muzzleObject.transform.parent,
                    "The muzzle's parent must never change - ownership stays with the Weapon.");
                Assert.AreEqual(new Vector3(0f, 0.25f, 0.75f), muzzleObject.transform.localPosition,
                    "The muzzle's authored local pose must stay intact (no restore needed).");

                // Repeated Unbind must be safe and idempotent. Any SetParent-during-
                // deactivation error would be caught by the fixture TearDown LogAssert.
                binder.Unbind();
                binder.Unbind();

                Assert.AreEqual(weaponRoot.transform, muzzleObject.transform.parent,
                    "Repeated Unbind must never re-parent the muzzle.");
                Assert.IsFalse(binder.IsBound, "Unbind must leave the binder unbound.");
            }
            finally
            {
                Object.DestroyImmediate(muzzleObject);
                Object.DestroyImmediate(weaponRoot);
            }
        }

        /// <summary>Unity euler angles are 0..360; normalise to -180..180 for sign assertions.</summary>
        private static float NormalizeEuler(float eulerY)
        {
            return Mathf.Repeat(eulerY + 180f, 360f) - 180f;
        }
    }
}
