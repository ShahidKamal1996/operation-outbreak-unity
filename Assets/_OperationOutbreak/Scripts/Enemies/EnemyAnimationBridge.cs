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
        }

        private void OnEnable()
        {
            if (zombie != null)
            {
                zombie.DamagedPlayer += HandleAttack;
                zombie.Died += HandleDied;
            }
        }

        private void OnDisable()
        {
            if (zombie != null)
            {
                zombie.DamagedPlayer -= HandleAttack;
                zombie.Died -= HandleDied;
            }
        }

        private void Update()
        {
            if (animator == null || _deathLatched)
            {
                // Once dead the locomotion parameters are frozen so Death plays cleanly.
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
            ForceDeathPresentation(animator);
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            walkReferenceSpeed = Mathf.Max(0.1f, walkReferenceSpeed);
            minimumLocomotionMultiplier = Mathf.Max(0.1f, minimumLocomotionMultiplier);
            maximumLocomotionMultiplier = Mathf.Max(minimumLocomotionMultiplier, maximumLocomotionMultiplier);
        }
#endif
    }
}
