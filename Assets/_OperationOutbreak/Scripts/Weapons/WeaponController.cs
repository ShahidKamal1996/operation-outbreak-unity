using OperationOutbreak.Enemies;
using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Weapons
{
    /// <summary>
    /// Milestone 1C auto-fire weapon. While this component is active it repeatedly
    /// launches the configured projectile straight down the combat lane (+Z).
    /// Aiming and target selection deliberately do not belong here.
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

        private float _nextShotTime;
        private GameObject _muzzleFlash;
        private bool _isOwnerDead;
        private ZombieController _currentTarget;
        private Vector3 _aimDirection = Vector3.forward;
        private float _nextTargetRefreshTime;

        private void Awake()
        {
            if (playerHealth == null)
            {
                playerHealth = GetComponentInParent<PlayerHealth>();
            }
        }

        private void Start()
        {
            _muzzleFlash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _muzzleFlash.name = "MuzzleFlash";
            Destroy(_muzzleFlash.GetComponent<Collider>());
            _muzzleFlash.transform.SetParent(muzzlePoint, false);
            _muzzleFlash.transform.localPosition = Vector3.zero;
            _muzzleFlash.transform.localScale = Vector3.one * 0.18f;
            _muzzleFlash.GetComponent<Renderer>().material.color = new Color(1f, .85f, .25f, 1f);
            _muzzleFlash.SetActive(false);
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

        private void Update()
        {
            if (_isOwnerDead || muzzlePoint == null || projectilePrefab == null) return;
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

        private void HandlePlayerDied()
        {
            _isOwnerDead = true;
        }

        private System.Collections.IEnumerator FlashMuzzle()
        {
            _muzzleFlash.SetActive(true);
            yield return new WaitForSeconds(0.05f);
            if (_muzzleFlash != null) _muzzleFlash.SetActive(false);
        }

        private void FireForward()
        {
            // World +Z is intentional for this milestone. Muzzle/player rotation is not
            // used as an aiming source, which prevents targeting logic from leaking in.
            Projectile projectile = Instantiate(
                projectilePrefab,
                muzzlePoint.position,
                Quaternion.identity);

            if (_muzzleFlash != null) StartCoroutine(FlashMuzzle());
            projectile.Initialize(
                _aimDirection,
                projectileSpeed,
                projectileLifetime,
                damage);
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
