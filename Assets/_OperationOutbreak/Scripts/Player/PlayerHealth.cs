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

        /// <summary>
        /// Milestone 1O.5 - raised ONLY when confirmed incoming damage is applied while the
        /// player survives it. Deliberately separate from HealthChanged, which also fires on
        /// Max Health upgrades and on the initial OnEnable seed: a cosmetic hit reaction must
        /// never be triggered by picking up an upgrade or by healing. It is also not raised
        /// for the killing blow, so a hit reaction can never interrupt or override Death.
        /// </summary>
        public event Action Damaged;

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

            if (CurrentHealth > 0)
            {
                // Survived the hit: cosmetic observers may react. The killing blow
                // intentionally skips this so Death is never contested.
                Damaged?.Invoke();
            }

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

        /// <summary>
        /// Milestone 1L - runtime-only maximum health upgrade (MAX HEALTH +2 gate).
        ///
        /// Raises the ceiling AND the current value by the same amount, so a full
        /// 10/10 player becomes 12/12 rather than 10/12. HealthChanged is raised once
        /// afterwards, which is all the existing event-driven Health HUD needs to
        /// redraw - no HUD code or styling is touched.
        ///
        /// RESET: maxHealth is an ordinary serialized field on a scene component and
        /// OnEnable re-seeds CurrentHealth from it, so reloading the scene restores the
        /// authored 10. Nothing static and nothing written to an asset.
        /// </summary>
        public void ApplyMaxHealthBonus(int amount)
        {
            if (amount <= 0 || _isDead)
            {
                return;
            }

            maxHealth += amount;
            CurrentHealth += amount;
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
        }
#endif
    }
}
