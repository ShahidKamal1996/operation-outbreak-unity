using UnityEngine;

namespace OperationOutbreak.Enemies
{
    /// <summary>
    /// Milestone 1Q - FINAL production death upgrade: hybrid animation -> ragdoll.
    ///
    /// The enemy dies in two stages:
    ///   1. ANIMATION LEAD-IN: the existing one-shot "Base Layer.Death" clip plays
    ///      normally for a short configurable window (EnemyAnimationBridge's
    ///      deathRagdollHandoffSeconds, default 0.30 s - the body is already
    ///      starting to fall inside that window).
    ///   2. RAGDOLL: the bridge calls ActivateRagdoll, which disables the Animator
    ///      (animation stops controlling the skeleton), flips the ragdoll bodies to
    ///      non-kinematic and enables the ragdoll colliders. Physics naturally
    ///      completes the fall and establishes ground contact - no corpse-Y
    ///      correction, no hover, no sinking.
    ///
    /// ALIVE STATE: every ragdoll Rigidbody is KINEMATIC and every ragdoll collider
    /// is DISABLED (the gameplay CapsuleCollider on the enemy root is the only live
    /// collider). The Animator and ZombieController behave exactly as before - root
    /// motion stays OFF, locomotion cadence untouched. The alive state is enforced
    /// in Awake as well as authored on the prefab, so a hand-edited ragdoll can
    /// never affect a living enemy.
    ///
    /// RESET / REUSE (pooling): RestoreForReuse puts every bone transform back at
    /// its AUTHORED pose (captured at runtime - prefab-independent and safe for
    /// variants), zeroes linear/angular velocities, restores kinematic bodies,
    /// disables ragdoll colliders, re-enables the Animator and clears the ragdoll
    /// latch. The bridge calls it on OnDisable, so a reused enemy can never spawn
    /// already collapsed.
    ///
    /// MOBILE PERFORMANCE: major humanoid bones only (11 bodies - no fingers/toes),
    /// primitive colliders, symmetric ConfigurableJoints with hard limits, no
    /// interpolation, discrete collision detection, no projection. Corpse-vs-corpse
    /// physics is avoided practically: the only enabled colliders belong to the
    /// corpse's own chain (joint enableCollision is off), live enemies' gameplay
    /// capsules are disabled the moment their owner dies, and the corpse lives only
    /// for the short settle window before the existing deactivation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyRagdoll : MonoBehaviour
    {
        [Header("Ragdoll Bones (written by Tools > Operation Outbreak > Set Up Basic Infected Ragdoll)")]
        [Tooltip("Ragdoll Rigidbodies in PARENT-BEFORE-CHILD order (Hips first). " +
                 "The order matters: the authored-pose restore walks the array and " +
                 "parents must be restored before their children.")]
        [SerializeField] private Rigidbody[] ragdollBodies = new Rigidbody[0];

        [Tooltip("Ragdoll colliders, one per body (same index as ragdollBodies). " +
                 "Disabled while alive; enabled only during the ragdoll stage.")]
        [SerializeField] private Collider[] ragdollColliders = new Collider[0];

        // Authored pose captured at runtime from the actual prefab instance, so the
        // restore is safe for any enemy variant without relying on prefab YAML.
        private Transform[] _boneTransforms;
        private Vector3[] _authoredLocalPositions;
        private Quaternion[] _authoredLocalRotations;
        private bool[] _authoredKinematicStates;
        private bool[] _authoredColliderStates;
        private bool _authoredCaptured;

        /// <summary>True once the setup tool has wired at least one ragdoll body.</summary>
        public bool IsConfigured => ragdollBodies != null && ragdollBodies.Length > 0;

        /// <summary>True only during the ragdoll stage of the death presentation.</summary>
        public bool IsRagdollActive { get; private set; }

        /// <summary>
        /// Pure gate: physics may drive the skeleton only when the ragdoll is BOTH
        /// configured AND activated. A missing configuration must never hand off.
        /// </summary>
        public static bool ShouldApplyRagdollPhysics(bool configured, bool activated)
        {
            return configured && activated;
        }

        /// <summary>
        /// Pure gate: the ALIVE enforcement (kinematic bodies + disabled colliders)
        /// applies whenever the ragdoll is not active - both before the handoff and
        /// after the reuse reset. Static and side-effect free for EditMode tests.
        /// </summary>
        public static bool ShouldEnforceAliveRagdollState(bool ragdollActive)
        {
            return !ragdollActive;
        }

        /// <summary>
        /// Pure gate: a reuse reset counts as complete only when ALL THREE restore
        /// groups have happened - kinematic states restored, ragdoll colliders
        /// disabled and velocities zeroed. Missing any one of them could spawn a
        /// zombie already collapsed or drifting. Static for EditMode tests.
        /// </summary>
        public static bool IsReuseResetComplete(
            bool kinematicRestored, bool collidersDisabled, bool velocitiesZeroed)
        {
            return kinematicRestored && collidersDisabled && velocitiesZeroed;
        }

        private void Awake()
        {
            // Alive-state enforcement: whatever the prefab says, a living enemy must
            // never have active ragdoll physics. Captures the authored pose first.
            CaptureAuthoredPose();
            ApplyAliveRagdollState();
            IsRagdollActive = false;
        }

        /// <summary>
        /// Called by EnemyAnimationBridge exactly once per death, when the animation
        /// lead-in window elapses. Disables the Animator (animation stops
        /// controlling the skeleton), makes the bodies non-kinematic and enables the
        /// ragdoll colliders - physics takes over the corpse.
        /// </summary>
        public void ActivateRagdoll(Animator animator)
        {
            if (!IsConfigured)
            {
                return;
            }

            CaptureAuthoredPose();

            // Handoff order matters: disable the Animator BEFORE freeing the bodies
            // so the last animated pose is exactly the one physics starts from.
            if (animator != null)
            {
                animator.enabled = false;
            }

            IsRagdollActive = true;
            ApplyKinematicStates(false);
            ApplyColliderStates(true);
        }

        /// <summary>
        /// Full reuse reset: authored bone poses restored (parent-first), velocities
        /// zeroed, bodies kinematic again, ragdoll colliders disabled, Animator
        /// re-enabled, ragdoll latch cleared. Called by the bridge on OnDisable.
        /// </summary>
        public void RestoreForReuse(Animator animator)
        {
            if (!IsConfigured)
            {
                return;
            }

            CaptureAuthoredPose();

            // 1. Bodies kinematic first: pose writes below are then purely
            //    transform-level and cannot disturb the physics scene.
            ApplyKinematicStates(true);

            // 2. Zero velocities BEFORE restoring poses (kinematic bodies keep their
            //    stored velocities otherwise, which would matter on the next handoff).
            for (int i = 0; i < ragdollBodies.Length; i++)
            {
                Rigidbody body = ragdollBodies[i];
                if (body == null)
                {
                    continue;
                }

                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            // 3. Restore authored local poses in PARENT-BEFORE-CHILD order (the
            //    setup tool guarantees the array order).
            if (_authoredCaptured)
            {
                for (int i = 0; i < ragdollBodies.Length; i++)
                {
                    Rigidbody body = ragdollBodies[i];
                    if (body == null || i >= _boneTransforms.Length || _boneTransforms[i] == null)
                    {
                        continue;
                    }

                    _boneTransforms[i].localPosition = _authoredLocalPositions[i];
                    _boneTransforms[i].localRotation = _authoredLocalRotations[i];
                }
            }

            // 4. Ragdoll colliders off again (the gameplay CapsuleCollider is
            //    restored separately by the bridge's QA fix #7 lifecycle).
            ApplyColliderStates(false);

            // 5. Animator back in control and the latch cleared.
            if (animator != null)
            {
                animator.enabled = true;
            }

            IsRagdollActive = false;
        }

        private void ApplyAliveRagdollState()
        {
            ApplyKinematicStates(true);
            ApplyColliderStates(false);
        }

        /// <summary>
        /// Captures, once, the authored transform poses and body/collider states of
        /// the actual prefab instance at runtime. This is the source of truth for
        /// the reuse restore - it deliberately does NOT depend on prefab YAML.
        /// </summary>
        private void CaptureAuthoredPose()
        {
            if (!IsConfigured || _authoredCaptured)
            {
                return;
            }

            _boneTransforms = new Transform[ragdollBodies.Length];
            _authoredLocalPositions = new Vector3[ragdollBodies.Length];
            _authoredLocalRotations = new Quaternion[ragdollBodies.Length];
            _authoredKinematicStates = new bool[ragdollBodies.Length];
            _authoredColliderStates = ragdollColliders != null
                ? new bool[ragdollColliders.Length]
                : new bool[0];

            for (int i = 0; i < ragdollBodies.Length; i++)
            {
                Rigidbody body = ragdollBodies[i];
                if (body == null)
                {
                    continue;
                }

                _boneTransforms[i] = body.transform;
                _authoredLocalPositions[i] = body.transform.localPosition;
                _authoredLocalRotations[i] = body.transform.localRotation;
                _authoredKinematicStates[i] = body.isKinematic;
            }

            for (int i = 0; i < _authoredColliderStates.Length; i++)
            {
                _authoredColliderStates[i] = ragdollColliders[i] != null && ragdollColliders[i].enabled;
            }

            _authoredCaptured = true;
        }

        private void ApplyKinematicStates(bool kinematic)
        {
            for (int i = 0; i < ragdollBodies.Length; i++)
            {
                if (ragdollBodies[i] != null)
                {
                    ragdollBodies[i].isKinematic = kinematic;
                }
            }
        }

        private void ApplyColliderStates(bool enabled)
        {
            for (int i = 0; i < ragdollColliders.Length; i++)
            {
                if (ragdollColliders[i] != null)
                {
                    ragdollColliders[i].enabled = enabled;
                }
            }
        }
    }
}
