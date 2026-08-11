using System;
using OperationOutbreak.Player;
using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Enemies
{
    /// <summary>Direct, ground-plane enemy approach and contact attack for Milestone 1D.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class ZombieController : MonoBehaviour, IDamageable
    {
        [Header("Target")]
        [SerializeField] private Transform playerTarget;

        [Header("Movement")]
        [Min(0f)]
        [SerializeField] private float moveSpeed = 2.5f;
        [Min(0f)]
        [SerializeField] private float attackRange = 1.25f;

        [Header("Local Separation")]
        [Min(0.1f)]
        [SerializeField] private float separationRadius = 1.1f;
        [Min(0f)]
        [SerializeField] private float separationStrength = 1.5f;

        [Header("Attack")]
        [Min(1)]
        [SerializeField] private int attackDamage = 1;
        [Min(0.01f)]
        [SerializeField] private float attackInterval = 1f;

        [Header("Prototype Health")]
        [Min(1)]
        [SerializeField] private int maxHealth = 3;
        [SerializeField] private bool deactivateOnDefeat = true;

        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;
        public event Action<ZombieController> Died;
        private bool _deathNotified;

        private PlayerHealth _playerHealth;
        private float _nextAttackTime;
        private float _groundY;
        // Allocated once per zombie; OverlapSphereNonAlloc keeps the chase loop allocation-free.
        private readonly Collider[] _nearbyColliders = new Collider[12];

        private void Awake()
        {
            _groundY = transform.position.y;
            ResolvePlayerHealth();
        }

        private void OnEnable()
        {
            CurrentHealth = Mathf.Max(1, maxHealth);
            _deathNotified = false;
            _nextAttackTime = Time.time;
            ResolvePlayerHealth();
        }

        /// <summary>Called once by EnemySpawner with the actual Player root and its health component.</summary>
        public void SetTarget(Transform target, PlayerHealth health)
        {
            playerTarget = target;
            _playerHealth = health;

            // Retain a small fallback only for manual scene testing of the prefab.
            if (_playerHealth == null)
            {
                ResolvePlayerHealth();
            }
        }

        private void Update()
        {
            if (!IsAlive || playerTarget == null)
            {
                return;
            }

            Vector3 offset = playerTarget.position - transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            bool inAttackRange = distance <= attackRange;

            Vector3 chaseDirection = !inAttackRange && distance > 0.001f
                ? offset / distance
                : Vector3.zero;
            Vector3 separation = CalculateSeparation();
            Vector3 movement = (chaseDirection * moveSpeed) + (separation * separationStrength);

            // Separation is allowed to spread a cluster while attacking, but never lets
            // a zombie exceed its existing authored chase speed.
            movement = Vector3.ClampMagnitude(movement, moveSpeed);
            if (movement.sqrMagnitude > 0.0001f)
            {
                transform.position += movement * Time.deltaTime;
                transform.position = new Vector3(transform.position.x, _groundY, transform.position.z);
                if (!inAttackRange && chaseDirection.sqrMagnitude > 0f)
                {
                    transform.rotation = Quaternion.LookRotation(chaseDirection, Vector3.up);
                }
            }

            if (inAttackRange && Time.time >= _nextAttackTime && _playerHealth != null && _playerHealth.IsAlive)
            {
                _playerHealth.TakeDamage(attackDamage);
                _nextAttackTime = Time.time + attackInterval;
            }
        }

        private Vector3 CalculateSeparation()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, separationRadius, _nearbyColliders,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Vector3 separation = Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                Collider neighbourCollider = _nearbyColliders[i];
                if (neighbourCollider == null) continue;
                ZombieController neighbour = neighbourCollider.GetComponentInParent<ZombieController>();
                if (neighbour == null || neighbour == this || !neighbour.IsAlive) continue;

                Vector3 away = transform.position - neighbour.transform.position;
                away.y = 0f;
                float sqrDistance = away.sqrMagnitude;
                if (sqrDistance > 0.0001f)
                {
                    // Strongest when bodies are close, fading to zero at the radius edge.
                    separation += away.normalized * (1f - Mathf.Sqrt(sqrDistance) / separationRadius);
                }
            }

            return separation.sqrMagnitude > 1f ? separation.normalized : separation;
        }

        public void TakeDamage(int amount)
        {
            if (!IsAlive || amount <= 0)
            {
                return;
            }

            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            if (CurrentHealth == 0)
            {
                if (!_deathNotified)
                {
                    _deathNotified = true;
                    Died?.Invoke(this);
                }

                if (deactivateOnDefeat)
                {
                    gameObject.SetActive(false);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }

        private void ResolvePlayerHealth()
        {
            _playerHealth = playerTarget != null ? playerTarget.GetComponent<PlayerHealth>() : null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            moveSpeed = Mathf.Max(0f, moveSpeed);
            attackRange = Mathf.Max(0f, attackRange);
            separationRadius = Mathf.Max(0.1f, separationRadius);
            separationStrength = Mathf.Max(0f, separationStrength);
            attackDamage = Mathf.Max(1, attackDamage);
            attackInterval = Mathf.Max(0.01f, attackInterval);
            maxHealth = Mathf.Max(1, maxHealth);
        }
#endif
    }
}
