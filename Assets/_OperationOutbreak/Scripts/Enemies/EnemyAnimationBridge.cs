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

        private static readonly int SpeedHash = Animator.StringToHash(SpeedParameter);
        private static readonly int AttackHash = Animator.StringToHash(AttackParameter);
        private static readonly int DeadHash = Animator.StringToHash(DeadParameter);

        /// <summary>True once the death animation has been requested; latches all other animation off.</summary>
        public bool IsDeathLatched => _deathLatched;

        private bool _deathLatched;

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
            animator.SetFloat(SpeedHash, planarSpeed);
        }

        private void HandleAttack(ZombieController source, int damage)
        {
            if (_deathLatched || animator == null)
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

            if (animator == null)
            {
                return;
            }

            // Death outranks everything: clear a queued Attack trigger so it cannot
            // consume the transition after death.
            animator.ResetTrigger(AttackHash);
            animator.SetFloat(SpeedHash, 0f);
            animator.SetBool(DeadHash, true);
        }
    }
}
