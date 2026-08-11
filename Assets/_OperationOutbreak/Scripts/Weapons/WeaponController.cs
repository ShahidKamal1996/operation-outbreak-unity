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

        private void OnEnable()
        {
            // Fire on the first Update, then continue at the authored cadence.
            _nextShotTime = Time.time;
        }

        private void Update()
        {
            if (muzzlePoint == null || projectilePrefab == null || Time.time < _nextShotTime)
            {
                return;
            }

            FireForward();
            _nextShotTime = Time.time + (1f / fireRate);
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
                Vector3.forward,
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
