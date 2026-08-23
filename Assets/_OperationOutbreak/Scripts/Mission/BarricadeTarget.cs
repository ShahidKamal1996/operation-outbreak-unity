using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X.5 - a destroyable world-space objective target (a barricade) for the
    /// DestroyTargets objective. Implements <see cref="IDamageable"/> so the EXISTING projectile
    /// damage path damages it automatically: Projectile.SphereCast hits the barricade's collider,
    /// FindDamageable walks up to this component, and TakeDamage is called. No weapon, targeting
    /// or projectile change is required - the player's auto-fire hits the barricade because it is
    /// placed in the lane ahead of the enemies, in the projectile's path.
    ///
    /// On destruction it raises <see cref="MissionObjectiveTargetEvents.RaiseTargetDestroyed"/>,
    /// which the single objective authority (MissionObjectiveController) routes into the
    /// DestroyTargets runtime. A barricade can only count once (latched) and reads as visually
    /// distinct from enemies (a tall broad barricade slab, not an infected silhouette).
    ///
    /// This is the minimum reusable barricade foundation; a later dedicated milestone (1Y+) can
    /// extend it (weak points, armour, multi-stage destruction) without replacing the contract.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class BarricadeTarget : MonoBehaviour, IDamageable
    {
        [Tooltip("Damageable target id raised on destruction (routed to the Destroy objective).")]
        [SerializeField] private string targetId = "barricade";

        [Tooltip("Damage required to destroy this barricade. Set by the spawner from mission data.")]
        [SerializeField] private int maxHealth = 6;

        [Tooltip("Optional visible body whose colour tint reflects the barricade (set by spawner).")]
        [SerializeField] private MeshRenderer body;

        private bool _destroyed;

        /// <summary>The target id raised on destruction.</summary>
        public string TargetId => targetId;

        public bool IsAlive => !_destroyed;

        private void OnEnable()
        {
            if (!_destroyed)
            {
                ObjectiveTargetAimProvider.RegisterDamageable();
            }
        }

        private void OnDisable()
        {
            ObjectiveTargetAimProvider.UnregisterDamageable();
        }

        /// <summary>Configures the barricade from mission/spawner data before it is used.</summary>
        public void Configure(string id, int health, MeshRenderer visibleBody)
        {
            if (!string.IsNullOrEmpty(id))
            {
                targetId = id;
            }

            maxHealth = Mathf.Max(1, health);
            CurrentHealth = maxHealth;
            body = visibleBody;
        }

        /// <summary>Current health (read by tests / future UI).</summary>
        public int CurrentHealth { get; private set; }

        private void Awake()
        {
            if (CurrentHealth <= 0)
            {
                CurrentHealth = Mathf.Max(1, maxHealth);
            }

            if (body == null)
            {
                body = GetComponentInChildren<MeshRenderer>();
            }
        }

        public void TakeDamage(int amount)
        {
            if (_destroyed || amount <= 0)
            {
                return;
            }

            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);

            if (CurrentHealth <= 0)
            {
                Destroy();
            }
        }

        private void Destroy()
        {
            if (_destroyed)
            {
                return;
            }

            _destroyed = true;
            ObjectiveTargetAimProvider.UnregisterDamageable();
            MissionObjectiveTargetEvents.RaiseTargetDestroyed(targetId);

            // Remove the collider/body so the destroyed barricade no longer blocks shots or
            // movement and reads as gone; the GameObject itself is deactivated (pooled-style).
            GetComponent<Collider>().enabled = false;
            if (body != null)
            {
                body.enabled = false;
            }
        }
    }
}
