using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Micro task #4 — polished cinematic follow camera for the helicopter.
    ///
    /// Attach to the EXISTING camera in Helicopter_Cinematic. This creates no cameras, disables no
    /// cameras, and changes no camera settings (FOV, clipping, culling are left alone) — it only
    /// moves and aims the transform it lives on.
    ///
    /// CINEMATIC REAR THREE-QUARTER COMPOSITION
    /// ----------------------------------------
    /// Derives a stable target-relative coordinate basis from the verified target axes:
    ///     forward = target.TransformDirection(targetForwardAxis).normalized;
    ///     up      = target.TransformDirection(targetUpAxis).normalized;
    ///     right   = Vector3.Cross(up, forward).normalized;
    ///
    /// Places the camera behind, above, and slightly to the side of the helicopter:
    ///     desiredPos = target.position - forward * followDistance + up * heightOffset + right * sideOffset;
    ///
    /// Aims at a tight focus point on the helicopter body rather than far ahead:
    ///     lookTarget = target.position + forward * lookAheadDistance + up * lookHeight;
    ///
    /// When <see cref="stableRearThreeQuarter"/> is enabled, the camera calculates look rotation
    /// directly from its actual position toward the focus point, guaranteeing the helicopter
    /// remains consistently framed and preventing any drift toward a front/diagonal angle.
    ///
    /// Runs in LateUpdate so HelicopterFlightRoot translation and rise have already executed.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Helicopter Camera Follow")]
    public sealed class CinematicHelicopterCameraFollow : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("What to follow. Assign HelicopterFlightRoot (authoritative flight parent), NOT the child model.")]
        [SerializeField] private Transform target;

        [Header("Offsets (relative to target orientation)")]
        [Tooltip("Metres BEHIND the target (suggested 10 to 12).")]
        [SerializeField] private float followDistance = 11f;

        [Tooltip("Metres ABOVE the target (suggested 3 to 4).")]
        [SerializeField] private float heightOffset = 3.5f;

        [Tooltip("Metres to the SIDE. Positive = target right, negative = left (suggested 3 to 4).")]
        [SerializeField] private float sideOffset = 3.5f;

        [Header("Aim / Framing")]
        [Tooltip("Metres AHEAD of target to aim at, keeping focus tight on the helicopter body (suggested 1 to 3m).")]
        [SerializeField] private float lookAheadDistance = 2f;

        [Tooltip("Metres above target to aim at.")]
        [SerializeField] private float lookHeight = 1f;

        [Header("Damping (higher = tighter/faster, lower = looser/floatier)")]
        [Tooltip("How quickly camera catches up in position (suggested 4 to 6).")]
        [SerializeField] private float positionDamping = 5f;

        [Tooltip("How quickly camera turns toward its look target (suggested 4 to 6).")]
        [SerializeField] private float rotationDamping = 5f;

        [Header("Axes")]
        [Tooltip("Which LOCAL axis of the TARGET is its forward direction. Verified for this model as (1, 0, 0).")]
        [SerializeField] private Vector3 targetForwardAxis = new Vector3(1f, 0f, 0f);

        [Tooltip("Which LOCAL axis of the TARGET is its up direction. Default (0, 1, 0).")]
        [SerializeField] private Vector3 targetUpAxis = new Vector3(0f, 1f, 0f);

        [Header("Cinematic Composition")]
        [Tooltip("When true, enforces a stable rear three-quarter shot by maintaining sideOffset " +
                 "and preserving the rear-side perspective throughout flight.")]
        [SerializeField] private bool stableRearThreeQuarter = true;

        [Header("Startup")]
        [Tooltip("Snap to ideal pose on first frame instead of gliding in from wherever camera was parked. " +
                 "Prevents a visible swoop when Play begins.")]
        [SerializeField] private bool snapOnStart = true;

        [Header("Control")]
        [Tooltip("Uncheck to freeze follow without removing component.")]
        [SerializeField] private bool followEnabled = true;

        [Tooltip("Use unscaled time so follow is unaffected by Time.timeScale.")]
        [SerializeField] private bool useUnscaledTime = true;

        private bool _snapped;

        /// <summary>Assign or replace the follow target at runtime.</summary>
        public Transform Target
        {
            get => target;
            set { target = value; _snapped = false; }
        }

        /// <summary>Enables/disables following at runtime.</summary>
        public bool FollowEnabled
        {
            get => followEnabled;
            set => followEnabled = value;
        }

        public float FollowDistance
        {
            get => followDistance;
            set => followDistance = value;
        }

        public float HeightOffset
        {
            get => heightOffset;
            set => heightOffset = value;
        }

        public float SideOffset
        {
            get => sideOffset;
            set => sideOffset = value;
        }

        public float LookAheadDistance
        {
            get => lookAheadDistance;
            set => lookAheadDistance = value;
        }

        public float LookHeight
        {
            get => lookHeight;
            set => lookHeight = value;
        }

        public float PositionDamping
        {
            get => positionDamping;
            set => positionDamping = value;
        }

        public float RotationDamping
        {
            get => rotationDamping;
            set => rotationDamping = value;
        }

        public Vector3 TargetForwardAxis
        {
            get => targetForwardAxis;
            set => targetForwardAxis = value;
        }

        public Vector3 TargetUpAxis
        {
            get => targetUpAxis;
            set => targetUpAxis = value;
        }

        public bool StableRearThreeQuarter
        {
            get => stableRearThreeQuarter;
            set => stableRearThreeQuarter = value;
        }

        public bool SnapOnStart
        {
            get => snapOnStart;
            set => snapOnStart = value;
        }

        /// <summary>Forces the next update to snap rather than damp.</summary>
        public void RequestSnap() => _snapped = false;

        private void LateUpdate()
        {
            if (!followEnabled) return;
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            UpdateFollow(dt);
        }

        /// <summary>
        /// Moves and aims the camera for one step. Exposed so composition can be verified
        /// deterministically in tests (Edit Mode has no frame loop).
        /// </summary>
        public void UpdateFollow(float deltaTime)
        {
            if (target == null) return;

            Vector3 fwd = ResolveAxis(targetForwardAxis, Vector3.right);
            Vector3 up = ResolveAxis(targetUpAxis, Vector3.up);

            Vector3 worldFwd = target.TransformDirection(fwd).normalized;
            Vector3 worldUp = target.TransformDirection(up).normalized;
            Vector3 worldRight = Vector3.Cross(worldUp, worldFwd).normalized;

            // Cross() degenerates if forward and up are parallel; fall back to target's right.
            if (worldRight.sqrMagnitude < 1e-8f) worldRight = target.right;

            float effectiveSideOffset = sideOffset;
            if (stableRearThreeQuarter && Mathf.Abs(effectiveSideOffset) < 0.1f)
            {
                effectiveSideOffset = 3.5f;
            }

            Vector3 desiredPos = target.position
                                 - worldFwd * followDistance
                                 + worldUp * heightOffset
                                 + worldRight * effectiveSideOffset;

            Vector3 lookTarget = target.position
                                 + worldFwd * lookAheadDistance
                                 + worldUp * lookHeight;

            // First frame: optionally adopt ideal pose exactly, preventing a visible swoop on start.
            if (!_snapped)
            {
                _snapped = true;
                if (snapOnStart)
                {
                    Vector3 initToTarget = lookTarget - desiredPos;
                    Quaternion initRot = initToTarget.sqrMagnitude > 1e-8f
                        ? Quaternion.LookRotation(initToTarget, worldUp)
                        : transform.rotation;
                    transform.SetPositionAndRotation(desiredPos, initRot);
                    return;
                }
            }

            if (deltaTime <= 0f) return;

            // Exponential damping. 1 - exp(-k * dt) is strictly framerate-independent, unlike a raw
            // Lerp(a, b, k * dt), which changes feel with framerate and can overshoot when k * dt > 1.
            float posT = 1f - Mathf.Exp(-Mathf.Max(0f, positionDamping) * deltaTime);
            float rotT = 1f - Mathf.Exp(-Mathf.Max(0f, rotationDamping) * deltaTime);

            transform.position = Vector3.Lerp(transform.position, desiredPos, Mathf.Clamp01(posT));

            // When stableRearThreeQuarter is enabled, aim from actual camera position so the
            // helicopter stays framed regardless of flight speed, preventing front drift.
            Vector3 aimOrigin = stableRearThreeQuarter ? transform.position : desiredPos;
            Vector3 toTarget = lookTarget - aimOrigin;
            Quaternion desiredRot = toTarget.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(toTarget, worldUp)
                : transform.rotation;

            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, Mathf.Clamp01(rotT));
        }

        private static Vector3 ResolveAxis(Vector3 axis, Vector3 fallback) =>
            axis.sqrMagnitude < 1e-8f ? fallback : axis.normalized;
    }
}
