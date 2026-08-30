using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Tiny reusable starter: plays an assigned <see cref="CinematicRadioDialogueController"/>
    /// when this cinematic begins.
    ///
    /// Behavior:
    /// - With playOnStart = true, the sequence start is requested on the first frame
    ///   (playOnStart is applied on the first AdvanceStarter call, which Update() drives in
    ///   play mode), then startDelay (if > 0) elapses, then PlaySequence() is called exactly once.
    /// - With playOnStart = false, nothing happens until Play() is called manually.
    /// - A missing controller reference fails safely (no exception, nothing queued); assigning
    ///   a controller later makes Play() work.
    /// - Duplicate starts are impossible: once a start has been requested (pending or fired),
    ///   further Play() calls are no-ops. If a delayed start is pending and Play() is called,
    ///   the pending countdown is simply left alone.
    /// - startDelay < 0 is clamped to 0 (start immediately, in the current call).
    /// - useUnscaledTime = false by default (scaled time); unscaled is an opt-in that applies
    ///   to the delay countdown only.
    ///
    /// Deterministic driver (EditMode-testable, same pattern as the dialogue controller):
    /// Update() calls <see cref="AdvanceStarter"/> with scaled/unscaled delta every frame;
    /// EditMode tests call it directly with fixed steps. No coroutines, no FindObjects, no
    /// singletons, no Resources, no scene-name dependency.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Dialogue Sequence Starter")]
    public sealed class CinematicDialogueSequenceStarter : MonoBehaviour
    {
        [Tooltip("The dialogue controller to start. Assigned in the Inspector (never discovered at runtime).")]
        [SerializeField] private CinematicRadioDialogueController dialogueController;

        [Tooltip("Start the assigned sequence automatically when this cinematic begins.")]
        [SerializeField] private bool playOnStart = true;

        [Tooltip("Seconds to wait before starting the sequence. Negative values are clamped to 0 (start immediately).")]
        [SerializeField] private float startDelay = 0f;

        [Tooltip("Use unscaled time for the start delay. Off by default (scaled time).")]
        [SerializeField] private bool useUnscaledTime = false;

        private bool _autoStartArmed = true;
        private bool _startIssued;
        private bool _startPending;
        private float _pendingTimeRemaining;

        /// <summary>True while a delayed start is counting down.</summary>
        public bool IsStartPending => _startPending;

        /// <summary>True once a start has been requested (pending or already fired).</summary>
        public bool IsStartIssued => _startIssued;

        /// <summary>Current time-mode setting (serialized default is false = scaled time).</summary>
        public bool UseUnscaledTime => useUnscaledTime;

        private void Update()
        {
            AdvanceStarter(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
        }

        /// <summary>
        /// Manually starts the assigned sequence (honoring startDelay), safely:
        /// a no-op while a start is already pending or was already issued (no duplicate starts),
        /// and a no-op while no controller is assigned (assigning one later still allows Play()).
        /// </summary>
        public void Play()
        {
            RequestStart();
        }

        /// <summary>
        /// Advances the starter by <paramref name="deltaTime"/> seconds — the single
        /// deterministic driver (Update in play mode, direct calls from EditMode tests).
        /// The first call applies the playOnStart check; subsequent calls run the pending
        /// start-delay countdown. Non-positive steps are ignored safely.
        /// </summary>
        public void AdvanceStarter(float deltaTime)
        {
            if (_autoStartArmed)
            {
                _autoStartArmed = false;
                if (playOnStart) RequestStart();
            }

            if (_startPending)
            {
                float t = deltaTime > 0f ? deltaTime : 0f;
                if (t >= _pendingTimeRemaining)
                {
                    StartNow();
                }
                else
                {
                    _pendingTimeRemaining -= t;
                }
            }
        }

        private void RequestStart()
        {
            if (_startIssued) return;              // no repeated/duplicate starts
            if (dialogueController == null) return; // fail safe; a later assignment still works

            _startIssued = true;
            float delay = Mathf.Max(0f, startDelay);
            if (delay > 0f)
            {
                _startPending = true;
                _pendingTimeRemaining = delay;
            }
            else
            {
                StartNow();
            }
        }

        private void StartNow()
        {
            _startPending = false;
            _pendingTimeRemaining = 0f;
            if (dialogueController != null)
                dialogueController.PlaySequence();
        }
    }
}
