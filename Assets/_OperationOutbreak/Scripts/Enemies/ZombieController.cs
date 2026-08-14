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

        /// <summary>
        /// Milestone 1O - read-only views of this enemy's authored stats, so the diagnostics
        /// layer can report "Runner speed 3.5 vs Basic 2.5" from the real values instead of
        /// duplicating them in a second table that could silently drift.
        ///
        /// These are getters only. Nothing can write a stat through them, so no balance
        /// change is possible and the serialized values remain the single source of truth.
        /// </summary>
        public float MoveSpeed => moveSpeed;

        /// <summary>Milestone 1O - read-only authored max health.</summary>
        public int MaxHealth => maxHealth;

        /// <summary>Milestone 1O - read-only authored contact damage.</summary>
        public int AttackDamage => attackDamage;

        /// <summary>
        /// Milestone 1O - raised after this enemy takes damage, carrying the amount.
        /// Diagnostics counts projectile hits with it. Purely a notification: it is raised
        /// after the health maths has already completed and no listener can alter it.
        /// </summary>
        public event Action<ZombieController, int> DamageTaken;

        /// <summary>
        /// Milestone 1P - visual-only hit punch curve for <see cref="HitReaction"/>.
        ///
        /// Pure sine pulse: exactly 1 at progress 0, peaks at 1 + 0.07 at the midpoint,
        /// back to exactly 1 at progress 1. The curve can never go below 1, so the enemy
        /// can never be made to shrink by hit feedback. It only ever scales the Visual
        /// child, never the authoritative transform, collider or navigation state.
        /// </summary>
        public static float ComputeHitPunchScale(float progress)
        {
            const float MaxPunch = 0.07f;
            float clampedProgress = Mathf.Clamp01(progress);
            return 1f + MaxPunch * Mathf.Sin(clampedProgress * Mathf.PI);
        }

        /// <summary>
        /// Milestone 1O - raised immediately after this enemy lands a hit on the player,
        /// carrying the damage dealt. Lets diagnostics report whether an archetype ever
        /// actually reached the player without polling anything.
        /// </summary>
        public event Action<ZombieController, int> DamagedPlayer;

        private bool _deathNotified;

        private PlayerHealth _playerHealth;
        private float _nextAttackTime;
        private float _groundY;
        private bool _isDying;
        private bool _combatSuspended;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _propertyBlock;
        private Vector3 _visualScale;
        // Allocated once per zombie; OverlapSphereNonAlloc keeps the chase loop allocation-free.
        private readonly Collider[] _nearbyColliders = new Collider[12];

        private void Awake()
        {
            _groundY = transform.position.y;
            _renderers = GetComponentsInChildren<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
            Transform visual = transform.Find("Visual");
            _visualScale = visual != null ? visual.localScale : Vector3.one;
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

        /// <summary>
        /// Milestone 1K - stops this zombie chasing and attacking after victory.
        /// The zombie is left in the scene untouched; only its behaviour halts, so no
        /// death feedback is triggered and nothing is destroyed.
        /// </summary>
        public void SuspendCombat()
        {
            _combatSuspended = true;
        }

        private void Update()
        {
            if (_combatSuspended || _isDying || !IsAlive || playerTarget == null)
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

                // Milestone 1O - notification only, raised after the damage has already
                // been dealt and the cooldown already scheduled, so observers cannot
                // influence combat timing or outcome.
                DamagedPlayer?.Invoke(this, attackDamage);
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

            // Milestone 1O - notification only; the health maths above is already complete.
            DamageTaken?.Invoke(this, amount);

            if (CurrentHealth > 0) StartCoroutine(HitReaction());
            if (CurrentHealth == 0)
            {
                if (!_deathNotified)
                {
                    _deathNotified = true;
                    Died?.Invoke(this);
                }

                _isDying = true;

                // Milestone 1P - clear any white hit flash still on the corpse so death
                // feedback starts from the authored material colours.
                SetFlashColor(Color.clear);
                StartCoroutine(DeathFeedback());
            }
        }

        /// <summary>
        /// Milestone 1D white flash, refined in 1P into one combined hit reaction: the
        /// existing short white material flash plus a tiny visual-only scale punch on the
        /// Visual child, driven by <see cref="ComputeHitPunchScale"/>. Both are
        /// presentation-only - the authoritative transform, collider, chase logic, hit
        /// detection and attack range are never touched.
        ///
        /// Death safety: every frame checks _isDying, so a reaction that overlaps the
        /// moment of death stops immediately, clears its flash and never writes the visual
        /// scale again. DeathFeedback takes over the Visual scale from the authored base
        /// scale, exactly as it did before 1P.
        /// </summary>
        private System.Collections.IEnumerator HitReaction()
        {
            Transform visual = transform.Find("Visual");
            SetFlashColor(Color.white);

            float elapsed = 0f;
            const float reactionDuration = 0.12f;

            while (elapsed < reactionDuration)
            {
                if (_isDying)
                {
                    SetFlashColor(Color.clear);
                    yield break;
                }

                elapsed += Time.deltaTime;

                if (visual != null)
                {
                    visual.localScale = _visualScale * ComputeHitPunchScale(elapsed / reactionDuration);
                }

                yield return null;
            }

            SetFlashColor(Color.clear);

            if (visual != null)
            {
                visual.localScale = _visualScale;
            }
        }

        private void SetFlashColor(Color color)
        {
            foreach (Renderer renderer in _renderers)
            {
                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor("_BaseColor", color == Color.clear ? renderer.sharedMaterial.color : color);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private System.Collections.IEnumerator DeathFeedback()
        {
            Transform visual = transform.Find("Visual");
            float elapsed = 0f;
            const float deathDuration = 0.38f;
            while (elapsed < deathDuration)
            {
                elapsed += Time.deltaTime;
                if (visual != null)
                {
                    float progress = elapsed / deathDuration;
                    visual.localScale = Vector3.Lerp(_visualScale, _visualScale * 0.12f, progress);
                    visual.localRotation = Quaternion.Euler(progress * 55f, 0f, 0f);
                }
                yield return null;
            }
            if (deactivateOnDefeat) gameObject.SetActive(false); else Destroy(gameObject);
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
