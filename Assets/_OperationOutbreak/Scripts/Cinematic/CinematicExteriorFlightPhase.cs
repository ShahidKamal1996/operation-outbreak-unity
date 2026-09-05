using UnityEngine;
using UnityEngine.Events;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Exterior-to-story handoff for the Helicopter_Cinematic scene: while the exterior
    /// establishing helicopter flight is active, ALL radio dialogue and subtitle progression on
    /// the assigned <see cref="CinematicRadioDialogueController"/> is HELD (frozen), and the
    /// story resumes EXACTLY ONCE, when the exterior phase explicitly hands off to the
    /// interior/story phase.
    ///
    /// BEHAVIOR
    /// --------
    /// - On enable (Play start) the component enters the exterior phase and holds the assigned
    ///   dialogue runner: `dialogueRunner.DialogueHeld = true`. A sequence may still be STARTED
    ///   while held (e.g. by CinematicDialogueSequenceStarter with playOnStart) — it just stays
    ///   frozen at line 0: no line plays, no subtitle text is revealed, no voice/SFX, no
    ///   speaker talking gestures. COMMAND/RAVEN/KANE (or any) dialogue therefore cannot play
    ///   during the exterior establishing flight.
    /// - HandOffToStoryPhase() is the EXPLICIT exterior -> story handoff. It releases the hold
    ///   and invokes OnHandedOffToStory exactly once, no matter how many times it is called —
    ///   the story resumes exactly once and can never be restarted or double-advanced by
    ///   repeated handoffs.
    /// - EnterExteriorPhase() (re)enters the exterior phase (used by OnEnable and available for
    ///   re-entry): it re-arms the hold and clears the handed-off flag.
    ///
    /// BOUNDARIES
    /// ----------
    /// - Decoupled from the flight itself: it never references, moves, or configures
    ///   CinematicHelicopterFlight, so the authored airborne-start transform and Start Airborne
    ///   behavior are untouched. The "exterior flight is active" window is simply "from Play
    ///   until the explicit handoff".
    /// - No scene search, no Resources, no coroutines, no singletons: the dialogue runner is
    ///   Inspector-assigned (a late runtime assignment while the exterior phase is active arms
    ///   the hold immediately).
    /// - Skip behavior and all existing dialogue controller semantics (stop/restart/completion)
    ///   are preserved — the hold only gates progression.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Exterior Flight Phase")]
    public sealed class CinematicExteriorFlightPhase : MonoBehaviour
    {
        [Header("Story / Dialogue")]
        [Tooltip("The radio dialogue runner to hold while the exterior flight is active. Inspector-assigned; " +
                 "a late runtime assignment while the exterior phase is active arms the hold immediately.")]
        [SerializeField] private CinematicRadioDialogueController dialogueRunner;

        [Tooltip("Invoked exactly once when the exterior phase hands off to the story. Wire the " +
                 "interior/story-phase transition (cameras, fades, rig activation) to this event.")]
        [SerializeField] private UnityEvent onHandedOffToStory = new UnityEvent();

        // ---- runtime state (never serialized) ----
        private bool _holdArmed;
        private bool _handedOff;

        /// <summary>True while the exterior establishing flight is the active phase (until the handoff).</summary>
        public bool IsExteriorPhaseActive => !_handedOff;

        /// <summary>True once the explicit handoff to the interior/story phase has happened.</summary>
        public bool HasHandedOff => _handedOff;

        /// <summary>True while the dialogue hold is currently applied to the assigned runner.</summary>
        public bool DialogueHoldArmed => _holdArmed;

        /// <summary>
        /// Assign or replace the dialogue runner. While the exterior phase is active (hold
        /// armed, not yet handed off) a late assignment is held immediately; after the handoff
        /// a newly assigned runner is NOT held (the story is already released).
        /// </summary>
        public CinematicRadioDialogueController DialogueRunner
        {
            get => dialogueRunner;
            set
            {
                dialogueRunner = value;
                if (value != null && _holdArmed)
                    value.DialogueHeld = true;
            }
        }

        private void OnEnable() => EnterExteriorPhase();

        /// <summary>
        /// Enters (or re-enters) the exterior phase: the assigned dialogue runner is held so no
        /// dialogue or subtitle progression can occur during the exterior flight. Idempotent;
        /// re-entering clears a previous handoff (a fresh exterior phase).
        /// </summary>
        public void EnterExteriorPhase()
        {
            _handedOff = false;
            _holdArmed = true;
            if (dialogueRunner != null)
                dialogueRunner.DialogueHeld = true;
        }

        /// <summary>
        /// THE explicit exterior -> interior/story handoff. Releasing the dialogue hold and
        /// invoking OnHandedOffToStory happen EXACTLY ONCE per exterior phase, no matter how
        /// many times this is called: story progression resumes exactly once and can never be
        /// restarted or double-advanced by repeated handoffs.
        /// </summary>
        public void HandOffToStoryPhase()
        {
            if (_handedOff) return;
            _handedOff = true;
            _holdArmed = false;
            if (dialogueRunner != null)
                dialogueRunner.DialogueHeld = false;
            if (onHandedOffToStory != null)
                onHandedOffToStory.Invoke();
        }
    }
}
