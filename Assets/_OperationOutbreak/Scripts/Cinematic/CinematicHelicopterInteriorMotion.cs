using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Interior Micro Task #2 — SUBTLE FLYING CABIN MOTION (scripting only).
    ///
    /// Attach to <c>HelicopterInterior_Manual</c>, the manually authored root that parents ALL
    /// interior visual objects (cabin shell, bench, lights, player, rifle). The component moves
    /// ONLY that root; every child rides along through the normal parent/child transform, so the
    /// seated player can never slide relative to the bench and the cabin camera can never drift
    /// away from the cabin.
    ///
    /// BEHAVIOR (deterministic, drift-free by construction)
    /// ----------------------------------------------------
    /// On startup the component captures the root's AUTHORED local position and local rotation.
    /// Every frame it then writes
    ///
    ///     localPosition = authoredLocalPosition + offset(t)
    ///     localRotation = authoredLocalRotation * rotationOffset(t)
    ///
    /// where t is the elapsed motion time and both offsets are a PURE FUNCTION of t — the
    /// transform is never accumulated frame-to-frame, so the root can never drift: at any
    /// moment its deviation from the authored pose is bounded by the configured amplitudes,
    /// for all time.
    ///
    /// LAYERED MOTION (multiple detuned frequencies, so it never reads as one obvious sine):
    ///   - vertical bob       — local +Y, small amplitude
    ///   - roll sway          — local Z rotation, tiny angle (vibration/turbulence feel)
    ///   - pitch sway         — local X rotation, even tinier angle
    ///   - forward/back micro — local +X, extremely subtle (optional realism layer)
    ///   - micro vibration    — local Y/X position, high frequency, very small
    /// Each layer is a two-tone sum: a primary sine plus a detuned secondary sine (ratio 1.618)
    /// whose weights sum to 1, so the layer's peak is bounded by its configured amplitude and
    /// the sum is exactly zero at t = 0 (the root starts exactly at its authored pose).
    ///
    /// SCOPE
    /// -----
    /// Scripting only. It never creates/modifies geometry, materials, or lights; never parents
    /// or unparents anything; never searches for, or references, the player, the seated
    /// animation, the rifle, or any camera. With Motion Enabled off (or on disable) the root is
    /// held/restored exactly at its authored pose.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Helicopter Interior Motion")]
    public sealed class CinematicHelicopterInteriorMotion : MonoBehaviour
    {
        // Two-tone layer: primary weight + secondary weight == 1, so the layer's maximum
        // deviation is bounded by the configured amplitude (triangle inequality). The secondary
        // sine is detuned (golden-ratio frequency ratio) so the layer does not look like a single
        // sine wave. Both sines have zero phase, so every offset is exactly 0 at t = 0.
        private const float PrimaryWeight = 0.65f;
        private const float SecondaryWeight = 0.35f;
        private const float SecondaryFrequencyRatio = 1.618f;

        [Header("Motion")]
        [Tooltip("Master switch. When off, the root is held EXACTLY at its authored pose.")]
        [SerializeField] private bool motionEnabled = true;

        [Tooltip("Use unscaled time so the motion continues while Time.timeScale is 0. Off by " +
                 "default (same safe default as the helicopter flight, QA #5D.1): with unscaled " +
                 "time ON, the Play-start editor stall is consumed in one giant first tick.")]
        [SerializeField] private bool useUnscaledTime = false;

        [Header("Vertical Bob (metres)")]
        [Tooltip("Peak vertical (local +Y) bobbing amplitude in metres.")]
        [SerializeField] private float bobAmplitude = 0.025f;

        [Tooltip("Vertical bob frequency in Hz.")]
        [SerializeField] private float bobFrequency = 1.2f;

        [Header("Roll Sway (degrees)")]
        [Tooltip("Peak roll (local Z) sway in degrees — the helicopter vibration/turbulence feel.")]
        [SerializeField] private float rollAmplitude = 0.45f;

        [Tooltip("Roll sway frequency in Hz.")]
        [SerializeField] private float rollFrequency = 0.8f;

        [Header("Pitch Sway (degrees)")]
        [Tooltip("Peak pitch (local X) sway in degrees — smaller than roll by design.")]
        [SerializeField] private float pitchAmplitude = 0.25f;

        [Tooltip("Pitch sway frequency in Hz.")]
        [SerializeField] private float pitchFrequency = 0.65f;

        [Header("Forward/Back Micro Motion (metres, optional)")]
        [Tooltip("Extremely subtle forward/back (local +X) drift amplitude in metres. 0 disables it.")]
        [SerializeField] private float forwardAmplitude = 0.01f;

        [Tooltip("Forward/back micro-motion frequency in Hz.")]
        [SerializeField] private float forwardFrequency = 0.5f;

        [Header("Micro Vibration (metres)")]
        [Tooltip("High-frequency micro vibration amplitude in metres (local Y + X). 0 disables it.")]
        [SerializeField] private float microVibrationAmplitude = 0.006f;

        [Tooltip("Micro vibration frequency in Hz.")]
        [SerializeField] private float microVibrationFrequency = 7.0f;

        // ---- runtime state ----
        private bool _poseCaptured;
        private Vector3 _authoredLocalPosition;
        private Quaternion _authoredLocalRotation;
        private float _elapsed;

        /// <summary>Master switch. Toggling it off snaps the root back to the authored pose.</summary>
        public bool MotionEnabled
        {
            get => motionEnabled;
            set => motionEnabled = value;
        }

        /// <summary>Seconds of motion accumulated since startup (read-only).</summary>
        public float Elapsed => _elapsed;

        private void Update()
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            AdvanceMotion(dt);
        }

        /// <summary>
        /// Captures the root's CURRENT local position/rotation as the new authored pose.
        /// Called automatically on the first movement frame; call it again after re-authoring
        /// the root in Play if the base pose should change.
        /// </summary>
        public void CaptureAuthoredPose()
        {
            _authoredLocalPosition = transform.localPosition;
            _authoredLocalRotation = transform.localRotation;
            _poseCaptured = true;
        }

        private void EnsureAuthoredPoseCaptured()
        {
            if (!_poseCaptured) CaptureAuthoredPose();
        }

        /// <summary>
        /// Advances the motion clock by <paramref name="deltaTime"/> seconds and writes the root
        /// pose as authored pose + a pure function of the elapsed time (never accumulated).
        /// </summary>
        public void AdvanceMotion(float deltaTime)
        {
            EnsureAuthoredPoseCaptured();

            if (!motionEnabled)
            {
                // Motion off: hold EXACTLY the authored pose (a toggle must not freeze the
                // root at a mid-offset).
                transform.localPosition = _authoredLocalPosition;
                transform.localRotation = _authoredLocalRotation;
                return;
            }

            if (deltaTime > 0f) _elapsed += deltaTime;
            ApplyMotion(_elapsed);
        }

        private void ApplyMotion(float t)
        {
            // Pure function of elapsed time — nothing is accumulated into the transform.
            // Local axes: +Y up (bob / vibration), +X forward (micro travel), Z roll, X pitch.
            float bob = TwoToneSine(bobAmplitude, bobFrequency, t);
            float forward = TwoToneSine(forwardAmplitude, forwardFrequency, t);
            float vibrateY = TwoToneSine(microVibrationAmplitude, microVibrationFrequency, t);
            float vibrateX = TwoToneSine(microVibrationAmplitude,
                microVibrationFrequency * SecondaryFrequencyRatio, t);

            Vector3 offset = Vector3.up * (bob + vibrateY)
                           + Vector3.right * (forward + vibrateX);

            float rollDeg = TwoToneSine(rollAmplitude, rollFrequency, t);
            float pitchDeg = TwoToneSine(pitchAmplitude, pitchFrequency, t);
            Quaternion rotationOffset = Quaternion.Euler(pitchDeg, 0f, rollDeg);

            transform.localPosition = _authoredLocalPosition + offset;
            transform.localRotation = _authoredLocalRotation * rotationOffset;
        }

        private void OnDisable()
        {
            // Clean stop: a disabled component leaves the root exactly at its authored pose,
            // never at a mid-offset.
            if (_poseCaptured)
            {
                transform.localPosition = _authoredLocalPosition;
                transform.localRotation = _authoredLocalRotation;
            }
        }

        /// <summary>
        /// Two-tone sine bounded by <paramref name="amplitude"/>: a primary sine plus a detuned
        /// secondary (frequency × 1.618) whose weights sum to 1. Exactly 0 at t = 0.
        /// </summary>
        private static float TwoToneSine(float amplitude, float frequency, float t)
        {
            float primary = Mathf.Sin(2f * Mathf.PI * frequency * t);
            float secondary = Mathf.Sin(2f * Mathf.PI * frequency * SecondaryFrequencyRatio * t);
            return amplitude * (PrimaryWeight * primary + SecondaryWeight * secondary);
        }
    }
}
