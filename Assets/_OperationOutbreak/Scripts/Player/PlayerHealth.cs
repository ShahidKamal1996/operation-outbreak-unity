using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Player
{
    /// <summary>Minimal internal player health for the Milestone 1D enemy encounter.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHealth : MonoBehaviour, IDamageable
    {
        [Header("Prototype Health")]
        [Min(1)]
        [SerializeField] private int maxHealth = 10;

        [Header("Debug Verification")]
        [Tooltip("Logs only confirmed damage events in the Editor or a development build. No HUD is created.")]
        [SerializeField] private bool logDamageToConsole = true;

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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (logDamageToConsole)
            {
                Debug.Log($"Player damaged: {CurrentHealth} / {maxHealth}", this);

                if (CurrentHealth == 0)
                {
                    Debug.Log("Player health reached 0", this);
                }
            }
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
        }
#endif
    }
}
