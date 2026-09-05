using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Very gentle, INDEPENDENT exterior camera tracking for the Helicopter_Cinematic exterior
    /// flight.
    ///
    /// The camera keeps its AUTHORED scene transform as the base — the current opening
    /// composition is preserved — and it is NEVER parented to (or rigidly carried by)
    /// HelicopterFlightRoot. It only, while a target is assigned:
    ///   - turns toward the target, clamped to maxTrackingAngle away from its AUTHORED
    ///     orientation (the helicopter stays in the intended shot without the camera re-framing
    ///     the world), and
    ///   - drifts at most maxPositionDrift metres toward the target from its AUTHORED position
    ///     (a subtle follow, not a chase camera).
    /// Both are exponentially damped (framerate-independent, the same 1 - exp(-k*dt)
    /// convention as CinematicHelicopterCameraFollow), so the motion is slow and gentle: the
    /// helicopter moves through the frame naturally while the camera behaves like an
    /// independent camera position, not one physically attached to the helicopter.
    ///
    /// The same authored-pose capture/re-arm pattern as the other cinematic components applies:
    /// the pose is captured once per Play session (OnEnable re-arms; the first UpdateTracking
    /// call captures), so the opening composition is always the CURRENT authored transform.
    ///
    /// This component creates no cameras, disables no cameras, and changes no camera settings
    /// (FOV, clipping, culling are left alone) — it only moves and aims the transform it lives
    /// on. Runs in LateUpdate; UpdateTracking is exposed for deterministic EditMode tests.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Exterior Camera Tracking")]
    public sealed class CinematicExteriorCameraTracking : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("What to gently track. Assign HelicopterFlightRoot. The camera is NEVER parented to it.")]
        [SerializeField] private Transform target;

        [Header("Limits (preserve the authored composition)")]
        [Tooltip("Maximum degrees the camera may turn away from its AUTHORED orientation while tracking.")]
        [SerializeField] private float maxTrackingAngle = 10f;

        [Tooltip("Maximum metres the camera may drift away from its AUTHORED position toward the target.")]
        [SerializeField] private float maxPositionDrift = 0.75f;

        [Header("Damping (lower = gentler/slower)")]
        [Tooltip("How quickly the camera turns toward the clamped tracking orientation (suggested 0.5 to 1).")]
        [SerializeField] private float rotationDamping = 0.8f;

        [Tooltip("How quickly the camera drifts toward the clamped position (suggested 0.1 to 0.5).")]
        [SerializeField] private float positionDamping = 0.25f;

        [Header("Control")]
        [Tooltip("Uncheck to freeze the tracking without removing the component.")]
        [SerializeField] private bool trackingEnabled = true;

        [Tooltip("Use unscaled time so tracking is unaffected by Time.timeScale. Off by default " +
                 "(safe scaled default, same as the flight and dialogue).")]
        [SerializeField] private bool useUnscaledTime = false;

        // ---- runtime state (re-captured per Play session, like the other cinematic components) ----
        private bool _captured;
        private Vector3 _authoredPosition;
        private Quaternion _authoredRotation;
        private Vector3 _authoredUp;
        private Vector3 _dampedPos;
        private Quaternion _dampedRot;

        // ---- play-session guards (same pattern as CinematicHelicopterCameraFollow) ----
        // With Enter Play Mode Options (Domain/Scene reload disabled) instance state persists
        // between Play sessions; the session counter (bumped from a
        // RuntimeInitializeOnLoadMethod, which runs even without domain reload) re-arms the
        // authored-pose capture at every new Play session.
        private static int _sessionCounter;
        private int _playSessionId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnNewPlaySession() => _sessionCounter++;

        /// <summary>Assign or replace the tracking target (resets the pose capture).</summary>
        public Transform Target
        {
            get => target;
            set { target = value; _captured = false; }
        }

        public bool TrackingEnabled
        {
            get => trackingEnabled;
            set => trackingEnabled = value;
        }

        public float MaxTrackingAngle
        {
            get => maxTrackingAngle;
            set => maxTrackingAngle = value;
        }

        public float MaxPositionDrift
        {
            get => maxPositionDrift;
            set => maxPositionDrift = value;
        }

        public float RotationDamping
        {
            get => rotationDamping;
            set => rotationDamping = value;
        }

        public float PositionDamping
        {
            get => positionDamping;
            set => positionDamping = value;
        }

        /// <summary>Current time-mode setting (serialized default is false = scaled time).</summary>
        public bool UseUnscaledTime => useUnscaledTime;

        private void OnEnable()
        {
            // Runs at the start of EVERY Play session (even when Enter Play Mode Options skip
            // Domain/Scene reload): re-arm the authored-pose capture so the camera always
            // opens a session from its CURRENT authored transform (opening composition).
            if (_playSessionId == _sessionCounter) return;
            _playSessionId = _sessionCounter;
            _captured = false;
        }

        private void LateUpdate()
        {
            UpdateTracking(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
        }

        /// <summary>
        /// One gentle tracking step. Exposed so behavior can be verified deterministically in
        /// tests (Edit Mode has no frame loop). Never parents the camera.
        /// </summary>
        public void UpdateTracking(float deltaTime)
        {
            if (!trackingEnabled) return;

            if (!_captured)
            {
                // Capture the AUTHORED pose (the opening composition) once per session.
                _captured = true;
                _authoredPosition = transform.position;
                _authoredRotation = transform.rotation;
                _authoredUp = _authoredRotation * Vector3.up;
                _dampedPos = _authoredPosition;
                _dampedRot = _authoredRotation;
                return;
            }

            if (target == null || deltaTime <= 0f) return;

            Vector3 toTarget = target.position - _dampedPos;
            if (toTarget.sqrMagnitude < 1e-8f) return;

            // Orientation: look at the target, clamped so the camera never turns more than
            // maxTrackingAngle away from its AUTHORED orientation (composition preserved).
            Quaternion idealRotation = Quaternion.LookRotation(toTarget, _authoredUp);
            float offAuthored = Quaternion.Angle(_authoredRotation, idealRotation);
            if (offAuthored > maxTrackingAngle && offAuthored > 1e-5f)
                idealRotation = Quaternion.Slerp(_authoredRotation, idealRotation, maxTrackingAngle / offAuthored);

            // Position: drift toward the target, clamped to maxPositionDrift from the authored
            // position. The damped position is a lerp between two points inside that drift
            // ball, so it can never leave it (the authored composition never breaks).
            Vector3 toTargetFromAuthored = target.position - _authoredPosition;
            Vector3 desiredPos = toTargetFromAuthored.sqrMagnitude > 1e-8f
                ? _authoredPosition + toTargetFromAuthored.normalized *
                      Mathf.Min(Mathf.Max(0f, maxPositionDrift), toTargetFromAuthored.magnitude)
                : _authoredPosition;

            // Exponential damping: framerate-independent, gentle (same convention as
            // CinematicHelicopterCameraFollow).
            float rotT = Mathf.Clamp01(1f - Mathf.Exp(-Mathf.Max(0f, rotationDamping) * deltaTime));
            float posT = Mathf.Clamp01(1f - Mathf.Exp(-Mathf.Max(0f, positionDamping) * deltaTime));
            _dampedRot = Quaternion.Slerp(_dampedRot, idealRotation, rotT);
            _dampedPos = Vector3.Lerp(_dampedPos, desiredPos, posT);

            transform.SetPositionAndRotation(_dampedPos, _dampedRot);
        }
    }
}
