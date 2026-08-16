using UnityEngine;

namespace OperationOutbreak.Weapons
{
    /// <summary>
    /// Milestone 1P.5 QA fix #8/#9 - full presentation correction for the Toon Soldier
    /// weapon: one visible weapon, one authoritative muzzle, no teardown errors.
    ///
    /// WHAT CHANGED ARCHITECTURALLY:
    ///   1. The MuzzlePoint is NEVER re-parented anymore. Instead it FOLLOWS the socket:
    ///      a runtime socket ("ToonSoldierMuzzleSocket") is created under the animated
    ///      Right Hand bone, and each frame (before WeaponController's Update, via
    ///      DefaultExecutionOrder) the SAME authoritative MuzzlePoint is placed at the
    ///      socket's world pose. This removes every SetParent call from the deactivation
    ///      path and therefore structurally eliminates the Console error
    ///      "Cannot set the parent of 'MuzzlePoint' while activating or deactivating
    ///      'ToonSoldierMuzzleSocket'" - the muzzle's parent never changes, so it can
    ///      never be orphaned, and the Carl/prototype fallback is simply "stop
    ///      following" (the muzzle's authored local pose under the Weapon was never
    ///      touched, so its world position snaps back automatically).
    ///   2. The obsolete prototype weapon visual (Weapon > WeaponModel, a scaled cube)
    ///      is hidden while the Toon Soldier is active and bound, so the soldier's own
    ///      skinned rifle is the ONLY visible weapon. It is restored for the
    ///      Carl/prototype fallback.
    ///   3. The muzzle socket position/direction are no longer a runtime guessing game.
    ///      FBX forensics proved the rifle is rigidly skinned (weight 1.0) to the
    ///      Bip001 R Hand and derived the muzzle from the actual rifle geometry:
    ///      hand-local offset (0.543, -0.033, 0.077) m and barrel direction
    ///      (0.9885, -0.0595, 0.1392) - exposed as fbxBarrelTipOffset /
    ///      fbxBarrelDirection. The default path (useMeasuredBarrelTip) recomputes the
    ///      SAME quantity in Unity's runtime frames from the hand-rigid cluster, which
    ///      is pose-independent (the rifle is rigid on the hand). The FBX-derived
    ///      constants are the deterministic fallback and the documented evidence.
    ///
    /// CONTRACT: WeaponController and MuzzleFlashFeedback keep using the SAME
    /// MuzzlePoint reference they already own. One authoritative muzzle, no duplicate
    /// projectile authority, no gameplay changes.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)] // Follow runs BEFORE WeaponController.Update, so shots
                                  // spawn from the up-to-date hand position (no 1-frame lag).
    public sealed class WeaponMuzzleSocketBinder : MonoBehaviour
    {
        [Header("Presentation Socket (Toon Soldier)")]
        [Tooltip("The ToonSoldier_demo instance root. Its Animator must use the imported " +
                 "ToonSoldier_demoAvatar humanoid avatar.")]
        [SerializeField] private Transform soldierVisualRoot;

        [Header("Gameplay Anchor (authority, never replaced, never re-parented)")]
        [Tooltip("The existing WeaponController muzzle point. This transform stays under " +
                 "the Weapon at all times; the binder only moves its world pose.")]
        [SerializeField] private Transform muzzlePoint;

        [Header("Obsolete Prototype Weapon Visual")]
        [Tooltip("Weapon > WeaponModel (the old prototype gun). Its renderers are hidden " +
                 "while the Toon Soldier is active and bound, and restored for the " +
                 "Carl/prototype fallback.")]
        [SerializeField] private Transform prototypeWeaponRoot;

        [Header("Barrel Tip Resolution (visual only)")]
        [Tooltip("Recompute the muzzle from the hand-rigid rifle cluster at startup " +
                 "(pose-independent, Unity-frame exact). Turn off to use the FBX-derived " +
                 "constants directly.")]
        [SerializeField] private bool useMeasuredBarrelTip = true;

