using OperationOutbreak.Feedback;
using UnityEngine;
using UnityEngine.Rendering;

namespace OperationOutbreak.Weapons
{
    /// <summary>
    /// Visible prototype projectile with swept collision detection. Configuration is
    /// supplied by WeaponController at spawn time so one prefab can support future weapons.
    /// Despawn behaviour is kept in one method for a straightforward later pooling swap.
    ///
    /// Milestone 1P - readability only: a short, thin TrailRenderer is attached at runtime
    /// so the projectile reads clearly from the portrait gameplay camera. The trail is
    /// mobile-conscious (sub-0.15s length, tiny width, no shadows, shared material) and is
    /// pure presentation: it has no collider, adds no forces, and takes no part in the
    /// swept hit test. Movement, damage, lifetime and collision behaviour are unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class Projectile : MonoBehaviour
    {
        // Trail tuning. Kept deliberately tiny for mobile: short lifetime, thin width,
        // minimum vertex distance so a fast projectile never allocates a long vertex strip.
        public const float DefaultTrailTime = 0.12f;
        public const float DefaultTrailStartWidth = 0.07f;
        public const float DefaultTrailEndWidth = 0.005f;
        public const float DefaultTrailMinVertexDistance = 0.04f;

        private Vector3 _direction = Vector3.forward;
        private float _speed = 25f;
        private float _lifetime = 3f;
        private int _damage = 1;
        private float _age;
        private bool _isLive;
        private SphereCollider _collisionShape;

        /// <summary>Milestone 1P - read-only view of the direction set by the last Initialize.</summary>
        public Vector3 Direction => _direction;

        /// <summary>Milestone 1P - read-only view of the speed set by the last Initialize.</summary>
        public float Speed => _speed;

        /// <summary>Milestone 1P - read-only view of the lifetime set by the last Initialize.</summary>
        public float Lifetime => _lifetime;

        /// <summary>Milestone 1P - read-only view of the damage set by the last Initialize.</summary>
        public int Damage => _damage;

        private void Awake()
        {
            _collisionShape = GetComponent<SphereCollider>();
            EnsureTrailPresentation();
        }

        private void OnEnable()
        {
            _age = 0f;
            _isLive = true;
        }

        /// <summary>
        /// Applies the firing weapon's per-shot configuration. Values are clamped so a
        /// misconfigured weapon can never produce a zero-life projectile or a negative
        /// damage value, regardless of how the fields are tuned in the Inspector.
        /// </summary>
        public void Initialize(Vector3 direction, float speed, float lifetime, int damage)
        {
            _direction = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.forward;
            _speed = Mathf.Max(0f, speed);
            _lifetime = Mathf.Max(0.01f, lifetime);
            _damage = Mathf.Max(1, damage);
            _age = 0f;
            _isLive = true;
        }

        /// <summary>
        /// Milestone 1P - attaches the readability trail once. Idempotent, so it can never
        /// stack a second trail on a projectile that already carries one. Uses the shared
        /// CombatFeedback trail material, so no material is instantiated per shot.
        /// </summary>
        public void EnsureTrailPresentation()
        {
            if (GetComponent<TrailRenderer>() != null)
            {
                return;
            }

            TrailRenderer trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = DefaultTrailTime;
            trail.startWidth = DefaultTrailStartWidth;
            trail.endWidth = DefaultTrailEndWidth;
            trail.minVertexDistance = DefaultTrailMinVertexDistance;
            trail.autodestruct = false;
            trail.emitting = true;
            trail.sharedMaterial = CombatFeedback.SharedTrailMaterial;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
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
