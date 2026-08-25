using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Micro task #2 — simple straight-line cinematic takeoff. Nothing else.
    ///
    /// Attach to <c>HelicopterFlightRoot</c>, the MOVEMENT parent of <c>helicopter_rigged</c>.
    /// This component moves only the GameObject it is attached to. It never touches the visual
    /// model, the rotors, or <see cref="CinematicHelicopterRotorSpin"/> — those keep running
    /// independently because the model is simply carried along as a child.
    ///
    /// THREE PHASES
    ///   1. Hold      — for <see cref="startDelay"/> seconds the helicopter is perfectly still
    ///                  while the rotors spool visually.
    ///   2. Accelerate— over <see cref="accelerationDuration"/> seconds, SmoothStep-eased from a
    ///                  dead stop to <see cref="cruiseSpeed"/>, rising gently and easing into a
    ///                  subtle forward-flight pitch.
    ///   3. Cruise    — continues straight at cruise speed with a reduced, optionally capped rise.
    ///
    /// NO physics, NO Rigidbody, NO Animator, NO root motion, NO waypoints, NO turning, NO banking.
    ///
    /// TWO DELIBERATE DESIGN CHOICES
    ///
    /// 1. Motion is INTEGRATED FROM THE AUTHORED START TRANSFORM. The start position/rotation are
    ///    captured on the first frame and every later position is
    ///    <c>startPosition + forward * distance + up * rise</c>. Nothing is written before that
    ///    capture, so the helicopter can never jump or snap when Play begins — it departs from
    ///    exactly where you placed it.
    ///
    /// 2. PITCH DOES NOT STEER. The travel direction is captured ONCE at start and is never
    ///    re-read from the transform. If travel used <c>transform.forward</c> each frame, the
    ///    cosmetic nose-down pitch would tilt the flight path into the ground. Keeping them
    ///    separate guarantees genuinely straight flight, which is what this task asks for.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Helicopter Flight")]
    public sealed class CinematicHelicopterFlight : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds the helicopter stays completely still before departing.")]
        [SerializeField] private float startDelay = 0.75f;

        [Tooltip("Seconds to ease from a dead stop up to cruise speed.")]
        [SerializeField] private float accelerationDuration = 2.5f;

        [Header("Speed")]
        [Tooltip("Forward speed in metres/second once fully accelerated.")]
        [SerializeField] private float cruiseSpeed = 8f;

        [Tooltip("Upward speed in metres/second at full acceleration.")]
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

        [Tooltip("Which LOCAL axis of THIS flight root counts as 'forward'. Default (0,0,1) is " +
                 "the root's own authored forward. If the helicopter flies backwards or sideways, " +
                 "change this — e.g. (0,0,-1) or (1,0,0). No code change needed.")]
        [SerializeField] private Vector3 localForwardAxis = new Vector3(0f, 0f, 1f);

        [Tooltip("Local axis treated as 'up' for the climb. Default (0,1,0) = world up when the " +
                 "flight root is unrotated.")]
        [SerializeField] private Vector3 localUpAxis = new Vector3(0f, 1f, 0f);

        [Header("Control")]
        [Tooltip("Uncheck to freeze the flight without removing the component.")]
        [SerializeField] private bool flightEnabled = true;

        [Tooltip("Use unscaled time so the flight is unaffected by Time.timeScale.")]
        [SerializeField] private bool useUnscaledTime = true;

        // ---- runtime state (captured on the first tick, never authored data) ----
        private bool _initialized;
        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private Vector3 _travelDirection;   // world-space, fixed at start
        private Vector3 _riseDirection;     // world-space, fixed at start
        private float _elapsed;
        private float _distance;
        private float _rise;

        /// <summary>Seconds since Play began, including the hold phase.</summary>
        public float Elapsed => _elapsed;

        /// <summary>Metres travelled forward from the start position.</summary>
        public float DistanceTravelled => _distance;

        /// <summary>Metres risen above the start position.</summary>
        public float HeightGained => _rise;

        /// <summary>0 while holding, easing to 1 across the acceleration window.</summary>
        public float SpeedFactor => ComputeSpeedFactor(_elapsed);

        /// <summary>Current forward speed in metres/second.</summary>
        public float CurrentSpeed => cruiseSpeed * SpeedFactor;

        /// <summary>True once the acceleration window has fully elapsed.</summary>
        public bool IsCruising => _elapsed >= startDelay + accelerationDuration;

        /// <summary>Enables/disables the flight at runtime.</summary>
        public bool FlightEnabled
        {
            get => flightEnabled;
            set => flightEnabled = value;
        }

        /// <summary>
        /// When true, applies cosmetic takeoff pitch directly to this root transform.
        /// Defaults to false in Micro Task #3 because cosmetic pitch is now owned by
        /// CinematicHelicopterVisualMotion on the child model, preventing duplicate pitching.
        /// </summary>
        public bool ApplyTakeoffPitch
        {
            get => applyTakeoffPitch;
            set => applyTakeoffPitch = value;
        }

        /// <summary>Cosmetic takeoff pitch in degrees (migrated to visual motion).</summary>
        public float TakeoffPitch
        {
            get => takeoffPitch;
            set => takeoffPitch = value;
        }

        private void Awake() => CaptureStartState();

        /// <summary>
        /// Snapshots the authored transform and derives the fixed travel/rise directions.
        /// Idempotent, so calling it again never re-bases an in-progress flight.
        /// </summary>
        private void CaptureStartState()
        {
            if (_initialized) return;
            _initialized = true;

            _startPosition = transform.position;
            _startRotation = transform.rotation;

            // Resolve the authored axes into world space ONCE. Degenerate axes fall back to the
            // transform's own forward/up rather than producing a zero-length direction.
            Vector3 fwd = localForwardAxis.sqrMagnitude < 1e-8f
                ? Vector3.forward
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

        /// <summary>
        /// Advances the flight by <paramref name="deltaTime"/> seconds and writes the resulting
        /// position/rotation. Exposed so behaviour can be verified deterministically in tests
        /// (Edit Mode has no frame loop, and Time.deltaTime there is unreliable).
        /// </summary>
        public void AdvanceFlight(float deltaTime)
        {
            CaptureStartState();
            if (deltaTime <= 0f) return;

            _elapsed += deltaTime;

            float factor = ComputeSpeedFactor(_elapsed);

            // --- forward travel ---
            _distance += cruiseSpeed * factor * deltaTime;

            // --- gentle climb ---
            // Blending the cruise multiplier by the SAME eased factor keeps the vertical speed
            // continuous; switching multipliers at the phase boundary would visibly kink the climb.
            float riseMultiplier = Mathf.Lerp(1f, cruiseRiseMultiplier, factor);
            _rise += verticalRiseSpeed * factor * riseMultiplier * deltaTime;
            if (maxRiseHeight > 0f) _rise = Mathf.Min(_rise, maxRiseHeight);

            transform.position = _startPosition
                                 + _travelDirection * _distance
                                 + _riseDirection * _rise;

            // --- rotation: authoritative flight root keeps its authored orientation ---
            // In Micro Task #3, cosmetic pitch is owned by CinematicHelicopterVisualMotion on the
            // child model. If applyTakeoffPitch is explicitly enabled (legacy standalone mode),
            // apply it here; otherwise keep _startRotation so the flight root is purely authoritative.
            transform.rotation = (applyTakeoffPitch && takeoffPitch != 0f)
                ? _startRotation * Quaternion.AngleAxis(takeoffPitch * factor, Vector3.right)
                : _startRotation;
        }

        /// <summary>
        /// 0 during the hold phase, SmoothStep-eased 0→1 across the acceleration window, then 1.
        /// SmoothStep gives zero velocity change at both ends, which is what makes the departure
        /// read as cinematic rather than mechanical.
        /// </summary>
        private float ComputeSpeedFactor(float elapsed)
        {
            if (elapsed <= startDelay) return 0f;
            if (accelerationDuration <= 0f) return 1f;

            float u = Mathf.Clamp01((elapsed - startDelay) / accelerationDuration);
            return Mathf.SmoothStep(0f, 1f, u);
        }

        /// <summary>Returns the flight root to its authored start transform and resets the clock.</summary>
        public void ResetFlight()
        {
            CaptureStartState();
            _elapsed = 0f;
            _distance = 0f;
            _rise = 0f;
            transform.position = _startPosition;
            transform.rotation = _startRotation;
        }
    }
}
