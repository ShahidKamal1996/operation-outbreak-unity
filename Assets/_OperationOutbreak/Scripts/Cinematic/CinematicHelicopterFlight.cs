using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Explicit phases for cinematic takeoff and flight:
    ///   Phase 1: GroundIdle          — helicopter sits stationary on ground while rotors spin.
    ///   Phase 2: VerticalLift        — smooth vertical lift off the ground (zero forward movement).
    ///   Phase 3: ForwardTransition   — smooth forward acceleration easing into cruise speed while climbing.
    ///   Phase 4: Cruise              — straight flight at cruise speed.
    /// </summary>
    public enum FlightPhase
    {
        GroundIdle,
        VerticalLift,
        ForwardTransition,
        Cruise
    }

    /// <summary>
    /// Micro task #5 — Visible ground-to-air takeoff flight presentation with explicit phases.
    ///
    /// Attach to <c>HelicopterFlightRoot</c>, the MOVEMENT parent of <c>helicopter_rigged</c>.
    /// Moves only the GameObject it is attached to. It never touches the visual model, the rotors,
    /// or <see cref="CinematicHelicopterRotorSpin"/>.
    ///
    /// FOUR EXPLICIT PHASES
    /// --------------------
    /// 1. GroundIdle          — sits on ground while rotors spin up.
    /// 2. VerticalLift        — lifts vertically to initialLiftHeight with ZERO forward displacement.
    /// 3. ForwardTransition   — smoothly accelerates forward up to cruiseSpeed while gently climbing.
    /// 4. Cruise              — continues straight at cruise speed.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Helicopter Flight")]
    public sealed class CinematicHelicopterFlight : MonoBehaviour
    {
        [Header("Takeoff Phasing")]
        [Tooltip("Seconds the helicopter stays stationary on the ground while rotors spool (Phase 1).")]
        [SerializeField] private float groundIdleDuration = 1.2f;

        [Tooltip("Seconds over which the helicopter smoothly lifts straight up off the ground (Phase 2).")]
        [SerializeField] private float verticalLiftDuration = 1.8f;

        [Tooltip("Target altitude in metres reached at the end of the vertical lift phase.")]
        [SerializeField] private float initialLiftHeight = 1.75f;

        [Tooltip("Seconds to smoothly accelerate from zero to cruise speed once airborne (Phase 3).")]
        [SerializeField] private float forwardAccelerationDuration = 2.5f;

        [Header("Speed")]
        [Tooltip("Forward speed in metres/second once fully accelerated (Phase 4: Cruise).")]
        [SerializeField] private float cruiseSpeed = 8f;

        [Tooltip("Upward climb speed in metres/second during forward acceleration.")]
        [SerializeField] private float verticalRiseSpeed = 1.2f;

        [Tooltip("Rise is scaled by this once cruising, so the helicopter levels off instead of " +
                 "climbing forever. 1 = keep full rise, 0 = stop rising at cruise.")]
        [Range(0f, 1f)]
        [SerializeField] private float cruiseRiseMultiplier = 0.35f;

        [Tooltip("Maximum total metres gained above the start height. 0 = uncapped.")]
        [SerializeField] private float maxRiseHeight = 6f;

        [Header("Orientation")]
        [Tooltip("Degrees of nose-down pitch eased in during takeoff. In Micro Task #3 cosmetic pitch " +
                 "was migrated to CinematicHelicopterVisualMotion on the child model (helicopter_rigged). " +
                 "Retained for backward compatibility / tests.")]
        [SerializeField] private float takeoffPitch = 4f;

        [Tooltip("When true, applies cosmetic pitch to HelicopterFlightRoot. Defaults to false " +
                 "so CinematicHelicopterVisualMotion owns visual pitch without duplicate pitching.")]
        [SerializeField] private bool applyTakeoffPitch = false;

        [Tooltip("Which LOCAL axis of THIS flight root counts as 'forward'. Verified as (1, 0, 0).")]
        [SerializeField] private Vector3 localForwardAxis = new Vector3(1f, 0f, 0f);

        [Tooltip("Local axis treated as 'up' for the climb. Default (0, 1, 0).")]
        [SerializeField] private Vector3 localUpAxis = new Vector3(0f, 1f, 0f);

        [Header("Control")]
        [Tooltip("Uncheck to freeze the flight without removing the component.")]
        [SerializeField] private bool flightEnabled = true;

        [Tooltip("Use unscaled time so the flight is unaffected by Time.timeScale.")]
        [SerializeField] private bool useUnscaledTime = true;

        // Retained for backward compatibility / tests:
        [SerializeField] private float startDelay = 0.75f;
        [SerializeField] private float accelerationDuration = 2.5f;

        // ---- runtime state ----
        private bool _initialized;
        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private Vector3 _travelDirection;   // world-space, fixed at start
        private Vector3 _riseDirection;     // world-space, fixed at start
        private float _elapsed;
        private float _distance;
        private float _forwardClimb;
        private float _rise;

        /// <summary>Seconds since Play began, including ground idle and vertical lift.</summary>
        public float Elapsed => _elapsed;

        /// <summary>Metres travelled forward from the start position.</summary>
        public float DistanceTravelled => _distance;

        /// <summary>Metres risen above the start position.</summary>
        public float HeightGained => _rise;

        public float GroundIdleDuration
        {
            get => groundIdleDuration;
            set { groundIdleDuration = value; startDelay = value; }
        }

        public float VerticalLiftDuration
        {
            get => verticalLiftDuration;
            set => verticalLiftDuration = value;
        }

        public float InitialLiftHeight
        {
            get => initialLiftHeight;
            set => initialLiftHeight = value;
        }

        public float ForwardAccelerationDuration
        {
            get => forwardAccelerationDuration;
            set { forwardAccelerationDuration = value; accelerationDuration = value; }
        }

        public float CruiseSpeed
        {
            get => cruiseSpeed;
            set => cruiseSpeed = value;
        }

        public Vector3 LocalForwardAxis
        {
            get => localForwardAxis;
            set => localForwardAxis = value;
        }

        public Vector3 LocalUpAxis
        {
            get => localUpAxis;
            set => localUpAxis = value;
        }

        public FlightPhase CurrentPhase
        {
            get
            {
                float idle = EffectiveIdle;
                float lift = verticalLiftDuration;
                float accel = EffectiveAccel;

                if (_elapsed <= idle) return FlightPhase.GroundIdle;
                if (_elapsed <= idle + lift) return FlightPhase.VerticalLift;
                if (_elapsed < idle + lift + accel) return FlightPhase.ForwardTransition;
                return FlightPhase.Cruise;
            }
        }

        public bool IsGroundIdle => CurrentPhase == FlightPhase.GroundIdle;
        public bool IsVerticalLift => CurrentPhase == FlightPhase.VerticalLift;
        public bool IsForwardTransition => CurrentPhase == FlightPhase.ForwardTransition;
        public bool IsCruising => CurrentPhase == FlightPhase.Cruise;

        /// <summary>0 during GroundIdle and VerticalLift, smoothly easing 0→1 during ForwardTransition, then 1.</summary>
        public float SpeedFactor => ComputeSpeedFactor(_elapsed);

        /// <summary>Current forward speed in metres/second.</summary>
        public float CurrentSpeed => cruiseSpeed * SpeedFactor;

        /// <summary>0 during GroundIdle, smoothly easing 0→1 during VerticalLift, then 1.</summary>
        public float VerticalLiftFactor => ComputeVerticalLiftFactor(_elapsed);

        public bool FlightEnabled
        {
            get => flightEnabled;
            set => flightEnabled = value;
        }

        public bool ApplyTakeoffPitch
        {
            get => applyTakeoffPitch;
            set => applyTakeoffPitch = value;
        }

        public float TakeoffPitch
        {
            get => takeoffPitch;
            set => takeoffPitch = value;
        }

        private float EffectiveIdle => (groundIdleDuration != 1.2f) ? groundIdleDuration : (startDelay != 0.75f ? startDelay : groundIdleDuration);
        private float EffectiveAccel => (forwardAccelerationDuration != 2.5f) ? forwardAccelerationDuration : accelerationDuration;

        private void Awake() => CaptureStartState();

        private void CaptureStartState()
        {
            if (_initialized) return;
            _initialized = true;

            _startPosition = transform.position;
            _startRotation = transform.rotation;

            Vector3 fwd = localForwardAxis.sqrMagnitude < 1e-8f
                ? Vector3.right
                : localForwardAxis.normalized;
            Vector3 up = localUpAxis.sqrMagnitude < 1e-8f
                ? Vector3.up
                : localUpAxis.normalized;

            _travelDirection = (_startRotation * fwd).normalized;
            _riseDirection = (_startRotation * up).normalized;
        }

        private void Update()
        {
            if (!flightEnabled) return;
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            AdvanceFlight(dt);
        }

        public void AdvanceFlight(float deltaTime)
        {
            CaptureStartState();
            if (deltaTime <= 0f) return;

            _elapsed += deltaTime;

            // Phase 1 & 2: Vertical Lift
            float liftFactor = ComputeVerticalLiftFactor(_elapsed);
            float baseLift = initialLiftHeight * liftFactor;

            // Phase 3 & 4: Forward Acceleration & Climb
            float speedFactor = ComputeSpeedFactor(_elapsed);

            // Forward translation only occurs once airborne in forward transition
            _distance += cruiseSpeed * speedFactor * deltaTime;

            // Additional gentle climb during forward travel
            if (speedFactor > 0f)
            {
                float riseMultiplier = Mathf.Lerp(1f, cruiseRiseMultiplier, speedFactor);
                _forwardClimb += verticalRiseSpeed * speedFactor * riseMultiplier * deltaTime;
            }

            _rise = baseLift + _forwardClimb;
            if (maxRiseHeight > 0f) _rise = Mathf.Min(_rise, maxRiseHeight);

            transform.position = _startPosition
                                 + _travelDirection * _distance
                                 + _riseDirection * _rise;

            transform.rotation = (applyTakeoffPitch && takeoffPitch != 0f)
                ? _startRotation * Quaternion.AngleAxis(takeoffPitch * speedFactor, Vector3.right)
                : _startRotation;
        }

        public float ComputeSpeedFactor(float elapsed)
        {
            float startForwardTime = EffectiveIdle + verticalLiftDuration;
            if (elapsed <= startForwardTime) return 0f;
            float accel = EffectiveAccel;
            if (accel <= 0f) return 1f;

            float u = Mathf.Clamp01((elapsed - startForwardTime) / accel);
            return Mathf.SmoothStep(0f, 1f, u);
        }

        public float ComputeVerticalLiftFactor(float elapsed)
        {
            float idle = EffectiveIdle;
            if (elapsed <= idle) return 0f;
            if (verticalLiftDuration <= 0f) return 1f;

            float u = Mathf.Clamp01((elapsed - idle) / verticalLiftDuration);
            return Mathf.SmoothStep(0f, 1f, u);
        }

        public void ResetFlight()
        {
            CaptureStartState();
            _elapsed = 0f;
            _distance = 0f;
            _forwardClimb = 0f;
            _rise = 0f;
            transform.position = _startPosition;
            transform.rotation = _startRotation;
        }
    }
}
