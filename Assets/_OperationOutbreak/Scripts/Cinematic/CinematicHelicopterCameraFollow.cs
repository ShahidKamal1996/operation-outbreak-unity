using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Micro task #2 — smooth follow camera for the cinematic helicopter. Nothing else.
    ///
    /// Attach to the EXISTING camera in Helicopter_Cinematic. This creates no cameras, disables no
    /// cameras, and changes no camera settings (FOV, clipping, culling are all left alone) — it
    /// only moves and aims the transform it lives on.
    ///
    /// The camera is deliberately NOT parented to the helicopter. Each frame it computes a desired
    /// pose relative to the target and damps toward it, so the rig lags slightly behind the
    /// helicopter. That lag is what makes the departure feel filmed rather than rigidly bolted on.
    ///
    /// Composition: behind + above + slightly to one side, aiming ahead of the helicopter at
    ///     target.position + forward * lookAheadDistance + up * lookHeight
    ///
    /// Runs in LateUpdate so the helicopter has already moved this frame — following in Update
    /// would always chase a stale position and judder.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Helicopter Camera Follow")]
    public sealed class CinematicHelicopterCameraFollow : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("What to follow. Assign HelicopterFlightRoot (the moving parent), not the model.")]
        [SerializeField] private Transform target;

        [Header("Offsets (relative to the target's orientation)")]
        [Tooltip("Metres BEHIND the target.")]
        [SerializeField] private float followDistance = 14f;

        [Tooltip("Metres ABOVE the target.")]
        [SerializeField] private float heightOffset = 5f;

        [Tooltip("Metres to the SIDE. Positive = target's right, negative = left.")]
        [SerializeField] private float sideOffset = 4f;

        [Header("Damping (higher = tighter/faster, lower = looser/floatier)")]
        [Tooltip("How quickly the camera catches up in position.")]
        [SerializeField] private float positionDamping = 3f;

        [Tooltip("How quickly the camera turns toward its look target.")]
        [SerializeField] private float rotationDamping = 3f;

        [Header("Aim")]
        [Tooltip("Metres AHEAD of the target to aim at, so the shot leads the motion.")]
        [SerializeField] private float lookAheadDistance = 4f;

        [Tooltip("Metres above the target to aim at.")]
        [SerializeField] private float lookHeight = 1f;

        [Header("Axes")]
        [Tooltip("Which LOCAL axis of the TARGET is its forward. Must match the flight " +
                 "component's Local Forward Axis, or the camera will sit beside the helicopter " +
                 "instead of behind it.")]
        [SerializeField] private Vector3 targetForwardAxis = new Vector3(0f, 0f, 1f);

        [Tooltip("Which LOCAL axis of the TARGET is its up.")]
        [SerializeField] private Vector3 targetUpAxis = new Vector3(0f, 1f, 0f);

        [Header("Startup")]
        [Tooltip("Snap to the ideal pose on the first frame instead of gliding in from wherever " +
                 "the camera was parked. Prevents a visible swoop when Play begins.")]
        [SerializeField] private bool snapOnStart = true;

        [Header("Control")]
        [Tooltip("Uncheck to freeze the follow without removing the component.")]
        [SerializeField] private bool followEnabled = true;

        [Tooltip("Use unscaled time so the follow is unaffected by Time.timeScale.")]
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

        /// <summary>Forces the next update to snap rather than damp.</summary>
        public void RequestSnap() => _snapped = false;

        private void LateUpdate()
        {
            if (!followEnabled) return;
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            UpdateFollow(dt);
        }

        /// <summary>
        /// Moves/aims the camera for one step. Exposed so composition can be verified
        /// deterministically in tests (Edit Mode has no frame loop).
        /// </summary>
        public void UpdateFollow(float deltaTime)
        {
            if (target == null) return;

            Vector3 fwd = ResolveAxis(targetForwardAxis, Vector3.forward);
            Vector3 up = ResolveAxis(targetUpAxis, Vector3.up);

            Vector3 worldFwd = (target.rotation * fwd).normalized;
            Vector3 worldUp = (target.rotation * up).normalized;
            Vector3 worldRight = Vector3.Cross(worldUp, worldFwd).normalized;

            // Cross() degenerates if forward and up are parallel; fall back to the target's right.
            if (worldRight.sqrMagnitude < 1e-8f) worldRight = target.right;

            Vector3 desiredPos = target.position
                                 - worldFwd * followDistance
                                 + worldUp * heightOffset
                                 + worldRight * sideOffset;

            Vector3 lookTarget = target.position
                                 + worldFwd * lookAheadDistance
                                 + worldUp * lookHeight;

            Vector3 toTarget = lookTarget - desiredPos;
            Quaternion desiredRot = toTarget.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(toTarget, worldUp)
                : transform.rotation;

            // First frame: optionally adopt the ideal pose exactly, so there is no visible swoop.
            if (!_snapped)
            {
                _snapped = true;
                if (snapOnStart)
                {
                    transform.SetPositionAndRotation(desiredPos, desiredRot);
                    return;
                }
            }

            if (deltaTime <= 0f) return;

            // Exponential damping. 1 - exp(-k * dt) is framerate-independent, unlike a raw
            // Lerp(a, b, k * dt), which changes feel with framerate and can overshoot when
            // k * dt > 1. Clamp01 keeps it stable across long editor hitches.
            float posT = 1f - Mathf.Exp(-Mathf.Max(0f, positionDamping) * deltaTime);
            float rotT = 1f - Mathf.Exp(-Mathf.Max(0f, rotationDamping) * deltaTime);

            transform.position = Vector3.Lerp(transform.position, desiredPos, Mathf.Clamp01(posT));
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, Mathf.Clamp01(rotT));
        }

        private static Vector3 ResolveAxis(Vector3 axis, Vector3 fallback) =>
            axis.sqrMagnitude < 1e-8f ? fallback : axis.normalized;
    }
}
