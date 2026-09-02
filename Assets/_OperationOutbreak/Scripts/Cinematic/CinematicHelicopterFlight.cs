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
    ///
    /// QA FIX #5D.1 — SCALED TIME IS THE SAFE DEFAULT
    /// ------------------------------------------------
    /// The #5D diagnostic trace proved the remaining Play-start jump: the first movement
    /// tick consumed dt = 6.43 s — the editor stall between pressing Play and the first
    /// Update — through Time.unscaledDeltaTime because "Use Unscaled Time" defaulted to
    /// true. One ~6.4 s tick skips GroundIdle + VerticalLift + ForwardTransition in a
    /// single integration step and lands the root in Cruise (~51 m, ~4 m up), far outside
    /// the starting camera shot. The serialized default of useUnscaledTime is therefore
    /// now FALSE (scaled Time.deltaTime), so a normal Play run can never consume the
    /// Play-start stall in one giant tick. The field remains serialized, so unscaled
    /// behavior can still be opted into explicitly from the Inspector when progress while
    /// Time.timeScale = 0 is required.
    ///
    /// AIRBORNE START MODE (optional, default OFF)
    /// -------------------------------------------
    /// startAirborne = true begins the cinematic with the helicopter ALREADY in
    /// forward/cruise flight from its authored scene transform (pre-placed outside the
    /// camera frame at flight altitude): GroundIdle, VerticalLift, the takeoff pitch and
    /// the takeoff acceleration staging are all skipped (lift factor 0, speed factor 1
    /// from the first frame, rotation stays the authored start rotation), and the
    /// existing shared movement path (cruise speed, gentle capped cruise rise,
    /// straight travel along the authored forward axis) runs unchanged. The flight
    /// origin is always the authored transform (the same session self-heal invariants
    /// from QA #5A/#5C apply; no teleport/reset to any other position). With the flag
    /// OFF (the default) the existing 4-phase takeoff behavior is fully preserved.
    ///
    /// CINEMATIC TURN (optional, default OFF)
    /// --------------------------------------
    /// enableTurn = true adds a one-off cinematic right turn on the SAME flight clock
    /// (no second timer): before turnStartTime the existing path and authored rotation
    /// run unchanged; during [turnStartTime, turnStartTime + turnDuration] the yaw is
    /// smoothly eased (smoothstep, never a linear snap) from 0 to turnYawDegrees and a
    /// temporary bank rises with a sine (0 -> peak at mid-turn -> 0 by the end), applied
    /// in the correct direction for the turn; after the window the final yaw is kept and
    /// the flight continues in a straight line along the NEW heading — nothing resets to
    /// the old direction.
    ///
    /// Sign convention: the forward axis is LOCAL (localForwardAxis, default +X — never
    /// assumed to be world Z). POSITIVE turnYawDegrees = visually correct RIGHT turn
    /// relative to that forward axis: the positive yaw is a rotation about the authored
    /// up axis from forward toward the aircraft's right side (up x forward), verified
    /// for the default axes as +X -> (cos yaw, 0, -sin yaw). The bank uses the
    /// right-handed convention about the current heading: negative angle = right wing
    /// down, so a right turn banks right. Movement curves through space (the arc is
    /// integrated along the evolving heading onto the fixed authored base/right axes —
    /// a pure function of the input/time sequence, no cumulative transform drift), and
    /// the existing cruise speed, cruise-rise scaling and maxRiseHeight cap all apply
    /// unchanged. ResetFlight and the #5C session self-heal zero the turn arc
    /// accumulators so a reset/new session starts exactly at the authored transform
    /// with zero turn progress. turnStartTime is clamped to >= 0 and turnDuration to a
    /// safe minimum (0.05s) so zero/negative values degrade safely (instant-but-smooth
    /// clamp, never a divide-by-zero or inverted window).
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

        [Tooltip("Use unscaled time so the flight is unaffected by Time.timeScale. Off by default: " +
                 "with unscaled time ON, the multi-second editor stall between pressing Play and the " +
                 "first Update is consumed in a single first tick (QA diagnostic #5D measured dt = 6.43s), " +
                 "which skips the takeoff phases and teleports the helicopter into Cruise. Enable only if " +
                 "the flight must keep progressing while Time.timeScale is 0.")]
        [SerializeField] private bool useUnscaledTime = false;

        [Header("Airborne Start (optional)")]
        [Tooltip("When true, the helicopter starts ALREADY in full forward/cruise flight from its authored " +
                 "scene transform: no ground idle, no vertical lift, no takeoff pitch, no takeoff " +
                 "acceleration staging. Use this when the root is pre-placed outside the camera frame at " +
                 "flight altitude (e.g. just left of the exterior shot) so it enters the frame in full " +
                 "flight. Default false = the existing ground takeoff behavior.")]
        [SerializeField] private bool startAirborne = false;

        [Header("Cinematic Turn (optional)")]
        [Tooltip("When true, after turnStartTime the helicopter smoothly yaws by turnYawDegrees over " +
                 "turnDuration seconds, banks up to turnBankDegrees into the turn (peaking mid-turn, back " +
                 "to zero by the end), then continues along the NEW heading — the path curves through " +
                 "space, it is not just the model rotating. Default false = no turn (existing straight " +
                 "flight unchanged).")]
        [SerializeField] private bool enableTurn = false;

        [Tooltip("Seconds from Play (same elapsed clock as the takeoff phases) before the turn begins. " +
                 "Clamped to >= 0.")]
        [SerializeField] private float turnStartTime = 4f;

        [Tooltip("Seconds over which the yaw (and bank in/out) ease with a smooth curve. Clamped to a " +
                 "safe minimum of 0.05s.")]
        [SerializeField] private float turnDuration = 1.75f;

        [Tooltip("Total yaw in degrees. POSITIVE = RIGHT turn relative to the Local Forward Axis " +
                 "(default +X: positive yaw turns the nose toward the helicopter's right side); negative " +
                 "= left turn.")]
        [SerializeField] private float turnYawDegrees = 40f;

        [Tooltip("Peak bank (roll) in degrees, applied into the turn: 0 at start, peak at mid-turn, 0 " +
                 "at the end. Applied in the correct bank direction for the turn direction (right turn " +
                 "-> right bank).")]
        [SerializeField] private float turnBankDegrees = 10f;

        // Retained for backward compatibility / tests:
        [SerializeField] private float startDelay = 0.75f;
        [SerializeField] private float accelerationDuration = 2.5f;

        // ---- runtime state ----
        private bool _initialized;
        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private Vector3 _travelDirection;   // world-space, fixed at start
        private Vector3 _riseDirection;     // world-space, fixed at start
        private Vector3 _turnRightDirection; // world-space, fixed at start: rise x travel = aircraft RIGHT
        private float _elapsed;
        private float _distance;            // straight metres travelled along the base forward axis
        private float _turnBaseDist;        // arc metres along the base forward axis during/after the turn
        private float _turnRightDist;       // arc metres along the right axis during/after the turn
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
                // Airborne-start mode is already in cruise from t = 0: the takeoff phase windows
                // are skipped entirely, so the phase is Cruise for the whole run.
                if (startAirborne) return FlightPhase.Cruise;

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

        // ---- cinematic turn (optional; pure functions of the flight clock => deterministic) ----

        /// <summary>Safe minimum turn window (seconds) — the clamped duration is never below this.</summary>
        private const float MinTurnDuration = 0.05f;

        /// <summary>turnStartTime clamped to >= 0 (negative values would otherwise start the turn before Play).</summary>
        private float EffectiveTurnStart => Mathf.Max(0f, turnStartTime);

        /// <summary>turnDuration clamped to a safe minimum so zero/negative values can never divide to zero or invert the window.</summary>
        private float EffectiveTurnDuration => Mathf.Max(MinTurnDuration, turnDuration);

        /// <summary>
        /// Signed yaw in degrees currently applied by the cinematic turn: 0 before the window,
        /// smoothly eased (smoothstep) to turnYawDegrees across the window, then constant.
        /// Sign convention: POSITIVE = RIGHT turn relative to the Local Forward Axis
        /// (a positive angle about the authored up axis turns the nose from forward toward the
        /// aircraft's right side = up x forward).
        /// </summary>
        public float CurrentTurnYawDegrees
        {
            get
            {
                if (!enableTurn)
                    return 0f;

                float u = Mathf.Clamp01((_elapsed - EffectiveTurnStart) / EffectiveTurnDuration);
                if (u <= 0f)
                    return 0f;
                if (u >= 1f)
                    return turnYawDegrees;

                return turnYawDegrees * Mathf.SmoothStep(0f, 1f, u);
            }
        }

        /// <summary>
        /// Signed bank (roll) in degrees currently applied by the cinematic turn: 0 outside the
        /// window, rising with a sine (0 -> peak at mid-turn -> 0). Sign convention (right-handed
        /// rotation about the CURRENT heading axis): NEGATIVE = RIGHT bank (right wing down), which
        /// is the correct lean into a RIGHT turn, so for positive turnYawDegrees the value returned
        /// here is negative during the turn. A negative turn banks left instead.
        /// </summary>
        public float CurrentTurnBankDegrees
        {
            get
            {
                if (!enableTurn)
                    return 0f;

                float u = Mathf.Clamp01((_elapsed - EffectiveTurnStart) / EffectiveTurnDuration);
                if (u <= 0f || u >= 1f)
                    return 0f;
                if (Mathf.Approximately(turnYawDegrees, 0f))
                    return 0f; // degenerate config: no yaw, no bank wobble

                float lean = turnBankDegrees * Mathf.Sin(Mathf.PI * u);
                return turnYawDegrees > 0f ? -lean : lean;
            }
        }

        /// <summary>0 during GroundIdle and VerticalLift, smoothly easing 0→1 during ForwardTransition, then 1. Always 1 in airborne-start mode.</summary>
        public float SpeedFactor => startAirborne ? 1f : ComputeSpeedFactor(_elapsed);

        /// <summary>Current forward speed in metres/second.</summary>
        public float CurrentSpeed => cruiseSpeed * SpeedFactor;

        /// <summary>0 during GroundIdle, smoothly easing 0→1 during VerticalLift, then 1. Always 0 in airborne-start mode (no vertical lift).</summary>
        public float VerticalLiftFactor => startAirborne ? 0f : ComputeVerticalLiftFactor(_elapsed);

        public bool FlightEnabled
        {
            get => flightEnabled;
            set => flightEnabled = value;
        }

        /// <summary>Optional airborne-start mode (default false = existing takeoff behavior).</summary>
        public bool StartAirborne
        {
            get => startAirborne;
            set => startAirborne = value;
        }

        // ---- cinematic turn views (default off = no turn) ----

        public bool EnableTurn
        {
            get => enableTurn;
            set => enableTurn = value;
        }

        public float TurnStartTime
        {
            get => turnStartTime;
            set => turnStartTime = value;
        }

        public float TurnDuration
        {
            get => turnDuration;
            set => turnDuration = value;
        }

        public float TurnYawDegrees
        {
            get => turnYawDegrees;
            set => turnYawDegrees = value;
        }

        public float TurnBankDegrees
        {
            get => turnBankDegrees;
            set => turnBankDegrees = value;
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
            _turnBaseDist = 0f;
            _turnRightDist = 0f;
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

            // Aircraft RIGHT in world space = rise x travel (verified: default axes +X forward /
            // +Y up -> right = -Z, i.e. the helicopter's right side when facing +X). Fixed at
            // start like the other axes: used to integrate the cinematic turn arc, so the
            // right-turn sign convention holds for any authored root orientation.
            Vector3 right = Vector3.Cross(_riseDirection, _travelDirection);
            _turnRightDirection = right.sqrMagnitude < 1e-8f ? Vector3.up : right.normalized;
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

            // Airborne-start mode skips the takeoff staging entirely (ground idle, vertical
            // lift, takeoff pitch, acceleration ramp): the helicopter is already airborne in
            // full cruise flight from its authored transform, so lift stays at 0 and the
            // speed factor is 1 from the first frame. The shared movement path below
            // (distance/rise integration, transform write) is otherwise unchanged.
            float liftFactor = startAirborne ? 0f : ComputeVerticalLiftFactor(_elapsed);
            float baseLift = initialLiftHeight * liftFactor;

            // Phase 3 & 4: Forward Acceleration & Climb
            float speedFactor = startAirborne ? 1f : ComputeSpeedFactor(_elapsed);

            // Forward translation only occurs once airborne in forward transition.
            //
            // Cinematic turn: BEFORE the turn window opens, travel is exactly the existing
            // straight accumulation along the base forward axis. Once the window opens (and
            // stays open), each tick adds the arc length along the EVOLVING heading, projected
            // onto the fixed authored base/right axes (cos/sin of the current eased yaw). The
            // heading integrates as H(t) = base * cos(yaw) + right * sin(yaw), so with a full
            // 90-degree yaw the path becomes a true circular arc through space — and after the
            // window closes the yaw is constant, so the helicopter continues in a straight
            // line along the NEW heading. Everything is a deterministic function of the same
            // input/time sequence (no per-frame multiplication of the current transform
            // rotation, therefore no cumulative drift).
            float forwardSpeed = cruiseSpeed * speedFactor;
            if (enableTurn && _elapsed >= EffectiveTurnStart)
            {
                float yawRad = CurrentTurnYawDegrees * Mathf.Deg2Rad;
                _turnBaseDist += forwardSpeed * Mathf.Cos(yawRad) * deltaTime;
                _turnRightDist += forwardSpeed * Mathf.Sin(yawRad) * deltaTime;
            }
            else
            {
                _distance += forwardSpeed * deltaTime;
            }

            // Additional gentle climb during forward travel
            if (speedFactor > 0f)
            {
                float riseMultiplier = Mathf.Lerp(1f, cruiseRiseMultiplier, speedFactor);
                _forwardClimb += verticalRiseSpeed * speedFactor * riseMultiplier * deltaTime;
            }

            _rise = baseLift + _forwardClimb;
            if (maxRiseHeight > 0f) _rise = Mathf.Min(_rise, maxRiseHeight);

            // Position = authored start + straight base travel + curved turn arc + rise. All
            // terms use the axes fixed at start, so the result is authored-relative and
            // deterministic (turn off => the turn terms are zero and this is the existing write).
            transform.position = _startPosition
                                 + _travelDirection * (_distance + _turnBaseDist)
                                 + _turnRightDirection * _turnRightDist
                                 + _riseDirection * _rise;

            // Rotation = authored start rotation + smooth turn yaw (about the authored up axis)
            // + temporary bank (about the CURRENT heading). Both are pure functions of the
            // flight clock, so the write is deterministic and drift-free: at the end of the
            // turn it is exactly start + final yaw + zero bank.
            Quaternion rotation = _startRotation;
            if (enableTurn)
            {
                float yawAngle = CurrentTurnYawDegrees;
                if (yawAngle != 0f)
                {
                    Quaternion yawQuat = Quaternion.AngleAxis(yawAngle, _riseDirection);
                    rotation = yawQuat * rotation;

                    float bankAngle = CurrentTurnBankDegrees;
                    if (bankAngle != 0f)
                    {
                        // Bank about the yawed heading (right turn => negative angle => right
                        // wing down, verified against Unity's right-handed AngleAxis).
                        Vector3 heading = yawQuat * _travelDirection;
                        rotation = Quaternion.AngleAxis(bankAngle, heading) * rotation;
                    }
                }
            }

            // Takeoff pitch is takeoff staging — never applied in airborne-start mode, so
            // the rotation stays exactly the authored start rotation there.
            if (!startAirborne && applyTakeoffPitch && takeoffPitch != 0f)
                rotation = rotation * Quaternion.AngleAxis(takeoffPitch * speedFactor, Vector3.right);

            transform.rotation = rotation;
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
            _turnBaseDist = 0f;
            _turnRightDist = 0f;
            _forwardClimb = 0f;
            _rise = 0f;
            transform.position = _startPosition;
            transform.rotation = _startRotation;
        }
    }
}
