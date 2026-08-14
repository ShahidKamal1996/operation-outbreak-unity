using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Feedback
{
    /// <summary>
    /// Milestone 1P - reusable muzzle flash listener. Previously the muzzle flash was
    /// created, toggled and timed inside WeaponController itself; it now lives here, in the
    /// feedback layer, wired to the weapon's existing ShotFired event.
    ///
    /// DIRECTION OF CONTROL IS STRICTLY ONE-WAY: gameplay -> presentation. WeaponController
    /// raises ShotFired only AFTER a shot has been fully committed (projectile spawned and
    /// initialised), so this component cannot influence fire timing, targeting, damage or
    /// cadence. It never writes anything back into gameplay. Removing it leaves the weapon
    /// fully functional.
    ///
    /// Replacement path for production VFX: swap the CombatFeedback.SpawnMuzzleFlash call
    /// (or this whole component) without touching WeaponController or Projectile.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MuzzleFlashFeedback : MonoBehaviour
    {
        [Header("Gameplay Source (read-only observer)")]
        [Tooltip("Weapon authority. Only its ShotFired event is observed.")]
        [SerializeField] private WeaponController weapon;

        [Header("Flash Anchor")]
        [Tooltip("Where the flash appears. Falls back to the weapon's authored muzzle point.")]
        [SerializeField] private Transform flashAnchor;

        [Header("Flash Tuning (visual only)")]
        [Tooltip("How long the flash stays visible. Clamped to a short, readable range so a " +
                 "mis-tuned value can never leave a lingering flash during rapid auto-fire.")]
        [Min(0.01f)]
        [SerializeField] private float flashDuration = 0.09f;

        /// <summary>Lower bound enforced by <see cref="ClampFlashDuration"/>.</summary>
        public const float MinimumFlashDuration = 0.02f;

        /// <summary>Upper bound enforced by <see cref="ClampFlashDuration"/>.</summary>
        public const float MaximumFlashDuration = 0.25f;

        /// <summary>
        /// Pure clamp so the authored duration stays inside the short, readable range no
        /// matter what is typed into the Inspector or a future tunable.
        /// </summary>
        public static float ClampFlashDuration(float duration)
        {
            return Mathf.Clamp(duration, MinimumFlashDuration, MaximumFlashDuration);
        }

        private void Reset()
        {
            weapon = GetComponent<WeaponController>();
        }

        private void Awake()
        {
            if (weapon == null)
            {
                weapon = GetComponent<WeaponController>();
            }

            if (flashAnchor == null && weapon != null)
            {
                flashAnchor = weapon.MuzzlePoint;
            }
        }

        private void OnEnable()
        {
            if (weapon != null)
            {
                weapon.ShotFired += HandleShotFired;
            }
        }

        private void OnDisable()
        {
            if (weapon != null)
            {
                weapon.ShotFired -= HandleShotFired;
            }
        }

        private void HandleShotFired()
        {
            Transform anchor = flashAnchor != null
                ? flashAnchor
                : weapon != null ? weapon.MuzzlePoint : null;

            if (anchor == null)
            {
                return;
            }

            CombatFeedback.SpawnMuzzleFlash(
                anchor.position,
                anchor.forward,
                ClampFlashDuration(flashDuration));
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            flashDuration = ClampFlashDuration(flashDuration);
        }
#endif
    }
}
