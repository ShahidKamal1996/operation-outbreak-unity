using OperationOutbreak.Player;
using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Upgrades
{
    /// <summary>
    /// Milestone 1L-R - the ONLY place that turns an <see cref="UpgradeDefinition"/> into a
    /// gameplay change. Every branch delegates to an existing approved runtime hook:
    ///
    ///   FireRateMultiplier  -> WeaponController.ApplyFireRateMultiplier
    ///   DamageBonus         -> WeaponController.ApplyDamageBonus
    ///   MaxHealthBonus      -> PlayerHealth.ApplyMaxHealthBonus
    ///   MoveSpeedMultiplier -> PlayerController.ApplyMoveSpeedMultiplier
    ///
    /// No upgrade maths lives here and no parallel player stats are introduced, so the
    /// pickup system can never drift from the systems it upgrades. This is a plain C#
    /// class - not a MonoBehaviour - so the effect layer stays independent of the pickup
    /// presentation and of the progression director.
    /// </summary>
    public sealed class UpgradeApplier
    {
        private readonly WeaponController _weapon;
        private readonly PlayerHealth _playerHealth;
        private readonly PlayerController _playerController;

        public UpgradeApplier(WeaponController weapon, PlayerHealth playerHealth, PlayerController playerController)
        {
            _weapon = weapon;
            _playerHealth = playerHealth;
            _playerController = playerController;
        }

        /// <summary>
        /// Applies one upgrade exactly once. Returns false when the required target is
        /// missing, so the caller can log instead of silently doing nothing.
        /// </summary>
        public bool Apply(UpgradeDefinition definition, Object context = null)
        {
            if (definition == null)
            {
                return false;
            }

            switch (definition.kind)
            {
                case UpgradeKind.FireRateMultiplier:
                    if (_weapon == null)
                    {
                        Debug.LogWarning("UpgradeApplier: no WeaponController, FIRE RATE not applied.", context);
                        return false;
                    }

                    _weapon.ApplyFireRateMultiplier(definition.multiplier);
                    return true;

                case UpgradeKind.DamageBonus:
                    if (_weapon == null)
                    {
                        Debug.LogWarning("UpgradeApplier: no WeaponController, DAMAGE not applied.", context);
                        return false;
                    }

                    _weapon.ApplyDamageBonus(definition.amount);
                    return true;

                case UpgradeKind.MaxHealthBonus:
                    if (_playerHealth == null)
                    {
                        Debug.LogWarning("UpgradeApplier: no PlayerHealth, MAX HEALTH not applied.", context);
                        return false;
                    }

                    // Raises max AND current, so a full 10/10 player becomes 12/12 and the
                    // existing event-driven Health HUD redraws itself.
                    _playerHealth.ApplyMaxHealthBonus(definition.amount);
                    return true;

                case UpgradeKind.MoveSpeedMultiplier:
                    if (_playerController == null)
                    {
                        Debug.LogWarning("UpgradeApplier: no PlayerController, MOVE SPEED not applied.", context);
                        return false;
                    }

                    _playerController.ApplyMoveSpeedMultiplier(definition.multiplier);
                    return true;

                default:
                    Debug.LogWarning($"UpgradeApplier: unhandled upgrade kind {definition.kind}.", context);
                    return false;
            }
        }
    }
}
