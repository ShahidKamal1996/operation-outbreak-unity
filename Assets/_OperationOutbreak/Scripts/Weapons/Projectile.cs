using OperationOutbreak.Feedback;
using UnityEngine;

namespace OperationOutbreak.Weapons
{
    /// <summary>
    /// Visible prototype projectile with swept collision detection. Configuration is
    /// supplied by WeaponController at spawn time so one prefab can support future weapons.
    /// Despawn behaviour is kept in one method for a straightforward later pooling swap.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class Projectile : MonoBehaviour
    {
        private Vector3 _direction = Vector3.forward;
        private float _speed = 25f;
        private float _lifetime = 3f;
        private int _damage = 1;
        private float _age;
        private bool _isLive;
        private SphereCollider _collisionShape;

        private void Awake()
        {
            _collisionShape = GetComponent<SphereCollider>();
        }

        private void OnEnable()
        {
            _age = 0f;
            _isLive = true;
        }

        /// <summary>Applies the firing weapon's per-shot configuration.</summary>
        public void Initialize(Vector3 direction, float speed, float lifetime, int damage)
        {
            _direction = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.forward;
            _speed = Mathf.Max(0f, speed);
            _lifetime = Mathf.Max(0.01f, lifetime);
            _damage = Mathf.Max(1, damage);
            _age = 0f;
            _isLive = true;
        }

        private void Update()
        {
            if (!_isLive)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            float travelDistance = _speed * deltaTime;
            Vector3 start = transform.position;

            // Sweep the projectile's collider radius across the complete frame step.
            // This remains reliable if speed or frame time increases beyond the prototype defaults.
            if (travelDistance > 0f && Physics.SphereCast(
                    start,
                    GetWorldCollisionRadius(),
                    _direction,
                    out RaycastHit hit,
                    travelDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                IDamageable damageable = FindDamageable(hit.collider);

                if (damageable != null && damageable.IsAlive)
                {
                    damageable.TakeDamage(_damage);
                    CombatFeedback.SpawnHitSpark(hit.point);
                    Despawn();
                    return;
                }
            }

            transform.position = start + (_direction * travelDistance);
            _age += deltaTime;

            if (_age >= _lifetime)
            {
                Despawn();
            }
        }

        private float GetWorldCollisionRadius()
        {
            if (_collisionShape == null)
            {
                return 0.1f;
            }

            Vector3 scale = transform.lossyScale;
            float largestAxis = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            return Mathf.Max(0.001f, _collisionShape.radius * largestAxis);
        }

        private static IDamageable FindDamageable(Collider hitCollider)
        {
            // MonoBehaviour lookup permits a lightweight interface without coupling the
            // projectile to TargetDummy or any future concrete enemy type.
            MonoBehaviour[] behaviours = hitCollider.GetComponentsInParent<MonoBehaviour>();

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IDamageable damageable)
                {
                    return damageable;
                }
            }

            return null;
        }

        private void Despawn()
        {
            if (!_isLive)
            {
                return;
            }

            _isLive = false;

            // This is the only destruction point. A later object-pool milestone can
            // replace it with a release call without changing movement or hit logic.
            Destroy(gameObject);
        }
    }
}
