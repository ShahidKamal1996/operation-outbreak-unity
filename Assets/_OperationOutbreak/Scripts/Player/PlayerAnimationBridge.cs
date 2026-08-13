using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Player
{
    /// <summary>
    /// Milestone 1O.5 - one-way bridge between the authoritative gameplay components and
    /// the Carl character's Animator.
    ///
    /// DIRECTION OF CONTROL IS STRICTLY ONE-WAY: gameplay -> animation. This component
    /// reads a single already-computed property (PlayerController.CurrentPlanarSpeed) and
    /// listens to three events that the gameplay systems raise for their own reasons
    /// (WeaponController.ShotFired, PlayerHealth.Damaged, PlayerHealth.Died). It never
    /// moves the player, never spawns projectiles, never touches health, never changes
    /// fire rate or damage, and never feeds anything back into gameplay. Deleting this
    /// component would leave the game fully playable.
    ///
    /// WHY A BRIDGE INSTEAD OF ANIMATOR LOGIC IN PlayerController:
    /// keeping it separate means the movement controller stays a movement controller, and
    /// the whole visual layer can be disabled or swapped without editing gameplay code.
    ///
    /// ROOT MOTION: the Animator must have Apply Root Motion OFF. Locomotion clips have
    /// their root motion baked into the pose, so the Animator only ever poses the mesh -
    /// PlayerController remains the single source of movement.
    ///
    /// NO IDLE CLIP EXISTS. When the player is stationary the bridge drives Speed to 0 and
    /// IsMoving to false, which parks the Locomotion blend tree on the first frame of
    /// Walking held at speed 0 - a neutral standing pose. No procedural idle is invented
    /// and Walking is never allowed to loop while standing still.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAnimationBridge : MonoBehaviour
    {
        [Header("Gameplay Sources (read-only observers)")]
        [Tooltip("Movement authority. Only its CurrentPlanarSpeed is read.")]
        [SerializeField] private PlayerController playerController;

        [Tooltip("Health authority. Only its Damaged / Died events are observed.")]
        [SerializeField] private PlayerHealth playerHealth;

        [Tooltip("Weapon authority. Only its ShotFired event is observed.")]
        [SerializeField] private WeaponController weaponController;

        [Header("Animation Target")]
        [Tooltip("Animator on the Carl visual. Apply Root Motion must be OFF.")]
        [SerializeField] private Animator animator;

        [Header("Locomotion Tuning (visual only)")]
        [Tooltip("Planar speed at or below which the character is treated as standing still.")]
        [Min(0f)]
        [SerializeField] private float idleSpeedThreshold = 0.15f;

        [Tooltip("Planar speed used to normalise the locomotion blend. Should match the fastest authored move speed.")]
        [Min(0.01f)]
        [SerializeField] private float referenceMoveSpeed = 6f;

        [Tooltip("Seconds of damping applied to the Speed parameter so the blend does not jitter.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float speedDamping = 0.1f;

        [Header("Action Tuning (visual only)")]
        [Tooltip("Minimum seconds between two Gunplay triggers, so rapid auto-fire cannot spam transitions.")]
        [Min(0f)]
        [SerializeField] private float minimumSecondsBetweenGunplay = 0.18f;

        [Tooltip("Minimum seconds between two Hit Reaction triggers.")]
        [Min(0f)]
        [SerializeField] private float minimumSecondsBetweenHitReactions = 0.4f;

        // Animator parameter names, hashed once. Hashing avoids a per-frame string lookup.
        public const string SpeedParameter = "Speed";
        public const string IsMovingParameter = "IsMoving";
        public const string GunplayParameter = "Gunplay";
        public const string HitReactionParameter = "HitReaction";
        public const string DeadParameter = "Dead";

        private static readonly int SpeedHash = Animator.StringToHash(SpeedParameter);
        private static readonly int IsMovingHash = Animator.StringToHash(IsMovingParameter);
        private static readonly int GunplayHash = Animator.StringToHash(GunplayParameter);
        private static readonly int HitReactionHash = Animator.StringToHash(HitReactionParameter);
        private static readonly int DeadHash = Animator.StringToHash(DeadParameter);

        /// <summary>Diagnostic counter: Gunplay triggers actually sent to the Animator.</summary>
        public int GunplayTriggerCount { get; private set; }

        /// <summary>Diagnostic counter: Hit Reaction triggers actually sent to the Animator.</summary>
        public int HitReactionTriggerCount { get; private set; }

        /// <summary>Diagnostic counter: Death triggers sent to the Animator (must never exceed 1).</summary>
        public int DeathTriggerCount { get; private set; }

        /// <summary>True once the death animation has been requested; latches all other animation off.</summary>
        public bool IsDeathLatched => _deathLatched;

        /// <summary>True when the Animator reference and a valid humanoid Avatar are both present.</summary>
        public bool HasValidRig => animator != null && animator.avatar != null && animator.avatar.isValid;

        private bool _deathLatched;
        private float _lastGunplayTime = float.NegativeInfinity;
        private float _lastHitReactionTime = float.NegativeInfinity;

        private void Reset()
        {
            playerController = GetComponent<PlayerController>();
            playerHealth = GetComponent<PlayerHealth>();
            weaponController = GetComponentInChildren<WeaponController>(true);
            animator = GetComponentInChildren<Animator>(true);
        }

        private void Awake()
        {
            if (playerController == null)
            {
                playerController = GetComponent<PlayerController>();
            }

            if (playerHealth == null)
            {
                playerHealth = GetComponent<PlayerHealth>();
            }

            // Resolved once at startup - never searched for per frame.
            if (weaponController == null)
            {
                weaponController = GetComponentInChildren<WeaponController>(true);
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (animator != null)
            {
                // Enforced in code as well as in the scene: animation must never move the player.
                animator.applyRootMotion = false;
            }
        }

        private void OnEnable()
        {
            if (playerHealth != null)
            {
                playerHealth.Damaged += HandleDamaged;
                playerHealth.Died += HandleDied;

                // Guard against a death that happened before this component woke up.
                if (playerHealth.IsDead)
                {
                    HandleDied();
                }
            }

            if (weaponController != null)
            {
                weaponController.ShotFired += HandleShotFired;
            }
        }

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.Damaged -= HandleDamaged;
                playerHealth.Died -= HandleDied;
            }

            if (weaponController != null)
            {
                weaponController.ShotFired -= HandleShotFired;
            }
        }

        private void Update()
        {
            if (animator == null || _deathLatched)
            {
                // Once dead the locomotion parameters are frozen so Death plays cleanly.
                return;
            }

            float planarSpeed = playerController != null ? playerController.CurrentPlanarSpeed : 0f;
            bool isMoving = IsConsideredMoving(planarSpeed, idleSpeedThreshold);
            float normalised = NormaliseSpeed(planarSpeed, idleSpeedThreshold, referenceMoveSpeed);

            animator.SetFloat(SpeedHash, normalised, speedDamping, Time.deltaTime);
            animator.SetBool(IsMovingHash, isMoving);
        }

        private void HandleShotFired()
        {
            if (_deathLatched || animator == null)
            {
                return;
            }

            if (!HasCooldownElapsed(Time.time, _lastGunplayTime, minimumSecondsBetweenGunplay))
            {
                return;
            }

            _lastGunplayTime = Time.time;
            GunplayTriggerCount++;
            animator.SetTrigger(GunplayHash);
        }

        private void HandleDamaged()
        {
            if (_deathLatched || animator == null)
            {
                return;
            }

            if (!HasCooldownElapsed(Time.time, _lastHitReactionTime, minimumSecondsBetweenHitReactions))
            {
                return;
            }

            _lastHitReactionTime = Time.time;
            HitReactionTriggerCount++;
            animator.SetTrigger(HitReactionHash);
        }

        private void HandleDied()
        {
            if (_deathLatched)
            {
                return;
            }

            _deathLatched = true;

            if (animator == null)
            {
                return;
            }

            // Death outranks everything: clear pending cosmetic triggers so a queued
            // Gunplay or Hit Reaction cannot consume the transition after death.
            animator.ResetTrigger(GunplayHash);
            animator.ResetTrigger(HitReactionHash);
            animator.SetFloat(SpeedHash, 0f);
            animator.SetBool(IsMovingHash, false);

            DeathTriggerCount++;
            animator.SetBool(DeadHash, true);
        }

        // ------------------------------------------------------------------ pure logic
        // Static and side-effect free so EditMode tests can verify the decisions without
        // a scene, an Animator or Play Mode.

        /// <summary>
        /// True when planar speed is meaningfully above the standing-still threshold.
        /// Keeps Walking from looping while the player is stationary.
        /// </summary>
        public static bool IsConsideredMoving(float planarSpeed, float idleThreshold)
        {
            return planarSpeed > Mathf.Max(0f, idleThreshold);
        }

        /// <summary>
        /// Maps planar speed onto the 0..1 locomotion blend axis. Anything at or below the
        /// idle threshold collapses to exactly 0 so the blend tree rests on a neutral pose
        /// rather than creeping into a walk cycle.
        /// </summary>
        public static float NormaliseSpeed(float planarSpeed, float idleThreshold, float referenceSpeed)
        {
            if (!IsConsideredMoving(planarSpeed, idleThreshold))
            {
                return 0f;
            }

            float reference = Mathf.Max(0.01f, referenceSpeed);
            return Mathf.Clamp01(planarSpeed / reference);
        }

        /// <summary>
        /// Retrigger guard shared by Gunplay and Hit Reaction. A non-positive cooldown
        /// always allows the trigger, which keeps the tuning field honest at 0.
        ///
        /// The small epsilon matters: Time.time accumulates in floating point, so an
        /// interval that is conceptually exactly the cooldown routinely lands a fraction
        /// under it. Without the tolerance a perfectly steady fire rate whose period
        /// equals the cooldown would drop roughly every other animation.
        /// </summary>
        public static bool HasCooldownElapsed(float now, float lastTime, float cooldown)
        {
            if (cooldown <= 0f)
            {
                return true;
            }

            const float boundaryTolerance = 1e-4f;
            return now - lastTime >= cooldown - boundaryTolerance;
        }
    }
}
