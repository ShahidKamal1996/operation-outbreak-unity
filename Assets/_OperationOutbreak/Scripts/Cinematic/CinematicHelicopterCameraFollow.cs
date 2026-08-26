using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Micro task #5 — polished cinematic ground-to-air takeoff follow camera for the helicopter.
    ///
    /// Attach to the EXISTING camera in Helicopter_Cinematic. This creates no cameras, disables no
    /// cameras, and changes no camera settings (FOV, clipping, culling are left alone) — it only
    /// moves and aims the transform it lives on.
    ///
    /// CINEMATIC GROUND-TO-AIR TAKEOFF FLOW
    /// ------------------------------------
    /// Phase 1: Grounded Establishing Shot (~1.0-1.5s)
    ///     The camera holds its authored scene transform exactly at Play.
    ///     Helicopter sits grounded, rotors spinning. No immediate jump/teleport.
    ///
    /// Phase 2 & 3: Vertical Lift & Forward Transition (~2.0-3.0s)
    ///     Helicopter lifts off the ground and accelerates forward.
    ///     The authored establishing composition RIGIDLY TRACKS the helicopter (it is carried by
    ///     the helicopter's displacement since capture, so the subject stays exactly as framed as
    ///     it was at Play), and the camera smoothly blends from that takeoff-tracking composition
    ///     into the dynamic rear 3/4 chase framing. The pose blend uses an ease-in weighting of
    ///     TakeoffBlendWeight, so the establishing shot dominates through the vertical lift and
    ///     the chase framing takes over across the forward transition.
    ///
    /// Phase 4: Full Chase Flight
    ///     Camera maintains the polished Micro Task #4 rear 3/4 cinematic composition:
    ///     behind + above + to one side, aimed tightly at the helicopter body.
    ///
    /// Runs in LateUpdate so HelicopterFlightRoot translation and rise have already executed.
    ///
    /// MICRO TASK #5A FIX — AUTHORED CAMERA SHOT IS ALWAYS RECAPTURED AT PLAY
    /// ----------------------------------------------------------------------
    /// The authored-shot capture (and the takeoff blend timeline) is re-armed in OnEnable every
    /// time a new Play session begins. Unity's Enter Play Mode Options can disable Domain/Scene
    /// reload, in which case the persisted _snapped/_elapsed/_dampedPos state from the previous
    /// session would otherwise skip the authored ground shot and jump the camera to the old
    /// chase pose on the very first frame. OnEnable is called at every Play entry, so the camera
    /// always re-captures its CURRENT authored transform and holds it for the full ground hold.
    ///
    /// QA FIX #5B — THE ESTABLISHING SHOT TRACKS THE LIFTOFF (HELICOPTER NEVER LEAVES THE FRAME)
    /// ---------------------------------------------------------------------------------------
    /// Root cause of the "takeoff is invisible for the first few seconds" report: the takeoff
    /// blend interpolated between the AUTHORED (world-locked, frozen) camera transform and the
    /// damped chase pose. The helicopter's VerticalLift (+1.75m) and ForwardTransition move the
    /// subject OUT of the frozen authored composition, while the chase framing only becomes
    /// dominant once the helicopter is already airborne — so the subject left the camera frame
    /// during the opening seconds and was re-acquired mid-flight.
    ///
    /// Fix: the "from" end of the blend is no longer the frozen authored transform. It is the
    /// authored composition RIGIDLY CARRIED by the target's displacement since capture
    /// (position + orientation delta). During GroundIdle the helicopter is stationary, so this
    /// is EXACTLY the authored establishing shot (unchanged behavior); once VerticalLift
    /// begins the establishing camera translates with the rising helicopter, keeping the
    /// subject continuously framed with the same relative composition the scene author
    /// verified. The "to" end (damped chase pose) always aims at the current helicopter, so
    /// every blend state frames the subject: the helicopter can never leave the frame during
    /// the takeoff. All smoothing stays framerate-independent (exponential damping +
    /// SmoothStep blend), and the Snap On Start = false cinematic behavior is preserved.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Helicopter Camera Follow")]
    public sealed class CinematicHelicopterCameraFollow : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("What to follow. Assign HelicopterFlightRoot (authoritative flight parent), NOT the child model.")]
        [SerializeField] private Transform target;

        [Header("Cinematic Takeoff Transition")]
        [Tooltip("When enabled (and snapOnStart is false), preserves the authored ground camera shot and " +
                 "smoothly transitions into dynamic chase framing as the helicopter lifts off.")]
        [SerializeField] private bool enableTakeoffTransition = true;

        [Tooltip("Seconds the camera stays locked in its authored ground composition before beginning transition.")]
        [SerializeField] private float takeoffCameraHoldDuration = 1.2f;

        [Tooltip("Seconds over which camera smoothly blends from authored ground shot into full chase framing.")]
        [SerializeField] private float takeoffCameraBlendDuration = 2.5f;

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
        [Tooltip("Snap to ideal chase pose on first frame. Set false for cinematic takeoff transition.")]
        [SerializeField] private bool snapOnStart = false;

        [Header("Control")]
        [Tooltip("Uncheck to freeze follow without removing component.")]
        [SerializeField] private bool followEnabled = true;

        [Tooltip("Use unscaled time so follow is unaffected by Time.timeScale.")]
        [SerializeField] private bool useUnscaledTime = true;

        // ---- runtime state ----
        private bool _snapped;
        private Vector3 _initialCameraPosition;
        private Quaternion _initialCameraRotation;
        private Vector3 _dampedPos;
        private Quaternion _dampedRot;
        private float _elapsed;

        // QA Fix #5B — the target's pose at the moment the authored shot was captured.
        // The takeoff blend's "from" pole is the authored composition rigidly carried by the
        // target's displacement relative to these values (see UpdateFollow / class docs).
        private Vector3 _targetStartPosition;
        private Quaternion _targetStartRotation;

        // ---- play-session guards (Micro Task #5A) ----
        // With Unity's "Enter Play Mode Options" (Domain/Scene reload disabled) instance state
        // persists between Play sessions and the camera would otherwise keep the previous
        // session's chase pose and blend timeline instead of re-capturing the authored shot.
        // OnEnable is called at every Play entry; the session counter (bumped from a
        // RuntimeInitializeOnLoadMethod, which runs even without domain reload) distinguishes a
        // brand-new Play session from a component toggle mid-Play.
        private static int _sessionCounter;
        private int _playSessionId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnNewPlaySession() => _sessionCounter++;

        /// <summary>Assign or replace the follow target at runtime.</summary>
        public Transform Target
        {
            get => target;
            set { target = value; _snapped = false; }
        }

        public bool EnableTakeoffTransition
        {
            get => enableTakeoffTransition;
            set => enableTakeoffTransition = value;
        }

        public float TakeoffCameraHoldDuration
        {
            get => takeoffCameraHoldDuration;
            set => takeoffCameraHoldDuration = value;
        }

        public float TakeoffCameraBlendDuration
        {
            get => takeoffCameraBlendDuration;
            set => takeoffCameraBlendDuration = value;
        }

        /// <summary>
        /// 0 while the authored ground shot is held, SmoothStep-ramping over
        /// takeoffCameraBlendDuration afterwards, 1 once the chase framing is fully established.
        /// The pose blend additionally applies an ease-in square of this weight so the
        /// takeoff-tracking (establishing) composition stays dominant through VerticalLift and
        /// the chase framing takes over across ForwardTransition (see UpdateFollow).
        /// </summary>
        public float TakeoffBlendWeight
        {
            get
            {
                if (!enableTakeoffTransition || snapOnStart) return 1f;
                if (_elapsed <= takeoffCameraHoldDuration) return 0f;
                if (takeoffCameraBlendDuration <= 0f) return 1f;

                float u = Mathf.Clamp01((_elapsed - takeoffCameraHoldDuration) / takeoffCameraBlendDuration);
                return Mathf.SmoothStep(0f, 1f, u);
            }
        }

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

        public float Elapsed => _elapsed;

        /// <summary>Forces the next update to reset initial capture.</summary>
        public void RequestSnap()
        {
            _snapped = false;
            _elapsed = 0f;
        }

        private void OnEnable()
        {
            // Runs at the start of EVERY Play session — even when Enter Play Mode Options skip
            // Domain/Scene reload. Re-arm the authored-shot capture and reset the takeoff blend
            // timeline so the camera always opens a session from its CURRENT authored transform
            // and holds it through the ground idle (Snap On Start = false).
            if (_playSessionId == _sessionCounter) return;
            _playSessionId = _sessionCounter;
            _snapped = false;
            _elapsed = 0f;
        }

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

            // First frame initialization
            if (!_snapped)
            {
                _snapped = true;
                _initialCameraPosition = transform.position;
                _initialCameraRotation = transform.rotation;

                // QA Fix #5B — capture the target's pose at the same moment as the authored shot.
                // The takeoff blend's "from" pole is the authored composition rigidly carried by
                // the target's displacement relative to these values, so the establishing camera
                // follows the helicopter's takeoff instead of staying frozen in world space.
                _targetStartPosition = target.position;
                _targetStartRotation = target.rotation;

                _dampedPos = _initialCameraPosition;
                _dampedRot = _initialCameraRotation;

                if (snapOnStart)
                {
                    Vector3 initToTarget = lookTarget - desiredPos;
                    Quaternion initRot = initToTarget.sqrMagnitude > 1e-8f
                        ? Quaternion.LookRotation(initToTarget, worldUp)
                        : transform.rotation;
                    transform.SetPositionAndRotation(desiredPos, initRot);
                    _dampedPos = desiredPos;
                    _dampedRot = initRot;
                    return;
                }

                if (enableTakeoffTransition)
                {
                    transform.SetPositionAndRotation(_initialCameraPosition, _initialCameraRotation);
                    return;
                }
            }

            if (deltaTime <= 0f) return;

            _elapsed += deltaTime;

            // Exponential damping. 1 - exp(-k * dt) is strictly framerate-independent, unlike a raw
            // Lerp(a, b, k * dt), which changes feel with framerate and can overshoot when k * dt > 1.
            float posT = 1f - Mathf.Exp(-Mathf.Max(0f, positionDamping) * deltaTime);
            float rotT = 1f - Mathf.Exp(-Mathf.Max(0f, rotationDamping) * deltaTime);

            _dampedPos = Vector3.Lerp(_dampedPos, desiredPos, Mathf.Clamp01(posT));

            // When stableRearThreeQuarter is enabled, aim from actual camera position so the
            // helicopter stays framed regardless of flight speed, preventing front drift.
            Vector3 aimOrigin = stableRearThreeQuarter ? _dampedPos : desiredPos;
            Vector3 toTarget = lookTarget - aimOrigin;
            Quaternion desiredRot = toTarget.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(toTarget, worldUp)
                : transform.rotation;

            _dampedRot = Quaternion.Slerp(_dampedRot, desiredRot, Mathf.Clamp01(rotT));

            if (enableTakeoffTransition && !snapOnStart)
            {
                float w = TakeoffBlendWeight;

                // QA Fix #5B — "from" pole: the authored establishing composition, rigidly
                // carried by the target's displacement since capture. While the helicopter sits
                // in GroundIdle the displacement is zero, so this is EXACTLY the authored shot
                // (unchanged behavior); once VerticalLift begins the establishing camera
                // translates with the rising helicopter, keeping the subject continuously
                // framed with the composition the scene author verified. (A frozen, world-locked
                // authored transform as the blend's "from" pole is what let the helicopter leave
                // the frame during the first few seconds of the takeoff before the chase
                // framing took over.)
                Vector3 takeoffPos = _initialCameraPosition + (target.position - _targetStartPosition);
                Quaternion takeoffRot = _initialCameraRotation;
                if (target.rotation != _targetStartRotation)
                {
                    takeoffRot = target.rotation * _targetStartRotation.inverse * _initialCameraRotation;
                }

                // Ease-in pose blend: the tracking composition stays dominant through
                // VerticalLift, and the chase framing takes over across ForwardTransition.
                // (TakeoffBlendWeight itself is unchanged and remains the public contract.)
                float poseW = w * w;
                transform.position = Vector3.Lerp(takeoffPos, _dampedPos, poseW);
                transform.rotation = Quaternion.Slerp(takeoffRot, _dampedRot, poseW);
            }
            else
            {
                transform.position = _dampedPos;
                transform.rotation = _dampedRot;
            }
        }

        private static Vector3 ResolveAxis(Vector3 axis, Vector3 fallback) =>
            axis.sqrMagnitude < 1e-8f ? fallback : axis.normalized;
    }
}