        [Tooltip("FBX-derived muzzle position in the Right Hand's local frame (cm -> m), " +
                 "measured from the actual rifle geometry: the tube's far end, 54.9 cm from " +
                 "the hand.")]
        [SerializeField] private Vector3 fbxBarrelTipOffset = new Vector3(0.543f, -0.0327f, 0.0765f);

        [Tooltip("FBX-derived barrel outward direction in the Right Hand's local frame, " +
                 "normalized. Used for the socket +Z orientation and as the fallback " +
                 "direction.")]
        [SerializeField] private Vector3 fbxBarrelDirection = new Vector3(0.9885f, -0.0595f, 0.1392f);

        [Tooltip("Optional hand-local rotation correction for the muzzle axis.")]
        [SerializeField] private Vector3 barrelRotationEuler = Vector3.zero;

        [Header("Binding Robustness")]
        [Tooltip("Extra frames the binder may retry if the Animator's humanoid bones are " +
                 "not resolvable on the first attempt (bounded; then it stops trying).")]
        [Min(0)]
        [SerializeField] private int bindRetryFrames = 2;

        /// <summary>Dominant-weight threshold for a vertex to count as hand-rigid rifle geometry.</summary>
        public const float RifleWeightThreshold = 0.9f;

        private Transform _socket;
        private bool _bound;
        private int _retriesLeft;
        private Mesh _bakeMesh;
        private Renderer[] _prototypeRenderers;

        /// <summary>True while the muzzle is following the soldier socket.</summary>
        public bool IsBound => _bound;

        /// <summary>
        /// Pure decision: whether binding should happen. Split out so EditMode tests
        /// can pin the fallback rules (inactive soldier, missing animator/avatar/bone)
        /// without an imported humanoid rig.
        /// </summary>
        public static bool ShouldBind(bool soldierActiveInHierarchy, bool hasAnimator, bool hasHandBone)
        {
            return soldierActiveInHierarchy && hasAnimator && hasHandBone;
        }

        /// <summary>
        /// Pure decision (QA fix #8): the obsolete prototype weapon visual is hidden
        /// exactly when the Toon Soldier presentation is active AND bound - the
        /// soldier's skinned rifle is then the only visible weapon. The Carl/prototype
        /// fallback keeps the old gun visible, as it always was.
        /// </summary>
        public static bool ShouldHidePrototypeWeapon(bool soldierActiveAndBound)
        {
            return soldierActiveAndBound;
        }

        /// <summary>
        /// Pure helper: places the authoritative muzzle at the socket's world pose.
        /// The muzzle's PARENT is never changed - this is what makes the follow
        /// architecture immune to Unity's SetParent-during-deactivation restriction.
        /// </summary>
        public static void WriteFollowPose(Transform muzzle, Transform socket)
        {
            if (muzzle == null || socket == null)
            {
                return;
            }

            muzzle.SetPositionAndRotation(socket.position, socket.rotation);
        }

        /// <summary>
        /// Pure helper (QA fix #6): true when the hand bone dominates the vertex - the
        /// maximum of the four skin weights belongs to <paramref name="boneIndex"/> and
        /// reaches the threshold. Static and side-effect free for EditMode tests.
        /// </summary>
        public static bool IsHandRigid(BoneWeight weights, int boneIndex, float threshold)
        {
            float w0 = weights.weight0, w1 = weights.weight1, w2 = weights.weight2, w3 = weights.weight3;
            float max = Mathf.Max(w0, Mathf.Max(w1, Mathf.Max(w2, w3)));

            if (max < Mathf.Max(0f, threshold))
            {
                return false;
            }

            if (w0 == max) return weights.boneIndex0 == boneIndex;
            if (w1 == max) return weights.boneIndex1 == boneIndex;
            if (w2 == max) return weights.boneIndex2 == boneIndex;
            return weights.boneIndex3 == boneIndex;
        }

