using UnityEngine;

namespace OperationOutbreak.Weapons
{
    /// <summary>
    /// Milestone 1P.5 QA fix #6 - binds the authoritative MuzzlePoint to the Toon
    /// Soldier's visible rifle barrel tip, PRESENTATION-ONLY.
    ///
    /// WHY QA FIX #4/#5 WAS STILL WRONG (QA: projectile/muzzle at the face, above the
    /// rifle): the binder measured the GLOBAL forward-most vertex of the baked mesh.
    /// FBX forensics proved two facts that break that heuristic for this package:
    ///   1. The rifle is a tube of 153 vertices rigidly skinned (weight 1.0) to the
    ///      Bip001 R Hand bone; the muzzle is the tube's far end, 53.4 cm from the hand.
    ///   2. In the BIND pose the rifle points SIDEWAYS, so the bind-pose global
    ///      forward-most vertex is the HELMET/FACE (Head cluster) - and the one-shot
    ///      bake ran in Start / early LateUpdate, BEFORE the Animator had posed the
    ///      idle animation, capturing exactly the bind pose. The socket was then stuck
    ///      at the face for the whole run.
    ///
    /// THE FIX - measure the muzzle from the hand cluster, not the global mesh:
    /// because the rifle is RIGID on the R Hand, the muzzle is ALWAYS the vertex
    /// farthest from the hand among vertices whose dominant skin weight belongs to the
    /// hand bone - in the bind pose, in idle, in run, in shoot, at any animation time.
    /// The measurement is therefore pose-independent and cannot be fooled by the
    /// helmet/face regardless of when the bake happens:
    ///   - bake the SkinnedMeshRenderer (any frame),
    ///   - filter vertices by dominant bone weight (sharedMesh.boneWeights) == hand
    ///     bone index with weight >= 0.9,
    ///   - pick the filtered vertex farthest from the hand bone,
    ///   - express it in hand-local space, create the socket there.
    ///
    /// CONTRACT: WeaponController and MuzzleFlashFeedback keep using the SAME
    /// MuzzlePoint reference they already own. One authoritative muzzle, no duplicate
    /// projectile authority. The authored barrelTipOffset remains only as a
    /// last-resort fallback when the mesh/weights are unavailable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponMuzzleSocketBinder : MonoBehaviour
    {
        [Header("Presentation Socket (Toon Soldier)")]
        [Tooltip("The ToonSoldier_demo instance root. Its Animator must use the imported " +
                 "ToonSoldier_demoAvatar humanoid avatar.")]
        [SerializeField] private Transform soldierVisualRoot;

        [Header("Gameplay Anchor (authority, never replaced)")]
        [Tooltip("The existing WeaponController muzzle point. The SAME transform is " +
                 "re-parented; no new muzzle object is created.")]
        [SerializeField] private Transform muzzlePoint;

        [Header("Barrel Tip Resolution (visual only)")]
        [Tooltip("Measure the real rifle muzzle from the hand-rigid rifle cluster at " +
                 "startup (pose-independent). Turn off to force the authored fallback offset.")]
        [SerializeField] private bool useMeasuredBarrelTip = true;

        [Tooltip("Last-resort hand-local barrel-tip offset. Used only when the mesh or " +
                 "its bone weights are unavailable.")]
        [SerializeField] private Vector3 barrelTipOffset = new Vector3(0f, 0f, 0.6f);

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
        private bool _originalCaptured;
        private Transform _originalParent;
        private Vector3 _originalLocalPosition;
        private Quaternion _originalLocalRotation;
        private Vector3 _originalLocalScale;

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
        /// the hand, the farthest such vertex IS the muzzle in every pose - this is what
        /// makes the measurement immune to the helmet/face that broke the previous
        /// global forward-most heuristic.
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

        /// <summary>
        /// The single binding operation: parents the EXISTING muzzlePoint under the
        /// socket and applies the socket-local offset. Pure enough to unit test with
        /// plain GameObjects - no Animator involved, no new objects created.
        /// </summary>
        public static void AttachMuzzleToSocket(
            Transform muzzlePoint, Transform socket, Vector3 localOffset, Vector3 localRotationEuler)
        {
            if (muzzlePoint == null || socket == null)
            {
                return;
            }

            muzzlePoint.SetParent(socket, false);
            muzzlePoint.localPosition = localOffset;
            muzzlePoint.localRotation = Quaternion.Euler(localRotationEuler);
        }

        private void Start()
        {
            _retriesLeft = Mathf.Max(0, bindRetryFrames);
            TryBind();
        }

        private void LateUpdate()
        {
            // Bounded retries only: covers late Animator/avatar initialization, then
            // this method does nothing for the rest of the scene run.
            if (!_bound && _retriesLeft > 0)
            {
                _retriesLeft--;
                TryBind();
            }
        }

        private void OnDisable()
        {
            Unbind();
        }

        /// <summary>
        /// Records the muzzle's current parent and local transform so it can be restored
        /// later (Carl/prototype fallback). Called once, lazily, before the first bind.
        /// </summary>
        public void CaptureOriginalMuzzleState()
        {
            _originalCaptured = true;
            _originalParent = muzzlePoint != null ? muzzlePoint.parent : null;

            if (muzzlePoint != null)
            {
                _originalLocalPosition = muzzlePoint.localPosition;
                _originalLocalRotation = muzzlePoint.localRotation;
                _originalLocalScale = muzzlePoint.localScale;
            }
        }

        /// <summary>
        /// Restores the muzzle to its original weapon-hierarchy ownership and removes
        /// the runtime socket. Safe to call at any time; afterwards the scene behaves
        /// exactly as it did before this component ever bound anything. Restoring an
        /// already-restored muzzle is a harmless no-op.
        /// </summary>
        public void Unbind()
        {
            if (muzzlePoint != null && _originalCaptured && _originalParent != null)
            {
                muzzlePoint.SetParent(_originalParent, false);
                muzzlePoint.localPosition = _originalLocalPosition;
                muzzlePoint.localRotation = _originalLocalRotation;
                muzzlePoint.localScale = _originalLocalScale;
            }

            _bound = false;

            if (_socket != null)
            {
                GameObject socketObject = _socket.gameObject;
                _socket = null;
                Destroy(socketObject);
            }
        }

        /// <summary>
        /// Resolves the soldier's right hand through the humanoid avatar, measures the
        /// muzzle from the hand-rigid rifle cluster (pose-independent), and binds the
        /// existing muzzle to a socket at that position. Returns true when bound.
        /// </summary>
        public bool TryBind()
        {
            if (muzzlePoint == null)
            {
                return false;
            }

            if (!_originalCaptured)
            {
                CaptureOriginalMuzzleState();
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
                // Carl/prototype fallback: if a previous bind left the muzzle under the
                // soldier, restore it to its authored weapon-hierarchy position.
                if (_bound)
                {
                    Unbind();
                }

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

            bool measured = useMeasuredBarrelTip && TryMeasureMuzzle(handBone, out Vector3 measuredOffset);

            if (measured)
            {
                // Orient the socket so its +Z runs along the hand -> muzzle direction,
                // keeping the muzzle flash's authored forward offset on the barrel line.
                Vector3 safeDirection = measuredOffset.sqrMagnitude > 0.0001f
                    ? measuredOffset.normalized
                    : Vector3.forward;
                _socket.localRotation = Quaternion.LookRotation(safeDirection);

                // The muzzle lands exactly on the measured barrel tip; the authored
                // rotation correction remains available as a final presentation tweak.
                AttachMuzzleToSocket(muzzlePoint, _socket, Vector3.zero, barrelRotationEuler);
            }
            else
            {
                // Last-resort fallback: authored offset, identity socket orientation.
                _socket.localRotation = Quaternion.identity;
                AttachMuzzleToSocket(muzzlePoint, _socket, barrelTipOffset, barrelRotationEuler);
            }

            _bound = true;
            return true;
        }

        /// <summary>
        /// Bakes the soldier's skinned mesh and measures the muzzle from the hand-rigid
        /// rifle cluster. Pose-independent: the rifle is rigid on the hand, so the
        /// farthest hand-dominated vertex is the muzzle in any pose, at any animation
        /// time. Returns false when the mesh or its bone weights are unavailable.
        /// </summary>
        private bool TryMeasureMuzzle(Transform hand, out Vector3 handLocalMuzzle)
        {
            handLocalMuzzle = barrelTipOffset;

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
