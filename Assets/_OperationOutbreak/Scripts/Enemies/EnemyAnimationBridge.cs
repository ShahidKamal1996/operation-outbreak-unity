using UnityEngine;

namespace OperationOutbreak.Enemies
{
    /// <summary>
    /// Milestone 1Q - one-way bridge between the authoritative enemy gameplay
    /// (ZombieController) and the enemy's production Animator, mirroring the 1O.5/1P.5
    /// player bridge contract.
    ///
    /// DIRECTION OF CONTROL IS STRICTLY ONE-WAY: gameplay -> animation. The bridge
    /// reads one already-computed property (ZombieController.CurrentPlanarSpeed) and
    /// observes two events that gameplay raises for its own reasons
    /// (ZombieController.DamagedPlayer, ZombieController.Died). It never moves the
    /// enemy, never applies damage, never changes health, attack timing, target
    /// selection or separation, and never feeds anything back into gameplay.
    /// Deleting this component leaves the enemy fully playable on the prototype
    /// visual - which is exactly the fallback behavior when the production visual has
    /// not been set up.
    ///
    /// ROOT MOTION: the Animator must have Apply Root Motion OFF. ZombieController is
    /// the single movement authority; animation poses the mesh only. This is enforced
    /// in Awake, same as the player bridge.
    ///
    /// DEATH: Died latches the bridge - the Dead bool is set, a pending Attack trigger
    /// is cleared, and locomotion parameters are frozen so the death clip plays once
    /// and the enemy can never animate out of it.
    ///
    /// DEATH PRESENTATION (1Q FINAL - hybrid animation -> ragdoll): with a ragdoll
    /// configured, the death clip is the animation LEAD-IN (deathRagdollHandoffSeconds,
    /// default 0.30 s); the bridge then hands the skeleton to physics exactly once
    /// (EnemyRagdoll.ActivateRagdoll - Animator disabled, bodies non-kinematic,
    /// ragdoll colliders enabled). Physics completes the fall; the presentation
    /// completes after the settle window. While the ragdoll is active the bridge
    /// writes NOTHING (no Animator parameters, no corpse-Y correction). Without a
    /// ragdoll (prototype fallback) the animation-only path and its time-driven
    /// grounding remain exactly as before.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAnimationBridge : MonoBehaviour
    {
        [Header("Gameplay Source (read-only observer)")]
        [Tooltip("Enemy authority. Only CurrentPlanarSpeed / DamagedPlayer / Died are observed.")]
        [SerializeField] private ZombieController zombie;

        [Header("Animation Target")]
        [Tooltip("Animator on the production enemy visual. Apply Root Motion must be OFF.")]
        [SerializeField] private Animator animator;

        // Parameter names shared with the controller-authoring tool and the EditMode
        // validation, so gameplay and presentation can never drift apart.
        public const string SpeedParameter = "Speed";
        public const string AttackParameter = "Attack";
        public const string DeadParameter = "Dead";
        public const string LocomotionSpeedMultiplierParameter = "LocomotionSpeedMultiplier";

        /// <summary>
        /// QA fix #2 - the controller's Death STATE name, shared with the
        /// controller-authoring tool so the bridge's direct death entry always
        /// targets the real state.
        /// </summary>
        public const string DeathStateName = "Death";

        /// <summary>
        /// QA fix #4 - the base layer's state machine name, shared with the controller
        /// tool (the tool names the base machine exactly this), so the full state path
        /// hash below always resolves.
        /// </summary>
        public const string BaseLayerName = "Base Layer";

        /// <summary>
        /// QA fix #4 - Animator.Play targets states by their FULL PATH hash
        /// ("Base Layer.Death"), not by the short state name. Unity's documented
        /// Play contract is the full path; the short-name hash used previously could
        /// fail to resolve the state in the generated controller, leaving the enemy
        /// in its previous state until the parameter-driven transition kicked in -
        /// which is why the death animation never visibly played.
        /// </summary>
        public const string DeathStateFullPath = BaseLayerName + "." + DeathStateName;

        /// <summary>QA fix #4 - the layer index Animator.Play switches into for death.</summary>
        public const int DeathPlayLayer = 0;

        private static readonly int SpeedHash = Animator.StringToHash(SpeedParameter);
        private static readonly int AttackHash = Animator.StringToHash(AttackParameter);
        private static readonly int DeadHash = Animator.StringToHash(DeadParameter);
        private static readonly int LocomotionSpeedMultiplierHash = Animator.StringToHash(LocomotionSpeedMultiplierParameter);
        private static readonly int DeathStateFullPathHash = Animator.StringToHash(DeathStateFullPath);

        [Header("Locomotion Cadence Sync (Milestone 1Q Bug 4)")]
        [Tooltip("Gameplay speed (units/second) at which the walk clip's foot cadence " +
                 "matches world translation. The setup tool derives this from the walk " +
                 "clip's own average speed; tune here if QA still sees foot sliding.")]
        [Min(0.1f)]
        [SerializeField] private float walkReferenceSpeed = 1.3f;

        [Tooltip("Clamp range for the locomotion playback multiplier, so extreme or " +
                 "misconfigured speeds can never produce absurd animation playback.")]
        [Min(0.1f)]
        [SerializeField] private float minimumLocomotionMultiplier = 0.5f;
        [Min(0.1f)]
        [SerializeField] private float maximumLocomotionMultiplier = 2.5f;

        /// <summary>True once the death animation has been requested; latches all other animation off.</summary>
        public bool IsDeathLatched => _deathLatched;

        private bool _deathLatched;
        private bool _deathPresentationStarted;

        // ------------------------------------------------------------------ death grounding

        /// <summary>
        /// QA fix #10 - DOCUMENTED FALLBACK for the final grounded death visual Y,
        /// used only when the editor setup tool cannot measure the near-final death
        /// pose (the setup tool always overwrites this with the measured value).
        /// Assumes the lying corpse's lowest point rests ~0.5 units above the
        /// ProductionVisual pivot (roughly the capsule radius 0.45 + margin), so
        /// with the enemy root at y=1 and the lane at y=0:
        ///   standing -1.005 -> final -(1 + 0.5) = -1.5.
        /// </summary>
        public const float FallbackDeathGroundedVisualY = -1.5f;

        [Header("Death Grounding (Milestone 1Q QA fix #10 - death-time-driven)")]
        [Tooltip("STABLE final local Y of the ProductionVisual for the grounded death " +
                 "pose, measured ONCE by the setup tool from the near-final death pose " +
                 "(deterministic for these fixed production assets). The runtime never " +
                 "resamples or chases a moving target - it blends to this value only, " +
                 "as a function of the Death clip's normalized time.")]
        [SerializeField] private float deathGroundedVisualY = FallbackDeathGroundedVisualY;

        [Tooltip("Death clip normalized time at which the blend from the standing Y to " +
                 "the final grounded Y BEGINS. Before this point the standing Y is " +
                 "retained unchanged (the fall is still mostly upright).")]
        [Range(0.01f, 0.99f)]
        [SerializeField] private float deathGroundingStartNormalizedTime = 0.25f;

        [Tooltip("Death clip normalized time at which the blend REACHES the final " +
                 "grounded Y. The final lying pose must already rest on the road by " +
                 "this point - well before the clip-finish gate at 0.999, so no " +
                 "post-animation correction can ever be visible.")]
        [Range(0.01f, 0.99f)]
        [SerializeField] private float deathGroundingEndNormalizedTime = 0.85f;

        [Tooltip("The grounded death pose counts as REACHED when the visual's Y is " +
                 "within this distance of the margin-adjusted final grounded Y. Used " +
                 "by the death presentation completion gate.")]
        [Min(0f)]
        [SerializeField] private float deathGroundingCompletionTolerance = 0.015f;

        /// <summary>
        /// QA fix #11 - DEFAULT contact margin subtracted from the serialized final
        /// grounded death Y: the corpse prefers a very slight contact/intersection
        /// with the road over visible hovering. Small by design (2 cm on a 2 m
        /// zombie); the setup tool always writes this value, and it can be tuned
        /// per-prefab in the Inspector without re-running the tool.
        /// </summary>
        public const float DefaultDeathGroundingContactMargin = 0.02f;

        /// <summary>
        /// QA fix #11 - safety ceiling for the contact margin. Anything larger would
        /// sink the body visibly ("do not sink the body deeply"); the clamp is
        /// applied at runtime too, so a hand-edited value can never over-sink.
        /// </summary>
        public const float MaximumDeathGroundingContactMargin = 0.05f;

        [Tooltip("QA fix #11 - small DOWNWARD contact margin subtracted from the " +
                 "serialized final grounded Y, so the corpse visually contacts the " +
                 "road instead of hovering a few centimetres above it. Clamped to " +
                 "[0, 0.05]; the setup tool writes the default 0.02 every run.")]
        [SerializeField] private float deathGroundingContactMargin = DefaultDeathGroundingContactMargin;

        // ------------------------------------------------ hybrid animation -> ragdoll death

        [Header("Hybrid Ragdoll Death (1Q Final Production Death Upgrade)")]
        [Tooltip("Ragdoll component authored on this enemy by the setup tool. When " +
                 "configured, the death presentation hands off to physics after the " +
                 "animation lead-in; when missing, the legacy animation-only path is " +
                 "used (prototype fallback stays intact).")]
        [SerializeField] private EnemyRagdoll ragdoll;

        [Tooltip("Animation lead-in: how long the one-shot Death clip plays before " +
                 "the skeleton is handed to ragdoll physics. Chosen against the " +
                 "actual clip (0.25-0.40 s band): by this point the body is already " +
                 "starting to fall, so physics completes the fall naturally.")]
        [Min(0f)]
        [SerializeField] private float deathRagdollHandoffSeconds = 0.3f;

        [Tooltip("How long the corpse stays in ragdoll physics before the death " +
                 "presentation counts as complete (the existing deactivation wait, " +
                 "production hold and safety timeout still apply). Long enough for " +
                 "the fall to settle on the road; the corpse never stays forever.")]
        [Min(0.1f)]
        [SerializeField] private float deathRagdollSettleSeconds = 0.6f;

        private float _ragdollHandoffElapsed;
        private float _ragdollActivationTime = -1f;
        private bool _ragdollHandoffDone;

        private Transform _productionVisual;
        private float _standingProductionVisualY;
        private bool _deathClipFinished;

        // QA fix #7 - gameplay collider lifecycle: the prototype prefab's CapsuleCollider
        // lives on the enemy ROOT. Its original enabled state is captured once and
        // restored on reuse, so a dead corpse stops colliding but the next enemy works.
        private Collider[] _gameplayColliders;
        private bool[] _gameplayColliderEnabledStates;
        private bool _gameplayCollidersCaptured;

        /// <summary>
        /// QA fix #5 - pure one-shot gate for the death presentation. Animator.Play
        /// with normalizedTime 0 MUST execute exactly once per death: any repeated
        /// call restarts the death clip at its first frames, which QA observed as
        /// jerking/looping. Static and side-effect free for EditMode tests.
        /// </summary>
        public static bool ShouldStartDeathPresentation(bool deathLatched, bool presentationStarted)
        {
            return deathLatched && !presentationStarted;
        }

        /// <summary>
        /// Pure helper (Bug 4): converts the code-driven planar speed into the Walk
        /// state's playback multiplier so foot cadence matches world translation.
        /// At zero speed the multiplier clamps to the minimum (the Walk state is not
        /// active anyway); the reference is guarded so a misconfigured value can never
        /// divide by zero. Static and side-effect free for EditMode tests.
        /// </summary>
        public static float ComputeLocomotionSpeedMultiplier(
            float planarSpeed, float walkReferenceSpeed, float minimum, float maximum)
        {
            float safeMinimum = Mathf.Max(0.1f, minimum);
            float safeMaximum = Mathf.Max(safeMinimum, maximum);

            if (planarSpeed <= 0f)
            {
                return safeMinimum;
            }

            float reference = Mathf.Max(0.1f, walkReferenceSpeed);
            return Mathf.Clamp(planarSpeed / reference, safeMinimum, safeMaximum);
        }

        /// <summary>
        /// QA fix #6 / 1Q FINAL - pure gate: the animation-path death grounding
        /// applies only after the death latch AND while the ragdoll is NOT active.
        /// Once physics owns the corpse, the ProductionVisual Y correction must
        /// never run - the two systems must not fight. While the enemy lives, the
        /// standing ProductionVisual offset is never touched either.
        /// </summary>
        public static bool ShouldApplyDeathGrounding(bool deathLatched, bool ragdollActive)
        {
            return deathLatched && !ragdollActive;
        }

        /// <summary>
        /// 1Q FINAL / QA fix #1 - pure ONE-SHOT handoff gate: the skeleton hands
        /// off to ragdoll physics exactly once - only when the ragdoll is NOT
        /// already active, the handoff has NOT already been done, and the
        /// animation lead-in has elapsed. Before the lead-in the Death clip (and
        /// ONLY the Death clip) drives the skeleton. Static and side-effect free
        /// for EditMode tests.
        /// </summary>
        public static bool ShouldTriggerRagdollHandoff(
            bool ragdollActive, bool handoffDone, float leadInElapsed, float handoffSeconds)
        {
            return !ragdollActive && !handoffDone && leadInElapsed >= Mathf.Max(0f, handoffSeconds);
        }

        /// <summary>
        /// 1Q FINAL - pure completion gate for the ragdoll path: with the ragdoll
        /// active, the presentation completes when the corpse has had its full
        /// physics settle window (there is no clip-finish or grounding tolerance to
        /// wait for - the ground contact is produced by physics itself). The
        /// animation-only path keeps the QA fix #9 gate
        /// (ShouldCompleteDeathPresentation below). Static for EditMode tests.
        /// </summary>
        public static bool ShouldCompleteRagdollPresentation(bool ragdollActive, bool ragdollSettled)
        {
            return ragdollActive && ragdollSettled;
        }

        /// <summary>
        /// QA fix #10 - pure gate: the time-driven grounding blend starts only once
        /// the Death clip has advanced to (or past) the configured start point.
        /// Early in the clip the standing Y is retained unchanged.
        /// </summary>
        public static bool ShouldStartDeathGroundingBlend(
            float deathNormalizedTime, float startNormalizedTime)
        {
            return deathNormalizedTime >= Mathf.Max(0f, startNormalizedTime);
        }

        /// <summary>
        /// QA fix #10 - pure remap: converts the Death clip's normalized time into
        /// the grounding blend progress over [start, end]. Smoothstep eases the
        /// lowering in and out so it visually MERGES with the fall animation
        /// instead of reading as a separate correction. Clamped to [0, 1]; a
        /// degenerate (zero/negative-width) window is treated as NEVER OPENING
        /// (progress stays 0) - the corpse-Y correction stays off entirely. The
        /// 1Q FINAL ragdoll setup tool relies on exactly this to bypass the
        /// animation grounding (it zeroes the window). Static and side-effect
        /// free for EditMode tests.
        /// </summary>
        public static float ComputeDeathGroundingProgress(
            float deathNormalizedTime, float startNormalizedTime, float endNormalizedTime)
        {
            float window = endNormalizedTime - startNormalizedTime;

            if (window <= 0f)
            {
                // A degenerate window must never slam the visual down - it reads
                // as "grounding disabled" (the ragdoll bypass contract).
                return 0f;
            }

            float linear = Mathf.Clamp01((deathNormalizedTime - startNormalizedTime) / window);

            // Smoothstep: 3t^2 - 2t^3 - eases in at the window start and eases out
            // at the window end so the lowering reads as part of the fall.
            return linear * linear * (3f - 2f * linear);
        }

        /// <summary>
        /// QA fix #10 - pure blend: the ProductionVisual's grounded Y as a function
        /// of the grounding progress between the standing Y and the STABLE final
        /// grounded death Y. At progress 0 the standing Y is returned exactly; at
        /// progress 1 the final grounded Y is returned exactly; between them it is
        /// a plain lerp of the (already smoothstepped) progress.
        /// </summary>
        public static float ComputeDeathGroundedVisualY(
            float standingVisualY, float finalGroundedVisualY, float groundingProgress)
        {
            return Mathf.Lerp(standingVisualY, finalGroundedVisualY, Mathf.Clamp01(groundingProgress));
        }

        /// <summary>
        /// QA fix #10 - pure monotonic guard: the visual's Y may only ever move
        /// DOWNWARD (or stay put). Replaces the QA fix #8 target clamp - there is
        /// no target to chase anymore, but the per-frame write still enforces the
        /// no-upward-motion invariant (misconfigured final Y, animator glitches or
        /// a repeated Died can never lift the corpse).
        /// </summary>
        public static float ClampDeathGroundingDownwardOnly(float currentVisualY, float nextVisualY)
        {
            return Mathf.Min(currentVisualY, nextVisualY);
        }

        /// <summary>
        /// QA fix #11 - pure clamp: the contact margin is DOWNWARD-ONLY and SMALL.
        /// Negative values are discarded (the margin may never raise the corpse)
        /// and anything above the safety ceiling clamps to it, so a hand-edited
        /// value can never sink the body deeply. Static and side-effect free for
        /// EditMode tests.
        /// </summary>
        public static float ClampDeathGroundingContactMargin(float contactMargin)
        {
            return Mathf.Clamp(contactMargin, 0f, MaximumDeathGroundingContactMargin);
        }

        /// <summary>
        /// QA fix #11 - pure application: the effective final grounded Y is the
        /// serialized pose-measured Y minus the (clamped, non-negative) contact
        /// margin. The result is therefore always AT OR BELOW the measured Y -
        /// never above it - which is the whole point: prefer a very slight
        /// contact with the road over visible hovering.
        /// </summary>
        public static float ApplyDeathGroundingContactMargin(
            float groundedVisualY, float contactMargin)
        {
            return groundedVisualY - ClampDeathGroundingContactMargin(contactMargin);
        }

        /// <summary>
        /// QA fix #10 - pure gate: the grounding blend has fully completed once the
        /// progress reaches 1, i.e. the Death clip advanced to (or past) the
        /// configured end point. Because the end point (0.85) sits well before the
        /// clip-finish gate (0.999), the final grounded Y is always reached DURING
        /// the death animation - never after it.
        /// </summary>
        public static bool IsDeathGroundingBlendComplete(float groundingProgress)
        {
            return groundingProgress >= 1f;
        }

        /// <summary>
        /// QA fix #7 - pure computation, WORLD-SPACE DELTA form - now used ONLY by
        /// the editor setup tool to derive the STABLE serialized
        /// deathGroundedVisualY once, from the near-final death pose. Given the
        /// corpse's lowest visible vertex WORLD Y (measured while the visual is at
        /// its current offset) and the lane surface WORLD Y, returns the
        /// ProductionVisual local Y that places that vertex exactly on the
        /// surface:
        ///   targetLocalY = currentVisualLocalY + (groundWorldY - lowestCorpseWorldY)
        /// This is valid because the visual's parent chain (the enemy gameplay root)
        /// is identity-rotated and identity-scaled: a world-space Y delta equals a
        /// local-Y delta. The runtime NEVER resamples this - the result is baked
        /// onto the prefab at setup time (QA fix #10).
        /// </summary>
        public static float ComputeDeathGroundedTargetLocalY(
            float currentVisualLocalY, float lowestCorpseWorldY, float groundWorldY)
        {
            return currentVisualLocalY + (groundWorldY - lowestCorpseWorldY);
        }

        /// <summary>
        /// QA fix #9 - pure tolerance check: the grounded death pose counts as
        /// reached when the visual's Y is within the configured tolerance of the
        /// stable final grounded Y (abs distance).
        /// </summary>
        public static bool IsDeathGroundingComplete(float currentVisualY, float targetY, float tolerance)
        {
            return Mathf.Abs(targetY - currentVisualY) <= Mathf.Max(0f, tolerance);
        }

        /// <summary>
        /// QA fix #9 - pure completion gate: the death presentation is complete only
        /// when BOTH the death animation has finished AND the final grounded death Y
        /// has been reached within tolerance. With the QA fix #10 time-driven blend,
        /// the Y is reached at normalized ~0.85 - well BEFORE the clip-finish gate
        /// at 0.999 - so the two conditions are both satisfied the moment the clip
        /// ends and no post-animation wait is ever visible.
        /// </summary>
        public static bool ShouldCompleteDeathPresentation(bool animationFinished, bool groundingComplete)
        {
            return animationFinished && groundingComplete;
        }

        /// <summary>
        /// QA fix #10 - live completion state read by ZombieController's death
        /// feedback: the death clip has finished AND the production visual's Y is
        /// within tolerance of the STABLE serialized final grounded Y. With no
        /// production visual the presentation is trivially complete once the clip
        /// finished.
        /// </summary>
        public bool IsDeathPresentationComplete
        {
            get
            {
                if (!_deathLatched)
                {
                    return false;
                }

                // 1Q FINAL - hybrid ragdoll path: once physics owns the corpse, the
                // presentation completes after the settle window (ground contact is
                // produced by physics itself - no clip/grounding wait applies).
                if (ragdoll != null && ragdoll.IsRagdollActive)
                {
                    float settleElapsed = Time.time - _ragdollActivationTime;
                    return ShouldCompleteRagdollPresentation(
                        true, settleElapsed >= Mathf.Max(0f, deathRagdollSettleSeconds));
                }

                // Animation-only path (prototype fallback or ragdoll not configured):
                // the QA fix #9 gate - clip finished AND grounding settled.
                if (!_deathClipFinished)
                {
                    return false;
                }

                if (_productionVisual == null)
                {
                    return true;
                }

                // QA fix #11 - completion is judged against the MARGIN-ADJUSTED final
                // grounded Y (the same value the blend converges to), so the wait ends
                // exactly when the corpse rests at its calibrated contact pose.
                float finalGroundedY = ApplyDeathGroundingContactMargin(
                    deathGroundedVisualY, deathGroundingContactMargin);

                return IsDeathGroundingComplete(
                    _productionVisual.localPosition.y, finalGroundedY, deathGroundingCompletionTolerance);
            }
        }

        private void Awake()
        {
            if (zombie == null)
            {
                zombie = GetComponent<ZombieController>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (ragdoll == null)
            {
                ragdoll = GetComponent<EnemyRagdoll>();
            }

            if (animator != null)
            {
                // Enforced in code as well as in the prefab: animation must never move the enemy.
                animator.applyRootMotion = false;
            }

            // QA fix #6 - the production visual's standing local Y is captured once,
            // so the death-only grounding can blend from it and restore to it.
            _productionVisual = transform.Find("ProductionVisual");
            _standingProductionVisualY = _productionVisual != null
                ? _productionVisual.localPosition.y
                : 0f;

            // QA fix #7 - capture the ROOT-level gameplay colliders (the prototype
            // CapsuleCollider lives on the enemy root) and their authored enabled
            // states so the death lifecycle can disable them and reuse can restore
            // them. Children of the visual subtree are deliberately ignored.
            _gameplayColliders = GetComponents<Collider>();
            _gameplayColliderEnabledStates = CaptureColliderEnabledStates(_gameplayColliders);
            _gameplayCollidersCaptured = _gameplayColliders != null && _gameplayColliders.Length > 0;
        }

        private void OnEnable()
        {
            if (zombie != null)
            {
                zombie.DamagedPlayer += HandleAttack;
                zombie.Died += HandleDied;
            }

            // Fresh spawn (or scene reload) state: presentation and grounding flags
            // reset, the production visual returns to its standing offset, and any
            // colliders a previous death disabled are restored to their authored
            // enabled states (QA fix #7 - reused enemies must collide again).
            _deathLatched = false;
            _deathPresentationStarted = false;
            _deathClipFinished = false;
            _ragdollHandoffElapsed = 0f;
            _ragdollActivationTime = -1f;
            _ragdollHandoffDone = false;
            RestoreStandingProductionVisualY();
            RestoreGameplayColliders();
        }

        private void OnDisable()
        {
            if (zombie != null)
            {
                zombie.DamagedPlayer -= HandleAttack;
                zombie.Died -= HandleDied;
            }

            // QA fix #6 - leave the visual at its standing offset when this component
            // (or the enemy) is disabled, so the prefab state is never polluted by a
            // death grounding correction.
            RestoreStandingProductionVisualY();

            // 1Q FINAL - reuse reset: the ragdoll must never survive into the next
            // spawn (pooled enemies would otherwise come back collapsed). Restores
            // bone poses, zeroes velocities, re-kinematics bodies, disables ragdoll
            // colliders and re-enables the Animator.
            if (ragdoll != null)
            {
                ragdoll.RestoreForReuse(animator);
            }
        }

        private void Update()
        {
            if (animator == null)
            {
                return;
            }

            bool ragdollActive = ragdoll != null && ragdoll.IsRagdollActive;

            // 1Q FINAL - once physics owns the corpse, the bridge writes NOTHING:
            // no Animator parameters, no ProductionVisual Y correction.
            if (ragdollActive)
            {
                return;
            }

            // QA fix #10 / 1Q FINAL - after the death latch (and while the ragdoll
            // is still inactive), ONLY the death presentation runs: the animation
            // lead-in + ragdoll handoff, or the legacy time-driven grounding blend.
            // Locomotion parameters stay frozen so Death plays cleanly.
            if (ShouldApplyDeathGrounding(_deathLatched, ragdollActive))
            {
                UpdateDeathPresentationVisual();
                return;
            }

            float planarSpeed = zombie != null ? zombie.CurrentPlanarSpeed : 0f;

            // State selection (Idle vs Walk) - unchanged from the 1Q foundation.
            animator.SetFloat(SpeedHash, planarSpeed);

            // Bug 4 - cadence sync: drive ONLY the Walk state's playback speed from
            // the actual code-driven translation speed. Attack and Death are not
            // driven by this parameter (their states ignore it), and Animator.speed
            // is never touched, so their timing stays authored.
            animator.SetFloat(
                LocomotionSpeedMultiplierHash,
                ComputeLocomotionSpeedMultiplier(
                    planarSpeed, walkReferenceSpeed, minimumLocomotionMultiplier, maximumLocomotionMultiplier));
        }

        /// <summary>
        /// Pure helper (QA fix #1B Bug 3): the attack animation may play only while the
        /// enemy is NOT death-latched and an Animator exists. Static and side-effect
        /// free so EditMode tests can pin the "dead enemy cannot generate attack
        /// presentation" invariant without a scene.
        /// </summary>
        public static bool ShouldPlayAttackAnimation(bool deathLatched, bool hasAnimator)
        {
            return !deathLatched && hasAnimator;
        }

        private void HandleAttack(ZombieController source, int damage)
        {
            if (!ShouldPlayAttackAnimation(_deathLatched, animator != null))
            {
                return;
            }

            // DamagedPlayer already carries the gameplay authority (damage dealt and
            // cooldown scheduled before the event fires); this is presentation only.
            animator.SetTrigger(AttackHash);
        }

        private void HandleDied(ZombieController source)
        {
            if (_deathLatched)
            {
                return;
            }

            _deathLatched = true;

            // QA fix #5 - ONE-SHOT: the latch and the presentation-start flag together
            // guarantee Animator.Play runs exactly once per death. A repeated Died
            // callback (or any other path) can never restart the death clip at
            // normalized time 0.
            if (!ShouldStartDeathPresentation(_deathLatched, _deathPresentationStarted))
            {
                return;
            }

            _deathPresentationStarted = true;

            // QA fix #10/#11 - sanity check: a misconfigured final grounded Y (after
            // the contact margin) ABOVE the standing Y can never lift the corpse (the
            // per-frame downward-only clamp discards upward motion), but QA should
            // hear about the misconfiguration.
            float effectiveFinalGroundedY = ApplyDeathGroundingContactMargin(
                deathGroundedVisualY, deathGroundingContactMargin);

            if (_productionVisual != null && effectiveFinalGroundedY > _standingProductionVisualY)
            {
                Debug.LogWarning(
                    "[1Q QA fix #10/#11] Effective death grounded Y " +
                    effectiveFinalGroundedY.ToString("0.000") + " (measured " +
                    deathGroundedVisualY.ToString("0.000") + " - contact margin " +
                    ClampDeathGroundingContactMargin(deathGroundingContactMargin).ToString("0.000") +
                    ") is ABOVE the standing visual Y " + _standingProductionVisualY.ToString("0.000") +
                    " - the corpse will stay at the standing Y until the deactivation safety " +
                    "timeout. Re-run 'Set Up Basic Infected Production Visual' to measure the " +
                    "correct value.", this);
            }

            // QA fix #7 - the dead corpse is presentation-only: disable the gameplay
            // colliders immediately after death is registered (never interrupting the
            // visual death animation, which is purely Animator-driven). Their
            // authored states are restored on reuse by OnEnable.
            DisableGameplayColliders();

            ForceDeathPresentation(animator);
        }

        /// <summary>
        /// QA fix #7 - pure capture: snapshots every collider's enabled state. Static
        /// and side-effect free for EditMode tests.
        /// </summary>
        public static bool[] CaptureColliderEnabledStates(Collider[] colliders)
        {
            if (colliders == null)
            {
                return null;
            }

            bool[] states = new bool[colliders.Length];

            for (int i = 0; i < colliders.Length; i++)
            {
                states[i] = colliders[i] != null && colliders[i].enabled;
            }

            return states;
        }

        /// <summary>
        /// QA fix #7 - pure application: writes an enabled-state snapshot back onto the
        /// colliders. Guards length mismatches and null entries. Static and
        /// side-effect free for EditMode tests.
        /// </summary>
        public static void ApplyColliderEnabledStates(Collider[] colliders, bool[] states)
        {
            if (colliders == null || states == null || colliders.Length != states.Length)
            {
                return;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = states[i];
                }
            }
        }

        /// <summary>
        /// QA fix #7 - disables all captured gameplay colliders (idempotent). Called
        /// exactly once per death, right after the one-shot death gate.
        /// </summary>
        private void DisableGameplayColliders()
        {
            if (!_gameplayCollidersCaptured || _gameplayColliders == null)
            {
                return;
            }

            bool[] disabled = new bool[_gameplayColliders.Length]; // all false
            ApplyColliderEnabledStates(_gameplayColliders, disabled);
        }

        /// <summary>
        /// QA fix #7 - restores the authored collider enabled states captured in Awake.
        /// Called on OnEnable so reused enemies collide correctly.
        /// </summary>
        private void RestoreGameplayColliders()
        {
            if (!_gameplayCollidersCaptured)
            {
                return;
            }

            ApplyColliderEnabledStates(_gameplayColliders, _gameplayColliderEnabledStates);
        }

        /// <summary>
        /// QA fix #4 - the deterministic death entry, also used as the isolation test
        /// hook: resets the Attack trigger, freezes Speed and the locomotion
        /// multiplier, latches Dead, then switches the BASE layer into the Death state
        /// via Animator.Play with the FULL state path hash ("Base Layer.Death") at
        /// normalized time 0 - immediate and independent of AnyState transition
        /// evaluation. The Death state has no exits, so nothing can animate the enemy
        /// out of it. Static so an editor diagnostic can force it without any
        /// gameplay involvement (no damage, no hit feedback, no Died event, no
        /// despawn logic).
        /// </summary>
        public static void ForceDeathPresentation(Animator animator)
        {
            if (animator == null)
            {
                return;
            }

            animator.ResetTrigger(AttackHash);
            animator.SetFloat(SpeedHash, 0f);
            animator.SetFloat(LocomotionSpeedMultiplierHash, 1f);
            animator.SetBool(DeadHash, true);

            // Full-path hash: Unity's documented Play contract is "Base Layer.Death",
            // not the bare state name.
            animator.Play(DeathStateFullPathHash, DeathPlayLayer, 0f);
        }

        /// <summary>
        /// Instance convenience for the editor diagnostic menu: forces the death
        /// presentation on this bridge's Animator (no gameplay involved) and latches
        /// the bridge so Update can no longer drive locomotion parameters.
        ///
        /// QA fix #5 - one-shot: returns false (and does NOT re-Play) when the death
        /// presentation has already been started, so repeated diagnostic invocations
        /// can never restart the death clip at its first frames.
        /// </summary>
        public bool ForceDeathPresentation()
        {
            _deathLatched = true;

            if (!ShouldStartDeathPresentation(_deathLatched, _deathPresentationStarted))
            {
                return false;
            }

            _deathPresentationStarted = true;
            ForceDeathPresentation(animator);
            return true;
        }

        /// <summary>
        /// QA fix #10 / 1Q FINAL - runs once per frame after the death latch and
        /// drives the death presentation:
        ///
        ///   HYBRID RAGDOLL PATH (ragdoll configured by the setup tool):
        ///   the one-shot Death clip plays as the lead-in for
        ///   deathRagdollHandoffSeconds (default 0.30 s - the body is already
        ///   starting to fall inside the clip), then the skeleton is handed to
        ///   physics exactly once: the Animator is disabled inside
        ///   EnemyRagdoll.ActivateRagdoll, the bodies become non-kinematic and
        ///   the ragdoll colliders enable. Physics completes the fall and
        ///   establishes ground contact; NO ProductionVisual Y correction ever
        ///   runs while the ragdoll is active (the two systems never fight).
        ///
        ///   ANIMATION-ONLY PATH (no ragdoll configured - prototype fallback):
        ///   deathT  = Base Layer.Death normalized time (clamped, non-looping)
        ///   blendT  = smoothstep(remap(deathT, start, end))
        ///   finalY  = serializedDeathGroundedY - contactMargin (QA fix #11)
        ///   nextY   = clampDownward(currentY, lerp(standingY, finalY, blendT))
        ///
        /// Only the ProductionVisual child's Y is ever written - the gameplay
        /// root, collider and the standing offset for Idle/Walk/Attack are
        /// untouched.
        /// </summary>
        private void UpdateDeathPresentationVisual()
        {
            // 1Q FINAL / QA fix #1 - hybrid handoff: the Death clip is the lead-in.
            // Tick the handoff timer; the ONE-SHOT gate fires the ragdoll exactly
            // once (never while already active, never twice), and this branch never
            // falls through to the animation/grounding path.
            if (ragdoll != null && ragdoll.IsConfigured)
            {
                bool ragdollActiveNow = ragdoll.IsRagdollActive;

                if (!ragdollActiveNow && !_ragdollHandoffDone)
                {
                    _ragdollHandoffElapsed += Time.deltaTime;
                }

                if (ShouldTriggerRagdollHandoff(
                        ragdollActiveNow, _ragdollHandoffDone,
                        _ragdollHandoffElapsed, deathRagdollHandoffSeconds))
                {
                    _ragdollHandoffDone = true;
                    _ragdollActivationTime = Time.time;
                    ragdoll.ActivateRagdoll(animator);
                }

                return;
            }

            if (_productionVisual == null)
            {
                return;
            }

            AnimatorStateInfo deathStateInfo = animator.GetCurrentAnimatorStateInfo(DeathPlayLayer);

            if (!deathStateInfo.IsName(DeathStateName))
            {
                return;
            }

            // QA fix #9 - persist the clip-finished flag once the death clip has
            // played out (it is terminal and non-looping, so this never resets).
            if (!_deathClipFinished && deathStateInfo.normalizedTime >= 0.999f)
            {
                _deathClipFinished = true;
            }

            float currentY = _productionVisual.localPosition.y;

            // QA fix #11 - the blend target is the MARGIN-ADJUSTED final grounded Y:
            // the serialized pose-measured Y minus the small downward contact margin,
            // so the corpse ends in a very slight contact with the road rather than
            // hovering a few centimetres above it.
            float finalGroundedY = ApplyDeathGroundingContactMargin(
                deathGroundedVisualY, deathGroundingContactMargin);

            // QA fix #10 - once the clip has finished AND the corpse already rests
            // at the final grounded Y, the visual is completely stationary: no
            // further writes, so nothing can move after the animation ends.
            if (_deathClipFinished &&
                IsDeathGroundingComplete(currentY, finalGroundedY, deathGroundingCompletionTolerance))
            {
                return;
            }

            float deathT = Mathf.Clamp01(deathStateInfo.normalizedTime);
            float blendProgress = ComputeDeathGroundingProgress(
                deathT, deathGroundingStartNormalizedTime, deathGroundingEndNormalizedTime);
            float nextY = ComputeDeathGroundedVisualY(
                _standingProductionVisualY, finalGroundedY, blendProgress);

            // QA fix #10 - the downward-only invariant is enforced on the WRITE, not
            // on a chased target: the blend is already monotonic in normalized time,
            // and this guard makes an upward motion impossible even for a
            // misconfigured final Y or an animator restart.
            nextY = ClampDeathGroundingDownwardOnly(currentY, nextY);

            Vector3 position = _productionVisual.localPosition;
            position.y = nextY;
            _productionVisual.localPosition = position;
        }

        /// <summary>
        /// QA fix #6 - returns the production visual to the standing local Y captured
        /// in Awake (x/z preserved). Idempotent and safe at any time.
        /// </summary>
        private void RestoreStandingProductionVisualY()
        {
            if (_productionVisual == null)
            {
                return;
            }

            Vector3 position = _productionVisual.localPosition;
            position.y = _standingProductionVisualY;
            _productionVisual.localPosition = position;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            walkReferenceSpeed = Mathf.Max(0.1f, walkReferenceSpeed);
            minimumLocomotionMultiplier = Mathf.Max(0.1f, minimumLocomotionMultiplier);
            maximumLocomotionMultiplier = Mathf.Max(minimumLocomotionMultiplier, maximumLocomotionMultiplier);

            // QA fix #10 - a grounded corpse's final Y must sit below the root
            // (never positive); the grounding window must stay inside the clip and
            // end after it starts; the completion tolerance is non-negative; the
            // QA fix #11 contact margin is downward-only and capped small.
            deathGroundedVisualY = Mathf.Min(0f, deathGroundedVisualY);
            deathGroundingStartNormalizedTime =
                Mathf.Clamp(deathGroundingStartNormalizedTime, 0.01f, 0.99f);
            deathGroundingEndNormalizedTime =
                Mathf.Clamp(deathGroundingEndNormalizedTime, 0.01f, 0.99f);

            if (deathGroundingEndNormalizedTime <= deathGroundingStartNormalizedTime)
            {
                deathGroundingEndNormalizedTime = Mathf.Min(
                    0.99f, deathGroundingStartNormalizedTime + 0.05f);
            }

            deathGroundingCompletionTolerance = Mathf.Max(0f, deathGroundingCompletionTolerance);
            deathGroundingContactMargin = ClampDeathGroundingContactMargin(deathGroundingContactMargin);

            // 1Q FINAL - hybrid ragdoll death timings: the lead-in stays inside the
            // 0.25-0.40 s band of the death clip's fall, the settle window is long
            // enough for ground contact but never indefinite.
            deathRagdollHandoffSeconds = Mathf.Clamp(deathRagdollHandoffSeconds, 0f, 2f);
            deathRagdollSettleSeconds = Mathf.Clamp(deathRagdollSettleSeconds, 0.1f, 3f);
        }
#endif
    }
}
