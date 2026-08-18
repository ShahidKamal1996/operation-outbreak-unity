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

        [Header("Death Grounding (Milestone 1Q QA fix #6)")]
        [Tooltip("Height of the enemy gameplay root above the lane surface. The corpse " +
                 "grounding is computed in root-local space against this value.")]
        [Min(0.1f)]
        [SerializeField] private float enemyRootGroundHeight = 1f;

        [Tooltip("Fallback extra lowering (in meters) applied to the production visual " +
                 "during the death presentation, used only when the death-pose measurement " +
                 "is unavailable.")]
        [Min(0f)]
        [SerializeField] private float deathGroundingOffsetY = 0.6f;

        [Tooltip("Seconds over which the death grounding correction blends in, so the " +
                 "corpse settles smoothly instead of teleporting.")]
        [Min(0.05f)]
        [SerializeField] private float deathGroundingBlendDuration = 0.35f;

        [Tooltip("Normalized time of the death clip at which the corpse pose is measured. " +
                 "The measurement must happen late in the fall, when the pose is close to " +
                 "its final resting shape.")]
        [Range(0.5f, 0.99f)]
        [SerializeField] private float deathGroundingSampleNormalizedTime = 0.9f;

        [Tooltip("Normalized time at which a FINAL refinement measurement recomputes the " +
                 "grounding target from the true resting pose (QA fix #7: the fall still " +
                 "moves slightly after the first sample).")]
        [Range(0.9f, 1f)]
        [SerializeField] private float deathGroundingRefineNormalizedTime = 0.99f;

        [Tooltip("QA fix #9 - the grounding settle counts as complete when the visual's Y " +
                 "is within this distance of the target. The enemy must stay visible until " +
                 "the corpse has actually reached the road.")]
        [Min(0f)]
        [SerializeField] private float deathGroundingCompletionTolerance = 0.015f;

        [Tooltip("Measure the corpse's lowest point from the actual animated death pose " +
                 "and ground the production visual to it; fall back to deathGroundingOffsetY " +
                 "only when the measurement is unavailable.")]
        [SerializeField] private bool useMeasuredDeathGrounding = true;

        private Transform _productionVisual;
        private float _standingProductionVisualY;
        private float _deathGroundingTargetY;
        private bool _deathGroundingMeasured;
        private bool _deathGroundingRefined;
        private bool _deathClipFinished;
        private Mesh _deathPoseBakeMesh;

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
        /// QA fix #6 - pure gate: the death-only grounding correction applies only
        /// after the death latch. While the enemy lives, the standing
        /// ProductionVisual offset is never touched.
        /// </summary>
        public static bool ShouldApplyDeathGrounding(bool deathLatched)
        {
            return deathLatched;
        }

        /// <summary>
        /// QA fix #6 - pure gate: the death-pose measurement happens only once the
        /// death clip has advanced to (or past) the sample threshold, so the measured
        /// pose is the near-final lying pose and never the standing pose.
        /// </summary>
        public static bool ShouldMeasureDeathGrounding(float normalizedTime, float sampleThreshold)
        {
            return normalizedTime >= Mathf.Max(0.01f, sampleThreshold);
        }

        /// <summary>
        /// QA fix #7 - pure computation, WORLD-SPACE DELTA form. Given the corpse's
        /// lowest visible vertex WORLD Y (measured while the visual is still at its
        /// current offset) and the lane surface WORLD Y, returns the ProductionVisual
        /// local Y that places that vertex exactly on the surface:
        ///   targetLocalY = currentVisualLocalY + (groundWorldY - lowestCorpseWorldY)
        /// This is valid because the visual's parent chain (the enemy gameplay root)
        /// is identity-rotated and identity-scaled: a world-space Y delta equals a
        /// local-Y delta. Measuring everything in ONE space (world) removes the
        /// instance-root/renderer/local-frame ambiguity that made the QA fix #6
        /// correction miss the road.
        /// </summary>
        public static float ComputeDeathGroundedTargetLocalY(
            float currentVisualLocalY, float lowestCorpseWorldY, float groundWorldY)
        {
            return currentVisualLocalY + (groundWorldY - lowestCorpseWorldY);
        }

        /// <summary>
        /// QA fix #8 - pure monotonic rule: the death-grounding target may only ever
        /// move DOWNWARD (or stay put). It can never exceed the standing ceiling, and
        /// a later pass (the clip-end refinement) can never raise the target above an
        /// earlier one. If a measurement says the corpse is already below the ground,
        /// the upward "correction" is DISCARDED - a small sink is preferable to an
        /// obvious upward pop. Returns the clamped target.
        /// </summary>
        public static float ClampDeathGroundingTargetDownwardOnly(
            float previousTargetY, float computedTargetY, float standingCeilingY)
        {
            float clampedToCeiling = Mathf.Min(computedTargetY, standingCeilingY);
            return Mathf.Min(previousTargetY, clampedToCeiling);
        }

        /// <summary>
        /// QA fix #8 - pure gate: a final REFINEMENT measurement runs only once the
        /// death clip has essentially completed, so the target is recomputed from the
        /// true resting pose (the fall still moves slightly between the first sample
        /// and the end, which is what left the corpse floating).
        /// </summary>
        public static bool ShouldRefineDeathGrounding(float normalizedTime, float refineThreshold)
        {
            return normalizedTime >= Mathf.Max(0.01f, refineThreshold);
        }

        /// <summary>
        /// QA fix #9 - pure tolerance check: the grounding settle is complete when the
        /// visual's Y is within the configured tolerance of the target (abs distance).
        /// </summary>
        public static bool IsDeathGroundingComplete(float currentVisualY, float targetY, float tolerance)
        {
            return Mathf.Abs(targetY - currentVisualY) <= Mathf.Max(0f, tolerance);
        }

        /// <summary>
        /// QA fix #9 - pure completion gate: the death presentation is complete only
        /// when BOTH the death animation has finished AND the corpse grounding has
        /// settled within tolerance. Deactivation must wait for both - a clip-length
        /// timer alone cuts the late settle short (the QA fix #9 symptom).
        /// </summary>
        public static bool ShouldCompleteDeathPresentation(bool animationFinished, bool groundingComplete)
        {
            return animationFinished && groundingComplete;
        }

        /// <summary>
        /// QA fix #9 - live completion state read by ZombieController's death
        /// feedback: the death clip has finished AND the production visual's Y is
        /// within tolerance of the (downward-only) grounding target. With no
        /// production visual the presentation is trivially complete once the clip
        /// finished.
        /// </summary>
        public bool IsDeathPresentationComplete
        {
            get
            {
                if (!_deathLatched || !_deathClipFinished)
                {
                    return false;
                }

                if (_productionVisual == null)
                {
                    return true;
                }

                return IsDeathGroundingComplete(
                    _productionVisual.localPosition.y, _deathGroundingTargetY, deathGroundingCompletionTolerance);
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

            // QA fix #8 - the death-grounding target starts AT the standing ceiling:
            // until a measurement produces a LOWER target, no grounding movement can
            // occur at all (this is what removes the upward pop before settling).
            _deathGroundingTargetY = _standingProductionVisualY;

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
            _deathGroundingMeasured = false;
            _deathGroundingRefined = false;
            _deathClipFinished = false;
            _deathGroundingTargetY = _standingProductionVisualY;
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
        }

        private void Update()
        {
            if (animator == null)
            {
                return;
            }

            // QA fix #6 - after the death latch, ONLY the death grounding correction
            // runs: locomotion parameters stay frozen so Death plays cleanly, and the
            // production visual settles smoothly onto the road.
            if (ShouldApplyDeathGrounding(_deathLatched))
            {
                UpdateDeathGrounding();
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
        /// QA fix #6/#7 - runs once per frame after the death latch: waits until the
        /// death clip has advanced to the sample threshold, then measures the corpse
        /// pose's lowest point in WORLD space and smoothly blends the production
        /// visual's local Y toward the world-delta grounding target. A final
        /// refinement pass at clip completion recomputes the target from the true
        /// resting pose (the fall still moves slightly after the first sample, which
        /// is what left the corpse floating). Only the ProductionVisual child's Y is
        /// ever written - the gameplay root, collider and standing offset for
        /// Idle/Walk/Attack are untouched.
        /// </summary>
        private void UpdateDeathGrounding()
        {
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

            bool wantMeasure =
                !_deathGroundingMeasured &&
                ShouldMeasureDeathGrounding(deathStateInfo.normalizedTime, deathGroundingSampleNormalizedTime);

            bool wantRefine =
                _deathGroundingMeasured &&
                !_deathGroundingRefined &&
                ShouldRefineDeathGrounding(deathStateInfo.normalizedTime, deathGroundingRefineNormalizedTime);

            if (wantMeasure || wantRefine)
            {
                RecomputeDeathGroundingTarget(wantRefine);

                if (wantMeasure)
                {
                    _deathGroundingMeasured = true;
                }
                else
                {
                    _deathGroundingRefined = true;
                }
            }

            // Smooth settle: move toward the target over the configured duration so
            // the corpse eases onto the road instead of teleporting.
            float currentY = _productionVisual.localPosition.y;

            // QA fix #8 - defensive re-assertion of the monotonic rule: whatever else
            // happened, the target may never sit ABOVE the visual's current Y, so the
            // settle below can only move the visual downward or keep it still.
            _deathGroundingTargetY = Mathf.Min(_deathGroundingTargetY, currentY);

            float totalDistance = Mathf.Max(0.0001f, Mathf.Abs(_deathGroundingTargetY - currentY));
            float step = totalDistance / Mathf.Max(0.05f, deathGroundingBlendDuration) * Time.deltaTime;
            float newY = Mathf.MoveTowards(currentY, _deathGroundingTargetY, step);

            // QA fix #9 - snap to the target once within tolerance, so the completion
            // check settles promptly instead of approaching asymptotically.
            if (IsDeathGroundingComplete(newY, _deathGroundingTargetY, deathGroundingCompletionTolerance))
            {
                newY = _deathGroundingTargetY;
            }

            Vector3 position = _productionVisual.localPosition;
            position.y = newY;
            _productionVisual.localPosition = position;
        }

        /// <summary>
        /// QA fix #7 - recomputes the grounding target from a WORLD-SPACE measurement
        /// and logs the full calculation once per pass, so manual QA can verify the
        /// maths from the console.
        /// </summary>
        private void RecomputeDeathGroundingTarget(bool isRefinement)
        {
            // The lane surface's world Y, derived from the gameplay root height (the
            // root rides at y=1; the lane is at y=0).
            float groundWorldY = transform.position.y - enemyRootGroundHeight;
            float currentVisualLocalY = _productionVisual.localPosition.y;

            if (useMeasuredDeathGrounding && TryMeasureDeathPoseLowestWorldY(out float lowestCorpseWorldY))
            {
                float computedTarget = ComputeDeathGroundedTargetLocalY(
                    currentVisualLocalY, lowestCorpseWorldY, groundWorldY);

                // QA fix #8 - MONOTONIC DOWNWARD-ONLY rule: the target may never rise
                // above the standing ceiling nor above a previous pass's target. A
                // mid-fall sample that would lift the visual is discarded, and the
                // clip-end refinement can only move the target further down.
                _deathGroundingTargetY = ClampDeathGroundingTargetDownwardOnly(
                    _deathGroundingTargetY, computedTarget, _standingProductionVisualY);

                Debug.Log(
                    "[1Q QA fix #7/#8] Death grounding " + (isRefinement ? "refinement" : "measurement") + ": " +
                    $"standingVisualY={_standingProductionVisualY:0.000}, currentVisualY={currentVisualLocalY:0.000}, " +
                    $"lowestCorpseWorldY={lowestCorpseWorldY:0.000}, groundWorldY={groundWorldY:0.000}, " +
                    $"computedTargetY={computedTarget:0.000}, clampedTargetY={_deathGroundingTargetY:0.000} " +
                    $"(downward-only, never above {_standingProductionVisualY:0.000}).", this);
            }
            else
            {
                // Documented fallback: lower the visual by the serialized offset,
                // clamped by the same monotonic downward-only rule.
                float computedTarget = _standingProductionVisualY - Mathf.Max(0f, deathGroundingOffsetY);
                _deathGroundingTargetY = ClampDeathGroundingTargetDownwardOnly(
                    _deathGroundingTargetY, computedTarget, _standingProductionVisualY);

                Debug.LogWarning(
                    "[1Q QA fix #7/#8] Death grounding fallback (measurement unavailable): " +
                    $"clampedTargetY={_deathGroundingTargetY:0.000} (standing {_standingProductionVisualY:0.000} - " +
                    $"{Mathf.Max(0f, deathGroundingOffsetY):0.000}, downward-only).", this);
            }
        }

        /// <summary>
        /// QA fix #7 - bakes the production skinned mesh once per death and returns
        /// the lowest vertex WORLD Y. Measuring in world space removes every
        /// instance-root/renderer/local-frame ambiguity from QA fix #6: the baked
        /// vertices are transformed through the renderer transform into world space,
        /// and the correction is computed as a pure world-space delta applied to the
        /// visual's local Y (valid because the parent chain is identity-rotated and
        /// identity-scaled).
        /// </summary>
        private bool TryMeasureDeathPoseLowestWorldY(out float lowestWorldY)
        {
            lowestWorldY = 0f;

            if (_productionVisual == null)
            {
                return false;
            }

            SkinnedMeshRenderer renderer = null;
            foreach (SkinnedMeshRenderer candidate in
                     _productionVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
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

            if (_deathPoseBakeMesh == null)
            {
                _deathPoseBakeMesh = new Mesh();
            }

            renderer.BakeMesh(_deathPoseBakeMesh);
            Vector3[] vertices = _deathPoseBakeMesh.vertices;

            if (vertices == null || vertices.Length == 0)
            {
                return false;
            }

            float minimum = float.MaxValue;

            foreach (Vector3 vertex in vertices)
            {
                float worldY = renderer.transform.TransformPoint(vertex).y;

                if (worldY < minimum)
                {
                    minimum = worldY;
                }
            }

            lowestWorldY = minimum;
            return true;
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
            enemyRootGroundHeight = Mathf.Max(0.1f, enemyRootGroundHeight);
            deathGroundingOffsetY = Mathf.Max(0f, deathGroundingOffsetY);
            deathGroundingBlendDuration = Mathf.Max(0.05f, deathGroundingBlendDuration);
            deathGroundingSampleNormalizedTime = Mathf.Clamp(deathGroundingSampleNormalizedTime, 0.5f, 0.99f);
            deathGroundingRefineNormalizedTime = Mathf.Clamp(deathGroundingRefineNormalizedTime, 0.9f, 1f);
            deathGroundingCompletionTolerance = Mathf.Max(0f, deathGroundingCompletionTolerance);
        }
#endif
    }
}
