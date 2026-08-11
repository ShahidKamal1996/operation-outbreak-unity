using System;
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
        public int MaxHealth => maxHealth;
        public bool IsAlive => !_isDead;
        public bool IsDead => _isDead;

        /// <summary>Raised once when health first reaches zero.</summary>
        public event Action Died;

        /// <summary>Raised after a confirmed health value change.</summary>
        public event Action<int, int> HealthChanged;

        private bool _isDead;

        private void OnEnable()
        {
            CurrentHealth = Mathf.Max(1, maxHealth);
            _isDead = false;
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        public void TakeDamage(int amount)
        {
            if (!IsAlive || amount <= 0)
            {
                return;
            }

            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (logDamageToConsole)
            {
                Debug.Log($"Player damaged: {CurrentHealth} / {maxHealth}", this);

            }
#endif

            if (CurrentHealth == 0)
            {
                _isDead = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (logDamageToConsole)
                {
                    Debug.Log("Player health reached 0", this);
                    Debug.Log("Player death state activated", this);
                }
#endif
                Died?.Invoke();
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