        /// <summary>
        /// Pure helper (QA fix #6): picks the hand-rigid rifle vertex farthest from the
        /// hand bone and returns its hand-local position. Because the rifle is rigid on
        /// the hand, the farthest such vertex IS the muzzle in every pose - the same
        /// quantity fbxBarrelTipOffset was derived from statically in the FBX.
        /// </summary>
        public static bool TryPickMuzzleFromHandCluster(
            Vector3[] bakedVertices,
            BoneWeight[] vertexWeights,
            int handBoneIndex,
            Transform meshTransform,
            Transform hand,
            out Vector3 handLocalMuzzle)
        {
            handLocalMuzzle = Vector3.zero;

            if (bakedVertices == null || vertexWeights == null ||
                bakedVertices.Length != vertexWeights.Length ||
                meshTransform == null || hand == null || handBoneIndex < 0)
            {
                return false;
            }

            Vector3 handPosition = hand.position;
            float bestDistanceSqr = -1f;
            Vector3 bestWorld = Vector3.zero;
            bool found = false;

            for (int i = 0; i < bakedVertices.Length; i++)
            {
                if (!IsHandRigid(vertexWeights[i], handBoneIndex, RifleWeightThreshold))
                {
                    continue;
                }

                Vector3 world = meshTransform.TransformPoint(bakedVertices[i]);
                float distanceSqr = (world - handPosition).sqrMagnitude;

                if (distanceSqr > bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestWorld = world;
                    found = true;
                }
            }

            if (!found)
            {
                return false;
            }

            handLocalMuzzle = hand.InverseTransformPoint(bestWorld);
            return true;
        }

        private void Start()
        {
            _retriesLeft = Mathf.Max(0, bindRetryFrames);
            TryBind();
        }

        private void Update()
        {
            // Bounded retries only: covers late Animator/avatar initialization, then
            // this method does nothing for the rest of the scene run.
            if (!_bound && _retriesLeft > 0)
            {
                _retriesLeft--;
                TryBind();
            }

            if (_bound)
            {
                FollowSocketTick();
            }
        }

        private void OnDisable()
        {
            // QA fix #8: Unbind never re-parents the muzzle anymore - it only stops
            // following and destroys the runtime socket - so it is safe to call from
            // OnDisable / scene teardown / Play Mode exit. No SetParent happens on any
            // deactivation path, which structurally removes the
            // "Cannot set the parent ... while activating or deactivating" error.
            Unbind();
        }

        /// <summary>
        /// Places the authoritative muzzle at the socket's current world pose. Runtime
        /// caller: Update (before WeaponController.Update via DefaultExecutionOrder).
        /// Public so EditMode tests can drive a single tick directly.
        /// </summary>
        public void FollowSocketTick()
        {
            if (!_bound)
            {
                return;
            }

            WriteFollowPose(muzzlePoint, _socket);
        }

        /// <summary>
        /// Stops following and removes the runtime socket. Does NOT re-parent the
        /// muzzle (its authored parent/local pose under the Weapon were never touched),
        /// so the muzzle's world position snaps back to the authored Weapon position -
        /// exactly the Carl/prototype fallback behavior. Idempotent.
        /// </summary>
        public void Unbind()
        {
            _bound = false;

            if (_socket != null)
            {
                GameObject socketObject = _socket.gameObject;
                _socket = null;
                Destroy(socketObject);
            }

            ApplyPrototypeWeaponVisibility(false);
        }

