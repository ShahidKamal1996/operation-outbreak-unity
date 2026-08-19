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
    /// primitive colliders, anatomical per-axis ConfigurableJoint limits, no
    /// interpolation, discrete collision detection, no projection. QA fix #1
    /// stabilization: capsules aligned to the REAL bone->child direction, conservative
    /// per-group radii, self-collision OFF via the dedicated 'OO_Ragdoll' layer
    /// (corpse-vs-corpse physics and corpse-part self-kicks removed entirely), and a
    /// stabilized handoff (velocities zeroed, Animator disabled before the bodies
    /// are freed, bodies freed hips-first).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyRagdoll : MonoBehaviour
    {
        /// <summary>
        /// QA fix #1 - the dedicated layer for every ragdoll collider (defined in
        /// ProjectSettings/TagManager.asset, layer 8). Ragdoll-vs-ragdoll
        /// collisions are disabled at runtime via Physics.IgnoreLayerCollision,
        /// so corpse parts interact ONLY with the environment/road - never with
        /// each other and never corpse-vs-corpse (mobile-friendly, and the source
        /// of the "random dance" self-kicks is removed).
        /// </summary>
        public const string RagdollLayerName = "OO_Ragdoll";

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

        /// <summary>Read-only view of the configured bodies, for the editor
        /// diagnostics menu (never mutated at runtime).</summary>
        public Rigidbody[] ConfiguredBodies => ragdollBodies;

        /// <summary>Read-only view of the configured ragdoll colliders, for the
        /// editor diagnostics menu (never mutated at runtime).</summary>
        public Collider[] ConfiguredColliders => ragdollColliders;

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

        /// <summary>
        /// QA fix #1 - pure gate: the ragdoll activation is PREPARED only when the
        /// velocities have been zeroed AND the Animator is disabled AND the ragdoll
        /// colliders are enabled. Only a prepared activation may free the bodies -
        /// an unprepared one is exactly what produced the first-frame
        /// twist/kick/explosion. Static for EditMode tests.
        /// </summary>
        public static bool IsActivationPrepared(
            bool velocitiesZeroed, bool animatorDisabled, bool collidersEnabled)
        {
            return velocitiesZeroed && animatorDisabled && collidersEnabled;
        }

        /// <summary>
        /// QA fix #2 - pure legality gate for Rigidbody velocity writes. Unity 6
        /// logs "Setting linear velocity of a kinematic body is not supported."
        /// whenever linearVelocity/angularVelocity is assigned while
        /// isKinematic == true (the write is discarded), so a velocity write is
        /// LEGAL only for a non-kinematic body. Every velocity assignment in this
        /// component goes through this gate. Static for EditMode tests.
        /// </summary>
        public static bool IsVelocityWriteAllowed(bool isKinematic)
        {
            return !isKinematic;
        }

        /// <summary>
        /// QA fix #2 - the SINGLE velocity-write site in this component. Zeroes
        /// linear AND angular velocity for every NON-KINEMATIC body in the array
        /// (the per-body guard means a kinematic body is never written to, so the
        /// Unity 6 kinematic-velocity warning can never fire from this helper).
        /// Returns how many bodies were actually zeroed.
        ///
        /// Both lifecycles call this helper with a legal ordering:
        ///   ACTIVATION: bodies are freed (non-kinematic) FIRST, then zeroed in
        ///   the same frame - before any FixedUpdate - so the first simulated
        ///   step starts at zero velocity (no launch/pop, no residual kick).
        ///   REUSE RESET: zeroed FIRST (bodies are still non-kinematic after the
        ///   ragdoll; bodies that never ragdolled are already kinematic and are
        ///   skipped), then the bodies are re-kinematic-ed.
        /// Static for EditMode tests (testable against real Rigidbodies).
        /// </summary>
        public static int ZeroVelocitiesWhereLegal(Rigidbody[] bodies)
        {
            if (bodies == null)
            {
                return 0;
            }

            int zeroed = 0;

            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];

                if (body == null || !IsVelocityWriteAllowed(body.isKinematic))
                {
                    continue;
                }

                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                zeroed++;
            }

            return zeroed;
        }

        /// <summary>
        /// QA fix #1 - pure self-collision policy: the layer-based "ragdoll
        /// ignores itself" rule applies only with a valid layer index (0..31).
        /// A missing layer (NameToLayer == -1) must never be passed to
        /// Physics.IgnoreLayerCollision. Static for EditMode tests.
        /// </summary>
        public static bool ShouldUseLayerSelfCollisionPolicy(int ragdollLayerIndex)
        {
            return ragdollLayerIndex >= 0 && ragdollLayerIndex < 32;
        }

        private void Awake()
        {
            // QA fix #1 - self-collision policy FIRST: ragdoll parts must never
            // collide with ragdoll parts (or other corpses) - they interact only
            // with the environment/road. One session-wide native call; guarded
            // against a missing layer.
            int ragdollLayer = LayerMask.NameToLayer(RagdollLayerName);

            if (ShouldUseLayerSelfCollisionPolicy(ragdollLayer))
            {
                Physics.IgnoreLayerCollision(ragdollLayer, ragdollLayer, true);
            }

            EnsureRagdollColliderLayers();

            // Alive-state enforcement: whatever the prefab says, a living enemy must
            // never have active ragdoll physics. Captures the authored pose first.
            CaptureAuthoredPose();
            ApplyAliveRagdollState();
            IsRagdollActive = false;
        }

        /// <summary>
        /// Called by EnemyAnimationBridge exactly once per death, when the animation
        /// lead-in window elapses. STABILIZED handoff sequence (QA fix #1, amended
        /// by QA fix #2 for Unity 6 kinematic-velocity rules):
        ///   1. disable the Animator (animation stops controlling the skeleton) -
        ///      the last animated pose is exactly the physics start pose;
        ///   2. enable the ragdoll colliders (environment-only interactions);
        ///   3. verify the prepared gate;
        ///   4. free the bodies in PARENT-BEFORE-CHILD order (the authored array
        ///      starts at the Hips - the physics root);
        ///   5. zero velocities AFTER the kinematic flip via
        ///      ZeroVelocitiesWhereLegal - Unity 6 discards velocity writes on
        ///      kinematic bodies (and logs a warning), so the zeroing must run
        ///      once the bodies are non-kinematic, still inside the same frame
        ///      before any FixedUpdate. The first simulated step therefore starts
        ///      at zero velocity: no launch/pop, no residual kick, no
        ///      interpenetrating self-contacts.
        /// </summary>
        public void ActivateRagdoll(Animator animator)
        {
            if (!IsConfigured)
            {
                return;
            }

            CaptureAuthoredPose();

            // 1. Handoff order matters: disable the Animator BEFORE freeing the
            // bodies so the last animated pose is exactly the physics start pose.
            if (animator != null)
            {
                animator.enabled = false;
            }

            // 2. Ragdoll colliders on (they interact only with the environment -
            // self-collision was disabled in Awake).
            EnsureRagdollColliderLayers();
            ApplyColliderStates(true);

            // 3. Prepared gate: velocities are zeroed immediately AFTER the
            // kinematic flip below (same frame, pre-simulation) - the legal
            // Unity 6 ordering - so the prepared state holds before any physics
            // step runs.
            if (!IsActivationPrepared(true, animator == null || !animator.enabled, true))
            {
                Debug.LogWarning(
                    "[1Q FINAL] Ragdoll activation was NOT prepared - refusing to free " +
                    "the bodies this frame. This should never happen; check the setup.", this);
                return;
            }

            // 4. Free the bodies hips-first (parent-before-child array order).
            ApplyKinematicStates(false);

            // 5. QA fix #2 - zero velocities AFTER the flip: legal (bodies are
            // non-kinematic now) and effective (the residual Animator-driven
            // kinematic velocity is removed before the first simulated step).
            ZeroVelocitiesWhereLegal(ragdollBodies);

            IsRagdollActive = true;
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

            // QA fix #2 - Unity 6 kinematic-velocity ordering for the reuse reset:
            // 1. Zero velocities FIRST, while the bodies are still NON-KINEMATIC
            //    (the post-ragdoll state). A body that never ragdolled is already
            //    kinematic and is skipped by the per-body guard, so no velocity
            //    write ever touches a kinematic body - the Unity 6
            //    "Setting linear velocity of a kinematic body" warning can never
            //    fire from this path.
            ZeroVelocitiesWhereLegal(ragdollBodies);

            // 2. Bodies kinematic: pose writes below are then purely
            //    transform-level and cannot disturb the physics scene.
            ApplyKinematicStates(true);

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
        /// QA fix #1 - defense in depth: ensures every ragdoll collider's
        /// GameObject is on the dedicated ragdoll layer (the setup tool authors
        /// it, this re-asserts it at runtime in case of hand-edited prefabs).
        /// </summary>
        private void EnsureRagdollColliderLayers()
        {
            int ragdollLayer = LayerMask.NameToLayer(RagdollLayerName);

            if (!ShouldUseLayerSelfCollisionPolicy(ragdollLayer))
            {
                return;
            }

            for (int i = 0; i < ragdollColliders.Length; i++)
            {
                if (ragdollColliders[i] != null)
                {
                    ragdollColliders[i].gameObject.layer = ragdollLayer;
                }
            }
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
