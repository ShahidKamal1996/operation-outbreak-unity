using UnityEngine;

namespace OperationOutbreak.Weapons
{
    /// <summary>
    /// Milestone 1P.5 QA fix #4 - binds the authoritative MuzzlePoint to the Toon
    /// Soldier's visible rifle barrel tip, PRESENTATION-ONLY.
    ///
    /// WHY THE PREVIOUS FIX WAS WRONG (QA: flash/projectile at head/upper-body):
    /// the hand-local offset (0, 0, 0.6) was a guess. FBX forensics on
    /// ToonSoldier_demo.FBX showed the real barrel tip sits roughly 1.25 m from the
    /// right hand's bind origin in a rotated bone frame, so a naive hand-local offset
    /// cannot land on the barrel. Additionally, if the Animator's humanoid bones were
    /// not resolvable on the first bind attempt, the muzzle simply stayed at its
    /// authored Weapon position - Player y=1 plus Weapon 0.25, i.e. head/upper-body
    /// height, which is exactly what the QA screenshot showed.
    ///
    /// FIX: the barrel tip is now MEASURED, not guessed. On bind, the soldier's
    /// SkinnedMeshRenderer is baked (one-time cost), the vertex furthest forward along
    /// the character root's facing (the visible barrel end - the package's rifle points
    /// forward, FrontAxis +Z) is picked, and its position is converted into the right
    /// hand's local space. A runtime socket ("ToonSoldierMuzzleSocket") is created
    /// under the Right Hand bone and the EXISTING authoritative MuzzlePoint is parented
    /// to it at the measured offset, so the muzzle rides the animated rifle during
    /// idle/run/shoot with zero per-frame work.
    ///
    /// ROBUSTNESS:
    ///   - Binding is attempted in Start plus a small bounded number of retry frames,
    ///     covering late Animator/avatar initialization. No per-frame searches.
    ///   - The authored <see cref="barrelTipOffset"/> remains as the fallback/override
    ///     when measurement is unavailable or <see cref="useMeasuredBarrelTip"/> is off.
    ///   - CARL FALLBACK: the muzzle's original parent/local transform is captured
    ///     before any bind, and Unbind() restores it. Binding is skipped entirely when
    ///     the soldier visual is inactive, so Carl/prototype keeps the muzzle exactly
    ///     where the Weapon hierarchy authors it.
    ///
    /// CONTRACT: WeaponController and MuzzleFlashFeedback keep using the SAME
    /// MuzzlePoint reference they already own. No duplicate muzzle, no duplicate
    /// projectile system, no gameplay authority changes.
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
        [Tooltip("Measure the real rifle barrel tip from the deformed skinned mesh at " +
                 "startup. Turn off to force the authored fallback offset below.")]
        [SerializeField] private bool useMeasuredBarrelTip = true;

        [Tooltip("Fallback/override hand-local barrel-tip offset. Used only when " +
                 "measurement is unavailable or disabled.")]
        [SerializeField] private Vector3 barrelTipOffset = new Vector3(0f, 0f, 0.6f);

        [Tooltip("Optional hand-local rotation correction for the muzzle axis.")]
        [SerializeField] private Vector3 barrelRotationEuler = Vector3.zero;

        [Header("Binding Robustness")]
        [Tooltip("Extra frames the binder may retry if the Animator's humanoid bones are " +
                 "not resolvable on the first attempt (bounded; then it stops trying).")]
        [Min(0)]
        [SerializeField] private int bindRetryFrames = 2;

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
        /// The single binding operation: parents the EXISTING muzzlePoint under the
        /// socket and applies the socket-local barrel offset. Pure enough to unit test
        /// with plain GameObjects - no Animator involved, no new objects created.
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

        /// <summary>
        /// Pure helper (QA fix #4): picks the vertex furthest forward along the
        /// character root's facing - the visible rifle barrel end for this package -
        /// and returns its position in the hand's local space. Static and side-effect
        /// free so EditMode tests can pin the selection with synthetic point clouds.
        /// </summary>
        public static bool TryPickBarrelTipHandLocal(
            Vector3[] vertices,
            Transform meshTransform,
            Transform characterRoot,
            Transform hand,
            out Vector3 handLocalTip)
        {
            handLocalTip = Vector3.zero;

            if (vertices == null || vertices.Length == 0 ||
                meshTransform == null || characterRoot == null || hand == null)
            {
                return false;
            }

            Vector3 rootPosition = characterRoot.position;
            Vector3 rootForward = characterRoot.forward;
            float bestDot = float.NegativeInfinity;
            Vector3 bestWorld = Vector3.zero;
            bool found = false;

            foreach (Vector3 vertex in vertices)
            {
                Vector3 world = meshTransform.TransformPoint(vertex);
                float dot = Vector3.Dot(world - rootPosition, rootForward);

                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestWorld = world;
                    found = true;
                }
            }

            if (!found)
            {
                return false;
            }

            handLocalTip = hand.InverseTransformPoint(bestWorld);
            return true;
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
        /// real barrel tip from the deformed mesh when enabled, and binds the existing
        /// muzzle to a socket at that position. Returns true when bound.
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

            Vector3 offset = barrelTipOffset;

            if (useMeasuredBarrelTip && TryMeasureBarrelTip(handBone, out Vector3 measured))
            {
                offset = measured;
            }

            AttachMuzzleToSocket(muzzlePoint, _socket, offset, barrelRotationEuler);
            _bound = true;
            return true;
        }

        /// <summary>
        /// Bakes the soldier's skinned mesh once and measures the barrel tip: the vertex
        /// furthest forward along the soldier root's facing, expressed in hand-local
        /// space. Falls back to false when no skinned mesh exists.
        /// </summary>
        private bool TryMeasureBarrelTip(Transform hand, out Vector3 handLocalTip)
        {
            handLocalTip = barrelTipOffset;

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

            if (renderer == null)
            {
                return false;
            }

            if (_bakeMesh == null)
            {
                _bakeMesh = new Mesh();
            }

            renderer.BakeMesh(_bakeMesh);
            Vector3[] vertices = _bakeMesh.vertices;

            return TryPickBarrelTipHandLocal(
                vertices, renderer.transform, soldierVisualRoot, hand, out handLocalTip);
        }
    }
}