        /// <summary>
        /// Resolves the soldier's right hand through the humanoid avatar, places the
        /// socket at the rifle muzzle (measured hand-cluster offset, or the FBX-derived
        /// constants as fallback), and starts the follow. Returns true when bound.
        /// </summary>
        public bool TryBind()
        {
            if (muzzlePoint == null)
            {
                return false;
            }

            if (soldierVisualRoot == null)
            {
                return false;
            }

            bool soldierActive = soldierVisualRoot.gameObject.activeInHierarchy;
            Animator animator = soldierVisualRoot.GetComponent<Animator>();
            if (animator == null)
            {
                animator = soldierVisualRoot.GetComponentInChildren<Animator>();
            }

            bool hasAnimator = animator != null &&
                               animator.avatar != null &&
                               animator.avatar.isValid &&
                               animator.isHuman;

            Transform handBone = hasAnimator
                ? animator.GetBoneTransform(HumanBodyBones.RightHand)
                : null;

            if (!ShouldBind(soldierActive, hasAnimator, handBone != null))
            {
                // Carl/prototype fallback: stop following if we were bound. The muzzle
                // needs no restoring - its authored pose under the Weapon is intact.
                Unbind();
                return false;
            }

            if (_socket == null)
            {
                GameObject socketObject = new GameObject("ToonSoldierMuzzleSocket");
                socketObject.transform.SetParent(handBone, false);
                _socket = socketObject.transform;
            }
            else if (_socket.parent != handBone)
            {
                _socket.SetParent(handBone, false);
            }

            // CS0165 guard (QA fix #7): the out variable must be definitely assigned.
            // Initializing with the FBX-derived fallback is semantically correct for
            // every path; the out call overwrites it whenever measurement runs.
            Vector3 muzzleOffset = fbxBarrelTipOffset;
            bool measured = useMeasuredBarrelTip && TryMeasureMuzzle(handBone, out muzzleOffset);

            Vector3 direction = measured
                ? (muzzleOffset.sqrMagnitude > 0.0001f ? muzzleOffset.normalized : fbxBarrelDirection.normalized)
                : fbxBarrelDirection.normalized;

            _socket.localPosition = muzzleOffset;
            _socket.localRotation = Quaternion.LookRotation(direction) * Quaternion.Euler(barrelRotationEuler);

            _bound = true;
            ApplyPrototypeWeaponVisibility(true);
            return true;
        }

        private void ApplyPrototypeWeaponVisibility(bool soldierActiveAndBound)
        {
            if (prototypeWeaponRoot == null)
            {
                return;
            }

            if (_prototypeRenderers == null)
            {
                _prototypeRenderers = prototypeWeaponRoot.GetComponentsInChildren<Renderer>(true);
            }

            if (_prototypeRenderers == null)
            {
                return;
            }

            bool hide = ShouldHidePrototypeWeapon(soldierActiveAndBound);

            foreach (Renderer renderer in _prototypeRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = !hide;
                }
            }
        }

        /// <summary>
        /// Bakes the soldier's skinned mesh and measures the muzzle from the hand-rigid
        /// rifle cluster. Pose-independent: the rifle is rigid on the hand, so the
        /// farthest hand-dominated vertex is the muzzle in any pose, at any animation
        /// time. Returns false when the mesh or its bone weights are unavailable
        /// (the FBX-derived constants are then used).
        /// </summary>
        private bool TryMeasureMuzzle(Transform hand, out Vector3 handLocalMuzzle)
        {
            handLocalMuzzle = fbxBarrelTipOffset;

            if (hand == null || soldierVisualRoot == null)
            {
                return false;
            }

            SkinnedMeshRenderer renderer = null;

            foreach (SkinnedMeshRenderer candidate in
                     soldierVisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (candidate.sharedMesh != null && candidate.sharedMesh.vertexCount > 0)
                {
                    renderer = candidate;
                    break;
                }
            }

            if (renderer == null || renderer.sharedMesh.boneWeights == null ||
                renderer.sharedMesh.boneWeights.Length != renderer.sharedMesh.vertexCount)
            {
                return false;
            }

            // The hand bone index inside the renderer's bone array.
            int handBoneIndex = -1;
            Transform[] bones = renderer.bones;

            if (bones != null)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == hand)
                    {
                        handBoneIndex = i;
                        break;
                    }
                }
            }

            if (handBoneIndex < 0)
            {
                return false;
            }

            if (_bakeMesh == null)
            {
                _bakeMesh = new Mesh();
            }

            renderer.BakeMesh(_bakeMesh);

            return TryPickMuzzleFromHandCluster(
                _bakeMesh.vertices,
                renderer.sharedMesh.boneWeights,
                handBoneIndex,
                renderer.transform,
                hand,
                out handLocalMuzzle);
        }
    }
}
