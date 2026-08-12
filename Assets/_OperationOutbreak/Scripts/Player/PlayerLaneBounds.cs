using UnityEngine;

namespace OperationOutbreak.Player
{
    /// <summary>
    /// Milestone 1B - resolves the playable rectangle of the combat corridor from the
    /// existing Milestone 1A scene references instead of hard-coding world values.
    ///
    ///   Side limits    : derived from the CombatLane width (minus the player's half width).
    ///   Rear limit     : PlayerSpawn.z - backwardAllowance.
    ///   Forward limit  : EnemyApproachReference.z - forwardStandoff.
    ///
    /// Sensible fallbacks are used if a reference is missing, so the component never
    /// throws and the player can never end up unbounded.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerLaneBounds : MonoBehaviour
    {
        [Header("Scene references (Milestone 1A environment)")]
        [Tooltip("Environment/CombatLane - supplies the corridor centre and width.")]
        [SerializeField] private Transform laneReference;

        [Tooltip("GameplayReferences/PlayerSpawn - defines the rear of the gameplay region.")]
        [SerializeField] private Transform rearReference;

        [Tooltip("GameplayReferences/EnemyApproachReference - the player must never reach it.")]
        [SerializeField] private Transform forwardReference;

        [Header("Player clearance")]
        [Tooltip("Half the player's visual width. Keeps the prototype off the side boundary walls.")]
        [Min(0f)]
        [SerializeField] private float playerHalfWidth = 0.5f;

        [Tooltip("Half the player's visual depth. Keeps the prototype inside the lane ends.")]
        [Min(0f)]
        [SerializeField] private float playerHalfDepth = 0.5f;

        [Tooltip("Optional cap on how far from the lane centre the player may strafe. " +
                 "The approved portrait camera only frames about +/-3.3 units at the player's " +
                 "depth, so the full 12-unit corridor is intentionally wider than the playable " +
                 "band. 0 = use the full lane width.")]
        [Min(0f)]
        [SerializeField] private float maxPlayableHalfWidth = 3.6f;

        [Header("Forward / back travel limits")]
        [Tooltip("How far behind PlayerSpawn the player may retreat (world units).")]
        [Min(0f)]
        [SerializeField] private float backwardAllowance = 3f;

        [Tooltip("How far short of EnemyApproachReference the player is stopped (world units).")]
        [Min(1f)]
        [SerializeField] private float forwardStandoff = 20f;

        [Header("Fallbacks (used only when a reference is missing)")]
        [SerializeField] private float fallbackLaneHalfWidth = 6f;
        [SerializeField] private float fallbackRearZ = -3f;
        [SerializeField] private float fallbackForwardZ = 15f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        public float MinX { get; private set; }
        public float MaxX { get; private set; }
        public float MinZ { get; private set; }
        public float MaxZ { get; private set; }

        // Milestone 1M - optional forward limit supplied by the mission section flow.
        // When no override is set the component behaves exactly as it did before:
        // forward limit = forwardReference.z - forwardStandoff.
        private bool _hasForwardOverride;
        private float _forwardOverrideZ;

        /// <summary>
        /// Milestone 1M - lets the mission progression open the corridor one section at a
        /// time. The lane geometry, lateral limits, rear limit and camera are untouched;
        /// only the forward stop line moves. Anything that reads MinZ/MaxZ (including the
        /// upgrade pickup placement) therefore sees the CURRENT reachable area, so a
        /// pickup can never appear in a section the player has not unlocked yet.
        /// </summary>
        public void SetForwardLimit(float worldZ)
        {
            _hasForwardOverride = true;
            _forwardOverrideZ = worldZ;
            Recalculate();
        }

        /// <summary>Drops the override and returns to the authored forward standoff.</summary>
        public void ClearForwardLimit()
        {
            _hasForwardOverride = false;
            Recalculate();
        }

        private void Awake()
        {
            Recalculate();
        }

        private void OnValidate()
        {
            Recalculate();
        }

        /// <summary>Recomputes the playable rectangle from the current scene references.</summary>
        public void Recalculate()
        {
            float laneCentreX = 0f;
            float laneHalfWidth = fallbackLaneHalfWidth;
            float laneMinZ = float.NegativeInfinity;
            float laneMaxZ = float.PositiveInfinity;

            if (laneReference != null)
            {
                Vector3 laneScale = laneReference.lossyScale;
                Vector3 lanePosition = laneReference.position;

                laneCentreX = lanePosition.x;
                laneHalfWidth = Mathf.Abs(laneScale.x) * 0.5f;
                laneMinZ = lanePosition.z - (Mathf.Abs(laneScale.z) * 0.5f);
                laneMaxZ = lanePosition.z + (Mathf.Abs(laneScale.z) * 0.5f);
            }

            float usableHalfWidth = Mathf.Max(0f, laneHalfWidth - playerHalfWidth);

            if (maxPlayableHalfWidth > 0f)
            {
                usableHalfWidth = Mathf.Min(usableHalfWidth, maxPlayableHalfWidth);
            }

            MinX = laneCentreX - usableHalfWidth;
            MaxX = laneCentreX + usableHalfWidth;

            float rearZ = rearReference != null
                ? rearReference.position.z - backwardAllowance
                : fallbackRearZ;

            float forwardZ = _hasForwardOverride
                ? _forwardOverrideZ
                : (forwardReference != null
                    ? forwardReference.position.z - forwardStandoff
                    : fallbackForwardZ);

            if (!float.IsInfinity(laneMinZ))
            {
                rearZ = Mathf.Max(rearZ, laneMinZ + playerHalfDepth);
                forwardZ = Mathf.Min(forwardZ, laneMaxZ - playerHalfDepth);
            }

            MinZ = Mathf.Min(rearZ, forwardZ);
            MaxZ = Mathf.Max(rearZ, forwardZ);
        }

        /// <summary>Clamps a world position into the playable corridor (Y is left untouched).</summary>
        public Vector3 Clamp(Vector3 worldPosition)
        {
            worldPosition.x = Mathf.Clamp(worldPosition.x, MinX, MaxX);
            worldPosition.z = Mathf.Clamp(worldPosition.z, MinZ, MaxZ);
            return worldPosition;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
            {
                return;
            }

            Recalculate();

            float y = transform.position.y - 1f;
            Vector3 a = new Vector3(MinX, y, MinZ);
            Vector3 b = new Vector3(MaxX, y, MinZ);
            Vector3 c = new Vector3(MaxX, y, MaxZ);
            Vector3 d = new Vector3(MinX, y, MaxZ);

            Gizmos.color = new Color(0.15f, 0.85f, 0.45f, 1f);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
#endif
    }
}
