using UnityEngine;

namespace OperationOutbreak.Weapons
{
    /// <summary>
    /// Stationary Milestone 1C test target. This is intentionally not an enemy: it has
    /// only prototype health and deactivation, with no movement, AI or targeting logic.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class TargetDummy : MonoBehaviour, IDamageable
    {
        [Header("Prototype Health")]
        [Min(1)]
        [SerializeField] private int maxHealth = 5;

        [Tooltip("Disable the target at zero health so it can be reset by re-enabling it.")]
        [SerializeField] private bool deactivateOnDefeat = true;

        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;

        private void OnEnable()
        {
            CurrentHealth = Mathf.Max(1, maxHealth);
        }

        public void TakeDamage(int amount)
        {
            if (!IsAlive || amount <= 0)
            {
                return;
            }

            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);

            if (CurrentHealth > 0)
            {
                return;
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
        }
#endif
    }
}
