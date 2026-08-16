using OperationOutbreak.Enemies;
using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Weapons
{
    /// <summary>
    /// Milestone 1C auto-fire weapon. While this component is active it repeatedly
    /// launches the configured projectile straight down the combat lane (+Z).
    /// Aiming and target selection deliberately do not belong here.
    ///
    /// Milestone 1P - the muzzle flash presentation that used to live here (a sphere
    /// created in Start and toggled by a coroutine) was extracted to the feedback layer:
    /// MuzzleFlashFeedback listens to <see cref="ShotFired"/> and spawns the visual through
    /// CombatFeedback. This component no longer contains any presentation code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Projectile spawn location at the front of the weapon.")]
        [SerializeField] private Transform muzzlePoint;

        [Tooltip("Prototype projectile prefab launched by this weapon.")]
        [SerializeField] private Projectile projectilePrefab;

        [Tooltip("Health on the owning Player. Resolved once from the parent at startup.")]
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private EnemySpawner enemySpawner;
        [Min(1f)] [SerializeField] private float targetRange = 35f;
        [Min(0.05f)] [SerializeField] private float targetRefreshInterval = 0.15f;

        [Header("Weapon Configuration")]
        [Tooltip("Automatic shots fired per second while gameplay is active.")]
        [Min(0.01f)]
        [SerializeField] private float fireRate = 5f;

        [Tooltip("Projectile travel speed in world units per second.")]
        [Min(0.01f)]
        [SerializeField] private float projectileSpeed = 25f;

        [Tooltip("Seconds before a projectile is removed if it has not hit a target.")]
        [Min(0.01f)]
        [SerializeField] private float projectileLifetime = 3f;

        [Tooltip("Damage delivered by each confirmed projectile hit.")]
        [Min(1)]
        [SerializeField] private int damage = 1;

        /// <summary>
        /// Milestone 1O.5 - raised immediately AFTER a projectile has been spawned and
        /// initialised. Purely a notification for cosmetic observers (the Carl animation
        /// bridge, and since Milestone 1P the muzzle flash feedback). Subscribers cannot
        /// influence spawn position, fire rate, damage, targeting or cadence: everything
        /// that matters has already happened by the time this fires, and the return value
        /// is ignored.
        /// </summary>
        public event System.Action ShotFired;

        /// <summary>
        /// Milestone 1P - read-only view of the authored muzzle anchor so presentation
        /// listeners can place their own visuals at the existing firing origin without
        /// this component knowing anything about them. Writing is impossible; gameplay
        /// remains the only owner of the anchor.
        /// </summary>
        public Transform MuzzlePoint => muzzlePoint;

        /// <summary>
        /// Milestone 1P.5 - read-only presentation accessor for the CURRENT combat
        /// target's transform, so visual-facing systems (ToonSoldierPresentationAim)
        /// can aim the character without duplicating AcquireTarget logic. Null when no
        /// target is selected. Gameplay never reads this back.
        /// </summary>
        public Transform CurrentTargetTransform => _currentTarget != null ? _currentTarget.transform : null;

        private float _nextShotTime;
        private float _baseFireRate;
        private int _baseDamage;
        private bool _isOwnerDead;
        private bool _firingSuspended;
        private ZombieController _currentTarget;
        private Vector3 _aimDirection = Vector3.forward;
        private float _nextTargetRefreshTime;

        private void Awake()
        {
            // Scene reload recreates this component, naturally restoring authored defaults.
            _baseFireRate = fireRate;
            _baseDamage = damage;
            if (playerHealth == null)
            {
                playerHealth = GetComponentInParent<PlayerHealth>();
            }
        }

        private void OnEnable()
        {
            if (playerHealth != null)
            {
                playerHealth.Died += HandlePlayerDied;
                _isOwnerDead = playerHealth.IsDead;
            }

            // Fire on the first Update, then continue at the authored cadence.
            _nextShotTime = Time.time;
        }

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.Died -= HandlePlayerDied;
            }
        }

        /// <summary>
        /// Milestone 1K - permanently stops automatic fire for the current scene run.
        /// No new projectiles are created afterwards. Authored fire rate and damage are
        /// left untouched, and a scene reload restores normal firing.
        /// </summary>
        public void SuspendFiring()
        {
            _firingSuspended = true;
            _currentTarget = null;
            // No flash cleanup needed here since Milestone 1P: the muzzle flash is spawned
            // by the presentation layer in response to ShotFired, which simply stops being
            // raised. Any flash already on screen finishes its own sub-0.1s lifetime.
        }

        private void Update()
        {
            if (_firingSuspended || _isOwnerDead || muzzlePoint == null || projectilePrefab == null) return;
            RefreshTarget();
            if (_currentTarget == null || Time.time < _nextShotTime)
            {
                return;
            }

            AimAtTarget();
            FireForward();
            _nextShotTime = Time.time + (1f / fireRate);
        }

        private void RefreshTarget()
        {
            if (enemySpawner == null || Time.time < _nextTargetRefreshTime) return;
            _currentTarget = enemySpawner.AcquireTarget(transform.root, targetRange, _currentTarget);
            _nextTargetRefreshTime = Time.time + targetRefreshInterval;
        }

        private void AimAtTarget()
        {
            if (_currentTarget == null) return;
            Vector3 direction = _currentTarget.transform.position - muzzlePoint.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f) return;
            _aimDirection = direction.normalized;
            transform.rotation = Quaternion.LookRotation(_aimDirection, Vector3.up);
        }

        /// <summary>Applies a runtime-only firing-speed multiplier for the current scene run.</summary>
        public void ApplyFireRateMultiplier(float multiplier)
        {
            if (multiplier <= 0f) return;
            fireRate = Mathf.Max(0.01f, fireRate * multiplier);
        }

        /// <summary>Applies a runtime-only bonus to damage passed to newly fired projectiles.</summary>
        public void ApplyDamageBonus(int amount)
        {
            if (amount <= 0) return;
            damage = Mathf.Max(1, damage + amount);
        }

        private void HandlePlayerDied()
        {
            _isOwnerDead = true;
        }

        private void FireForward()
        {
            // World +Z is intentional for this milestone. Muzzle/player rotation is not
            // used as an aiming source, which prevents targeting logic from leaking in.
            Projectile projectile = Instantiate(
                projectilePrefab,
                muzzlePoint.position,
                Quaternion.identity);

            projectile.Initialize(
                _aimDirection,
                projectileSpeed,
                projectileLifetime,
                damage);

            // Cosmetic observers are notified last, once the shot is fully committed.
            ShotFired?.Invoke();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            fireRate = Mathf.Max(0.01f, fireRate);
            projectileSpeed = Mathf.Max(0.01f, projectileSpeed);
            projectileLifetime = Mathf.Max(0.01f, projectileLifetime);
            damage = Mathf.Max(1, damage);
        }
#endif
    }
}
