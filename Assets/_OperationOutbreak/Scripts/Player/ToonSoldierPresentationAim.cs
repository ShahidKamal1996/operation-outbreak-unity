using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Player
{
    /// <summary>
    /// Milestone 1P.5 QA fix #2 - PRESENTATION-ONLY aim facing for the Toon Soldier.
    ///
    /// Manual QA found the soldier kept facing forward while gameplay auto-targeting
    /// fired at enemies to the left and right. The targeting system itself is VERIFIED
    /// and untouched; this component only turns the visual.
    ///
    /// CONTRACT:
    ///   - It reads ONE existing gameplay value: WeaponController's current target,
    ///     through the read-only <see cref="WeaponController.CurrentTargetTransform"/>
    ///     accessor added for this milestone. No AcquireTarget logic is duplicated here.
    ///   - It writes ONLY the yaw of its own presentation pivot (ToonSoldierVisual).
    ///     The Player gameplay root is never rotated, so movement, lane mechanics and
    ///     collision are untouched. Yaw only, no pitch/roll.
    ///   - Smoothing via a clamped per-second turn speed plus a snap epsilon prevents
    ///     180-degree flips and jitter when targets switch.
    ///   - With no valid target the pivot eases back to the forward pose (yaw 0).
    ///
    /// Removing this component leaves the game fully playable, exactly like removing
    /// the animation bridge.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToonSoldierPresentationAim : MonoBehaviour
    {
        [Header("Gameplay Source (read-only observer)")]
        [Tooltip("Weapon authority. Only its CurrentTargetTransform is read.")]
        [SerializeField] private WeaponController weaponController;

        [Header("Presentation Pivot")]
        [Tooltip("The visual layer this component may rotate (yaw only). Should be " +
                 "ToonSoldierVisual - NOT the Player root.")]
        [SerializeField] private Transform presentationPivot;

        [Header("Tuning (visual only)")]
        [Tooltip("Maximum yaw change per second, in degrees.")]
        [Min(1f)]
        [SerializeField] private float turnSpeedDegrees = 270f;

        [Tooltip("Angular distance below which the pivot snaps straight to the desired yaw.")]
        [Min(0f)]
        [SerializeField] private float snapEpsilonDegrees = 0.5f;

        /// <summary>
        /// Pure helper: yaw in degrees from <paramref name="fromPosition"/> toward
        /// <paramref name="toPosition"/>, in the horizontal plane. 0 = world forward
        /// (+Z), positive = right, negative = left. Static and side-effect free so
        /// EditMode tests can pin left/right/forward cases exactly.
        /// </summary>
        public static float ComputePlanarYaw(Vector3 fromPosition, Vector3 toPosition)
        {
            Vector3 delta = toPosition - fromPosition;
            return Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Pure helper: one step of smoothed yaw turning. Moves <paramref name="currentYaw"/>
        /// toward <paramref name="desiredYaw"/> by at most <paramref name="maxDeltaDegrees"/>
        /// (shortest path, so 179 -> -179 turns left, never 358 degrees right), then snaps
        /// when the remaining distance is at or below the epsilon.
        /// </summary>
        public static float TurnToward(
            float currentYaw, float desiredYaw, float maxDeltaDegrees, float snapEpsilonDegrees)
        {
            float remaining = Mathf.DeltaAngle(currentYaw, desiredYaw);

            if (Mathf.Abs(remaining) <= Mathf.Max(0f, snapEpsilonDegrees))
            {
                return desiredYaw;
            }

            float clamped = Mathf.Clamp(remaining, -Mathf.Max(0f, maxDeltaDegrees), Mathf.Max(0f, maxDeltaDegrees));
            return currentYaw + clamped;
        }

        private void Reset()
        {
            weaponController = GetComponentInChildren<WeaponController>(true);
            presentationPivot = transform.Find("ToonSoldierVisual");
        }

        private void Awake()
        {
            // One-time fallback resolution, never a per-frame search.
            if (weaponController == null)
            {
                weaponController = GetComponentInChildren<WeaponController>(true);
            }

            if (presentationPivot == null)
            {
                presentationPivot = transform.Find("ToonSoldierVisual");
            }
        }

        private void LateUpdate()
        {
            // LateUpdate runs after the weapon has updated its current target.
            Transform target = weaponController != null ? weaponController.CurrentTargetTransform : null;
            ApplyAim(target, Time.deltaTime);
        }

        /// <summary>
        /// Applies the smoothed yaw toward <paramref name="target"/> (or back to forward
        /// when null). Public because EditMode tests drive it directly with an explicit
        /// delta time; LateUpdate is the only runtime caller. The Player root is never
        /// written to.
        /// </summary>
        public void ApplyAim(Transform target, float deltaTime)
        {
            if (presentationPivot == null)
            {
                return;
            }

            float desiredYaw = target != null
                ? ComputePlanarYaw(presentationPivot.position, target.position)
                : 0f;

            float currentYaw = presentationPivot.localEulerAngles.y;
            float maxDelta = Mathf.Max(1f, turnSpeedDegrees) * Mathf.Max(0f, deltaTime);
            float newYaw = TurnToward(currentYaw, desiredYaw, maxDelta, snapEpsilonDegrees);

            // Yaw only. X/Z rotation of the pivot is left exactly as authored.
            presentationPivot.localRotation = Quaternion.Euler(0f, newYaw, 0f);
        }
    }
}
