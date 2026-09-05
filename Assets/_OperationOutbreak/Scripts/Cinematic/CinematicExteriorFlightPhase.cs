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
    /// - HandOffToStoryPhase() is the EXPLICIT exterior -> story handoff. It first restores the
    ///   dialogue presentation UI (if one is assigned), then releases the hold and invokes
    ///   OnHandedOffToStory — all exactly once, no matter how many times it is called: the story
    ///   resumes exactly once and can never be restarted or double-advanced by repeated
    ///   handoffs.
    /// - EnterExteriorPhase() (re)enters the exterior phase (used by OnEnable and available for
    ///   re-entry): it re-arms the hold, hides the dialogue presentation UI again, and clears
    ///   the handed-off flag.
    ///
    /// DIALOGUE PRESENTATION UI
    /// ------------------------
    /// The optional Inspector-assigned Dialogue Presentation Root (the CinematicDialogueCanvas
    /// GameObject — speaker label, dialogue text, panel/background) is HIDDEN (SetActive(false))
    /// when the exterior phase begins, so the transparent dialogue panel is never visible
    /// during the exterior shot. Its previous active state is remembered and the UI is restored
    /// EXACTLY ONCE on the handoff, immediately before the dialogue hold is released. Re-entering
    /// the exterior phase hides it again. The CinematicRadioDialogueController itself is NEVER
    /// destroyed or disabled — only the presentation root's active state changes.
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

        [Tooltip("Optional: the dialogue presentation UI to hide while the exterior flight is active (e.g. the " +
                 "CinematicDialogueCanvas root: speaker label, dialogue text, panel/background). Hidden with " +
                 "SetActive(false) when the exterior phase begins and restored to its previous active state " +
                 "exactly once on the handoff. The CinematicRadioDialogueController is never touched.")]
        [SerializeField] private GameObject dialoguePresentationRoot;

        [Tooltip("Invoked exactly once when the exterior phase hands off to the story. Wire the " +
                 "interior/story-phase transition (cameras, fades, rig activation) to this event.")]
        [SerializeField] private UnityEvent onHandedOffToStory = new UnityEvent();

        // ---- runtime state (never serialized) ----
        private bool _holdArmed;
        private bool _handedOff;
        private bool _dialogueUiHiddenByPhase;
        private bool _dialogueUiWasActive;
        private GameObject _hiddenRoot;

        /// <summary>True while the exterior establishing flight is the active phase (until the handoff).</summary>
        public bool IsExteriorPhaseActive => !_handedOff;

        /// <summary>True once the explicit handoff to the interior/story phase has happened.</summary>
        public bool HasHandedOff => _handedOff;

        /// <summary>True while the dialogue hold is currently applied to the assigned runner.</summary>
        public bool DialogueHoldArmed => _holdArmed;

        /// <summary>True while the phase currently has the dialogue presentation UI hidden.</summary>
        public bool DialogueUiHiddenByPhase => _dialogueUiHiddenByPhase;

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

        /// <summary>
        /// Assign or replace the dialogue presentation root. While the exterior phase is active
        /// (hold armed, not yet handed off) a late assignment is hidden immediately; after the
        /// handoff a newly assigned root is NOT hidden (the story is already released).
        /// </summary>
        public GameObject DialoguePresentationRoot
        {
            get => dialoguePresentationRoot;
            set
            {
                dialoguePresentationRoot = value;
                if (_holdArmed)
                    HideDialogueUi();
            }
        }

        private void OnEnable() => EnterExteriorPhase();

        /// <summary>
        /// Enters (or re-enters) the exterior phase: the assigned dialogue runner is held so no
        /// dialogue or subtitle progression can occur during the exterior flight, and the
        /// assigned dialogue presentation UI is hidden (the panel must not be visible during
        /// the exterior shot). Idempotent; re-entering clears a previous handoff (a fresh
        /// exterior phase).
        /// </summary>
        public void EnterExteriorPhase()
        {
            _handedOff = false;
            _holdArmed = true;
            if (dialogueRunner != null)
                dialogueRunner.DialogueHeld = true;
            HideDialogueUi();
        }

        /// <summary>
        /// THE explicit exterior -> interior/story handoff. Restoring the dialogue presentation
        /// UI, releasing the dialogue hold, and invoking OnHandedOffToStory all happen EXACTLY
        /// ONCE per exterior phase, no matter how many times this is called: the story resumes
        /// exactly once and can never be restarted or double-advanced by repeated handoffs.
        /// The UI is restored immediately BEFORE the hold is released, so the panel is visible
        /// for the very first frame of resumed dialogue.
        /// </summary>
        public void HandOffToStoryPhase()
        {
            if (_handedOff) return;
            _handedOff = true;
            _holdArmed = false;
            RestoreDialogueUi(); // 1. UI visible again first
            if (dialogueRunner != null)
                dialogueRunner.DialogueHeld = false; // 2. then the dialogue hold is released
            if (onHandedOffToStory != null)
                onHandedOffToStory.Invoke(); // 3. then the handoff event
        }

        // ---- dialogue presentation UI hide/restore ----

        /// <summary>
        /// Hides the assigned dialogue presentation root (remembering its previous active
        /// state). Null-safe and idempotent per root: the same already-hidden root is left
        /// alone, while a different root assigned mid-exterior is hidden too.
        /// </summary>
        private void HideDialogueUi()
        {
            if (dialoguePresentationRoot == null) return;
            if (_dialogueUiHiddenByPhase && _hiddenRoot == dialoguePresentationRoot) return;
            _dialogueUiWasActive = dialoguePresentationRoot.activeSelf;
            _hiddenRoot = dialoguePresentationRoot;
            _dialogueUiHiddenByPhase = true;
            dialoguePresentationRoot.SetActive(false);
        }

        /// <summary>
        /// Restores the currently assigned dialogue presentation root to the active state it
        /// had before the phase hid it. No-op unless the phase currently owns the hidden state
        /// (so a root assigned after the handoff, or one that was never hidden, is never
        /// touched).
        /// </summary>
        private void RestoreDialogueUi()
        {
            if (!_dialogueUiHiddenByPhase) return;
            _dialogueUiHiddenByPhase = false;
            var root = dialoguePresentationRoot;
            _hiddenRoot = null;
            if (root != null)
                root.SetActive(_dialogueUiWasActive);
        }
    }
}
