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
    ///
    /// MICRO TASK #5A FIX — START TRANSFORM IS ALWAYS THE AUTHORED TRANSFORM AT PLAY
    /// -------------------------------------------------------------------------------
    /// The start transform and the phase clock are re-captured/reset for every new Play session,
    /// not only in Awake. Unity's Enter Play Mode Options can disable Domain Reload and/or Scene
    /// Reload, in which case Awake is NOT called again on the next Play and the one-shot capture
    /// would keep the stale end-of-flight position, the stale elapsed time, and the stale
    /// accumulated distance from the previous session — making the helicopter instantly
    /// reappear far away instead of performing GroundIdle -> VerticalLift from its authored
    /// position.
    ///
    /// QA FIX #5C — THE FIRST MOVEMENT TICK SELF-HEALS STALE SESSION STATE
    /// ------------------------------------------------------------------
    /// Manual runtime QA proved the #5A OnEnable-only re-arm is NOT sufficient: with Domain
    /// Reload / Scene Reload disabled, OnEnable is NOT guaranteed to fire again at Play entry
    /// (the component instance persists without an enable transition), so the previous
    /// session's _elapsed (~9.4s), _distance (~41.6m), _forwardClimb/_rise (~4.0m) and
    /// _initialized flag reached the first Update of the new session and teleported the root
    /// ~41.6m forward and ~4m up on frame 1. The invariant is therefore enforced where it
    /// matters: <see cref="AdvanceFlight"/> begins with EnsureCurrentPlaySessionInitialized(),
    /// which compares the instance session token against the play-session generation and, on
    /// mismatch, captures the CURRENT authored transform, recomputes the travel/up directions,
    /// and zeroes the phase clock and all accumulated values BEFORE any phase calculation,
    /// elapsed advancement, distance/rise integration, or transform write. The first frame of
    /// every new session thus writes exactly the authored transform. Awake, Start and OnEnable
    /// are no longer relied upon for this invariant.
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

        // ---- play-session guards (Micro Task #5A) ----
        // These make the one-shot start capture robust against Unity's "Enter Play Mode Options":
        // when Domain Reload and/or Scene Reload are disabled, scene objects and their instance
        // fields persist between Play sessions, so Awake may not run again. OnEnable is still
        // called at every Play entry; a session counter bumped from a RuntimeInitializeOnLoadMethod
        // lets OnEnable distinguish "a brand-new Play session" (re-capture) from "component toggled
        // mid-Play" (keep the in-flight state).
        private static int _sessionCounter;
        private int _playSessionId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnNewPlaySession() => _sessionCounter++;

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

        /// <summary>
        /// QA Fix #5C — the authoritative fresh-session guarantee, run at the start of EVERY
        /// movement tick (and best-effort in OnEnable).
        ///
        /// When Enter Play Mode Options disable Domain Reload and/or Scene Reload, this
        /// component's instance fields PERSIST from the previous Play session, and OnEnable is
        /// NOT guaranteed to fire again at Play entry — so the Micro Task #5A OnEnable re-arm
        /// alone could be skipped entirely. If this instance's session token does not match the
        /// current play-session generation, ALL stale state is discarded BEFORE any phase
        /// calculation, elapsed advancement, distance/rise integration, or transform write:
        ///   1. the CURRENT authored transform is captured as the new start,
        ///   2. travel/up directions are recomputed from that authored state,
        ///   3. the phase clock and every accumulated distance/rise value are zeroed,
        /// so the first frame of the new session writes exactly the authored transform
        /// (GroundIdle, zero displacement) instead of teleporting to the previous session's
        /// end-of-flight position. Component toggles mid-Play (same generation) are untouched.
        /// </summary>
        private void EnsureCurrentPlaySessionInitialized()
        {
            if (_playSessionId == _sessionCounter) return;
            _playSessionId = _sessionCounter;

            _elapsed = 0f;
            _distance = 0f;
            _forwardClimb = 0f;
            _rise = 0f;
            RecaptureStartState();
        }

        private void OnEnable()
        {
            // Runs at the start of every Play session in the configurations where OnEnable
            // does fire (domain-reload builds, mid-Play re-enables) and re-arms early. It is
            // NOT the authoritative guarantee, though: OnEnable is not guaranteed to fire at
            // every Enter Play Mode, so AdvanceFlight calls the same self-heal on the first
            // movement tick of every new session (QA Fix #5C).
            EnsureCurrentPlaySessionInitialized();
        }

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

        /// <summary>
        /// Forces the CURRENT transform to become the new authored start transform.
        /// Used by <see cref="OnEnable"/> when a new Play session begins so the flight can never
        /// depart from a stale position captured in a previous session.
        /// </summary>
        public void RecaptureStartState()
        {
            _initialized = false;
            CaptureStartState();
        }

        private void Update()
        {
            if (!flightEnabled) return;
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            AdvanceFlight(dt);
        }

        public void AdvanceFlight(float deltaTime)
        {
            // QA Fix #5C — FIRST line of the movement path: the first tick of a new Play session
            // must discard any stale previous-session state (elapsed, distance, rise, start
            // transform) BEFORE any trajectory calculation. This makes the invariant hold
            // regardless of whether OnEnable fired at Play entry.
            EnsureCurrentPlaySessionInitialized();

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
