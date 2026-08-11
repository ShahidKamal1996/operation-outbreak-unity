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

        private PlayerHealth _playerHealth;
        private float _nextAttackTime;
        private float _groundY;

        private void Awake()
        {
            _groundY = transform.position.y;
            ResolvePlayerHealth();
        }

        private void OnEnable()
        {
            CurrentHealth = Mathf.Max(1, maxHealth);
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

            if (distance > attackRange)
            {
                Vector3 direction = offset / distance;
                transform.position += direction * (moveSpeed * Time.deltaTime);
                transform.position = new Vector3(transform.position.x, _groundY, transform.position.z);
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                return;
            }

            if (Time.time >= _nextAttackTime && _playerHealth != null && _playerHealth.IsAlive)
            {
                _playerHealth.TakeDamage(attackDamage);
                _nextAttackTime = Time.time + attackInterval;
            }
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
            attackDamage = Mathf.Max(1, attackDamage);
            attackInterval = Mathf.Max(0.01f, attackInterval);
            maxHealth = Mathf.Max(1, maxHealth);
        }
#endif
    }
}
