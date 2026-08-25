using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Micro task #3 — subtle realistic visual motion (takeoff pitch, vertical bob, and roll)
    /// for the cinematic helicopter visual model (<c>helicopter_rigged</c>).
    ///
    /// ARCHITECTURAL SEPARATION
    /// ------------------------
    /// - <c>HelicopterFlightRoot</c> is authoritative for translation and world trajectory
    ///   via <see cref="CinematicHelicopterFlight"/>.
    /// - <c>helicopter_rigged</c> (this component) owns purely COSMETIC visual attitude:
    ///     1. Smooth takeoff/forward pitch (nose-down along the verified model forward axis).
    ///     2. Subtle vertical bob (smooth deterministic sinusoidal motion).
    ///     3. Subtle roll/tilt variation (smooth deterministic sinusoidal motion).
    ///
    /// ZERO CUMULATIVE DRIFT
    /// ---------------------
    /// On startup the authored local transform is captured ONCE:
    ///     <see cref="_baseLocalPosition"/> and <see cref="_baseLocalRotation"/>.
    /// Every frame computes offsets strictly FROM those base values:
    ///     transform.localPosition = baseLocalPosition + calculatedBobOffset;
    ///     transform.localRotation = baseLocalRotation * calculatedVisualRotation;
    /// The transform is NEVER stepped cumulatively with += or Rotate(), eliminating drift.
    ///
    /// ROTOR & CHILD COMPONENT SAFETY
    /// ------------------------------
    /// This component never touches child transforms (<c>rotor_up</c>, <c>rotor_tail</c>,
    /// <c>mian_body</c>) or <see cref="CinematicHelicopterRotorSpin"/>. Because the rotors are
    /// children of <c>helicopter_rigged</c>, they inherit the helicopter body's visual attitude
    /// while spinning independently in their own local frames.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Helicopter Visual Motion")]
    public sealed class CinematicHelicopterVisualMotion : MonoBehaviour
    {
        [Header("Takeoff Pitch")]
        [Tooltip("Target cosmetic forward pitch in degrees during flight/acceleration (4 to 6 degrees). " +
                 "Positive pitches nose down along the forward axis.")]
        [SerializeField] private float takeoffPitchDegrees = 5f;

        [Tooltip("Seconds to hold before visual pitch eases in (matches flight startDelay). " +
                 "Used when no parent flight component is linked.")]
        [SerializeField] private float pitchStartDelay = 0.75f;

        [Tooltip("Seconds to smoothly ease pitch up to takeoffPitchDegrees (matches flight accelerationDuration). " +
                 "Used when no parent flight component is linked.")]
        [SerializeField] private float pitchAccelerationDuration = 2.5f;

        [Header("Vertical Bob")]
        [Tooltip("Maximum vertical bob displacement in metres (0.05 to 0.10m).")]
        [SerializeField] private float bobAmplitude = 0.06f;

        [Tooltip("Oscillation frequency of the vertical bob in Hz (approx 1.0 to 1.5 Hz).")]
        [SerializeField] private float bobFrequency = 1.2f;

        [Header("Subtle Roll / Tilt")]
        [Tooltip("Maximum subtle roll angle in degrees (0.5 to 1.0 degrees).")]
        [SerializeField] private float rollAmplitude = 0.8f;

        [Tooltip("Oscillation frequency of the subtle roll in Hz (approx 0.6 to 1.0 Hz).")]
        [SerializeField] private float rollFrequency = 0.8f;

        [Header("Startup Blend")]
        [Tooltip("Seconds over which visual bob and roll smoothly blend in from 0 to full amplitude. " +
                 "Guarantees zero sudden position or rotation jump at t = 0.")]
        [SerializeField] private float visualBlendDuration = 1.2f;

        [Header("Axes (Local to helicopter_rigged)")]
        [Tooltip("Local axis representing the model forward direction. Verified for this model as (1, 0, 0).")]
        [SerializeField] private Vector3 localForwardAxis = new Vector3(1f, 0f, 0f);

        [Tooltip("Local axis representing the model up direction. Default (0, 1, 0).")]
        [SerializeField] private Vector3 localUpAxis = new Vector3(0f, 1f, 0f);

        [Tooltip("Local axis around which forward/takeoff pitch rotates. For forward=(1,0,0) and up=(0,1,0), " +
                 "the correct pitch axis for nose-down tilt is (0, 0, -1). If zero, auto-derived from Cross(up, forward).")]
        [SerializeField] private Vector3 pitchAxis = new Vector3(0f, 0f, -1f);

        [Tooltip("Local axis around which subtle roll rotates. Defaults to local forward axis (1, 0, 0). " +
                 "If zero, falls back to localForwardAxis.")]
        [SerializeField] private Vector3 rollAxis = new Vector3(1f, 0f, 0f);

        [Tooltip("Local axis along which vertical bob is applied. Defaults to local up axis (0, 1, 0). " +
                 "If zero, falls back to localUpAxis.")]
        [SerializeField] private Vector3 bobAxis = new Vector3(0f, 1f, 0f);

        [Header("Flight Synchronization (Optional)")]
        [Tooltip("Optional reference to the parent CinematicHelicopterFlight. If null, automatically found " +
                 "in parent. When linked, visual pitch factor synchronizes directly with flight acceleration.")]
        [SerializeField] private CinematicHelicopterFlight flight;

        [Header("Control")]
        [Tooltip("Uncheck to freeze visual motion without removing the component.")]
        [SerializeField] private bool motionEnabled = true;

        [Tooltip("Use unscaled time so visual motion continues during cinematics / time pause.")]
        [SerializeField] private bool useUnscaledTime = true;

        // ---- runtime state (captured once on first tick, never authored) ----
        private bool _initialized;
        private Vector3 _baseLocalPosition;
        private Quaternion _baseLocalRotation;
        private float _elapsed;

        // ---- public properties ----
        public Vector3 BaseLocalPosition => _baseLocalPosition;
        public Quaternion BaseLocalRotation => _baseLocalRotation;
        public float Elapsed => _elapsed;
        public bool IsInitialized => _initialized;

        public bool MotionEnabled
        {
            get => motionEnabled;
            set
            {
                motionEnabled = value;
                if (!motionEnabled && _initialized)
                {
                    transform.localPosition = _baseLocalPosition;
                    transform.localRotation = _baseLocalRotation;
                }
            }
        }

        public CinematicHelicopterFlight Flight
        {
            get => flight;
            set => flight = value;
        }

        public float TakeoffPitchDegrees
        {
            get => takeoffPitchDegrees;
            set => takeoffPitchDegrees = value;
        }

        public float BobAmplitude
        {
            get => bobAmplitude;
            set => bobAmplitude = value;
        }

        public float BobFrequency
        {
            get => bobFrequency;
            set => bobFrequency = value;
        }

        public float RollAmplitude
        {
            get => rollAmplitude;
            set => rollAmplitude = value;
        }

        public float RollFrequency
        {
            get => rollFrequency;
            set => rollFrequency = value;
        }

        public float VisualBlendDuration
        {
            get => visualBlendDuration;
            set => visualBlendDuration = value;
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

        public Vector3 PitchAxis
        {
            get => pitchAxis;
            set => pitchAxis = value;
        }

        public Vector3 RollAxis
        {
            get => rollAxis;
            set => rollAxis = value;
        }

        public Vector3 BobAxis
        {
            get => bobAxis;
            set => bobAxis = value;
        }

        private void Awake() => CaptureBaseState();

        private void OnDisable()
        {
            if (_initialized)
            {
                transform.localPosition = _baseLocalPosition;
                transform.localRotation = _baseLocalRotation;
            }
        }

        /// <summary>
        /// Captures the neutral authored local transform and resolves parent flight reference.
        /// Idempotent and safe to call repeatedly.
        /// </summary>
        public void CaptureBaseState()
        {
            if (_initialized) return;
            _initialized = true;

            _baseLocalPosition = transform.localPosition;
            _baseLocalRotation = transform.localRotation;

            if (flight == null)
                flight = GetComponentInParent<CinematicHelicopterFlight>();
        }

        /// <summary>
        /// Force re-captures the current local transform as the new neutral base state.
        /// </summary>
        public void RecaptureBaseState()
        {
            _baseLocalPosition = transform.localPosition;
            _baseLocalRotation = transform.localRotation;
            _initialized = true;
        }

        private void Update()
        {
            if (!motionEnabled) return;
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            AdvanceVisualMotion(dt);
        }

        /// <summary>
        /// Advances visual motion by <paramref name="deltaTime"/> seconds and writes the resulting
        /// local position and rotation. Exposed so behaviour can be verified deterministically in tests.
        /// </summary>
        public void AdvanceVisualMotion(float deltaTime)
        {
            CaptureBaseState();
            if (!motionEnabled) return;
            if (deltaTime < 0f) return;

            _elapsed += deltaTime;

            // Startup blend factor: smoothly blends periodic bob & roll from 0 to 1 over visualBlendDuration.
            // At t = 0 this evaluates to exactly 0, guaranteeing zero sudden jump or snap.
            float blendFactor = visualBlendDuration <= 0f
                ? 1f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_elapsed / visualBlendDuration));

            // --- 1. Vertical Bob ---
            Vector3 resolvedBobAxis = ResolveAxis(bobAxis, localUpAxis, Vector3.up);
            float bobOffset = (bobAmplitude == 0f || bobFrequency <= 0f)
                ? 0f
                : bobAmplitude * Mathf.Sin(2f * Mathf.PI * bobFrequency * _elapsed) * blendFactor;
            Vector3 calculatedBobOffset = resolvedBobAxis * bobOffset;

            // --- 2. Subtle Roll / Tilt ---
            Vector3 resolvedRollAxis = ResolveAxis(rollAxis, localForwardAxis, Vector3.right);
            float rollAngle = (rollAmplitude == 0f || rollFrequency <= 0f)
                ? 0f
                : rollAmplitude * Mathf.Sin(2f * Mathf.PI * rollFrequency * _elapsed) * blendFactor;
            Quaternion rollRotation = (rollAngle != 0f && resolvedRollAxis.sqrMagnitude > 1e-8f)
                ? Quaternion.AngleAxis(rollAngle, resolvedRollAxis)
                : Quaternion.identity;

            // --- 3. Forward / Takeoff Pitch ---
            float pitchFactor;
            if (flight != null)
            {
                pitchFactor = flight.FlightEnabled ? flight.SpeedFactor : 0f;
            }
            else
            {
                pitchFactor = ComputePitchFactor(_elapsed);
            }

            Vector3 resolvedPitchAxis = ResolvePitchAxis();
            float pitchAngle = takeoffPitchDegrees * pitchFactor;
            Quaternion pitchRotation = (pitchAngle != 0f && resolvedPitchAxis.sqrMagnitude > 1e-8f)
                ? Quaternion.AngleAxis(pitchAngle, resolvedPitchAxis)
                : Quaternion.identity;

            // --- 4. Apply strictly relative to captured neutral base (zero cumulative drift) ---
            transform.localPosition = _baseLocalPosition + calculatedBobOffset;
            transform.localRotation = _baseLocalRotation * (pitchRotation * rollRotation);
        }

        /// <summary>
        /// Computes the pitch factor (0 to 1) when standalone without a parent flight component.
        /// </summary>
        public float ComputePitchFactor(float elapsed)
        {
            if (elapsed <= pitchStartDelay) return 0f;
            if (pitchAccelerationDuration <= 0f) return 1f;

            float u = Mathf.Clamp01((elapsed - pitchStartDelay) / pitchAccelerationDuration);
            return Mathf.SmoothStep(0f, 1f, u);
        }

        /// <summary>
        /// Resolves the local pitch axis, falling back to Vector3.Cross(up, forward).
        /// For forward=(1,0,0) and up=(0,1,0), Cross yields (0, 0, -1).
        /// </summary>
        public Vector3 ResolvePitchAxis()
        {
            if (pitchAxis.sqrMagnitude > 1e-8f)
                return pitchAxis.normalized;

            Vector3 fwd = localForwardAxis.sqrMagnitude > 1e-8f ? localForwardAxis.normalized : Vector3.right;
            Vector3 up = localUpAxis.sqrMagnitude > 1e-8f ? localUpAxis.normalized : Vector3.up;
            Vector3 cross = Vector3.Cross(up, fwd);
            return cross.sqrMagnitude > 1e-8f ? cross.normalized : Vector3.forward;
        }

        private static Vector3 ResolveAxis(Vector3 axis, Vector3 primaryFallback, Vector3 ultimateFallback)
        {
            if (axis.sqrMagnitude > 1e-8f) return axis.normalized;
            if (primaryFallback.sqrMagnitude > 1e-8f) return primaryFallback.normalized;
            return ultimateFallback.normalized;
        }

        /// <summary>
        /// Resets the motion timer and restores the captured neutral local transform.
        /// </summary>
        public void ResetVisualMotion()
        {
            CaptureBaseState();
            _elapsed = 0f;
            transform.localPosition = _baseLocalPosition;
            transform.localRotation = _baseLocalRotation;
        }
    }
}
