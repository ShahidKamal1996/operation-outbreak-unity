using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.Cinematic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Tests for <see cref="CinematicExteriorFlightPhase"/> — while the exterior establishing
    /// helicopter flight is active, ALL radio dialogue and subtitle progression is held; the
    /// story resumes exactly once after the explicit handoff; the airborne-start flight itself
    /// is unaffected.
    /// </summary>
    public sealed class CinematicExteriorFlightPhaseTests
    {
        private GameObject _directorGo;
        private GameObject _flightGo;
        private GameObject _dialogueGo;
        private GameObject _canvasGo;
        private List<GameObject> _uiRoots = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _directorGo = new GameObject("CinematicDirector");
            _flightGo = new GameObject("HelicopterFlightRoot");
            _dialogueGo = new GameObject("RadioDialogue");
            _uiRoots = new List<GameObject>();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _uiRoots.Count; i++)
                if (_uiRoots[i] != null) Object.DestroyImmediate(_uiRoots[i]);
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_flightGo != null) Object.DestroyImmediate(_flightGo);
            if (_dialogueGo != null) Object.DestroyImmediate(_dialogueGo);
        }

        /// <summary>
        /// Standard dialogue presentation UI: the CinematicDialogueCanvas root (active) with
        /// the dialogue panel, its background, the speaker label and the dialogue text — the
        /// full presentation the exterior phase must hide as a whole.
        /// </summary>
        private (GameObject, GameObject, GameObject, GameObject, GameObject) MakeDialogueUiRoot()
        {
            var canvas = new GameObject("CinematicDialogueCanvas");
            canvas.AddComponent<Canvas>();
            var panel = new GameObject("DialoguePanel");
            panel.transform.SetParent(canvas.transform, false);
            var background = new GameObject("PanelBackground");
            background.transform.SetParent(panel.transform, false);
            var speaker = new GameObject("SpeakerLabel");
            speaker.transform.SetParent(panel.transform, false);
            var text = new GameObject("DialogueText");
            text.transform.SetParent(panel.transform, false);
            _uiRoots.Add(canvas);
            return (canvas, panel, background, speaker, text);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, field + " must exist on " + target.GetType().Name + ".");
            f.SetValue(target, value);
        }

        private static RadioDialogueLine MakeLine(string speaker, string text, float cps, float before, float after)
        {
            return new RadioDialogueLine
            {
                SpeakerName = speaker,
                DialogueText = text,
                CharactersPerSecond = cps,
                DelayBeforeLine = before,
                DelayAfterLine = after,
            };
        }

        private TMP_Text CreateTmpText(string name)
        {
            if (_canvasGo == null)
            {
                _canvasGo = new GameObject("Canvas");
                _canvasGo.AddComponent<Canvas>();
            }
            var go = new GameObject(name);
            go.transform.SetParent(_canvasGo.transform, false);
            return go.AddComponent<TextMeshProUGUI>();
        }

        /// <summary>
        /// Standard exterior fixture: the airborne-start flight (as currently configured in the
        /// scene), the phase controller holding the runner, and the starter that requests the
        /// dialogue start 0.5s into Play (inside the exterior window).
        /// </summary>
        private CinematicExteriorFlightPhase MakePhase(CinematicRadioDialogueController runner,
            out CinematicDialogueSequenceStarter starter)
        {
            var flight = _flightGo.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true; // the exterior flight as currently configured

            var phase = _directorGo.AddComponent<CinematicExteriorFlightPhase>();
            phase.DialogueRunner = runner; // late assignment while the exterior phase is active

            starter = _dialogueGo.AddComponent<CinematicDialogueSequenceStarter>();
            SetPrivate(starter, "dialogueController", runner);
            SetPrivate(starter, "startDelay", 0.5f);
            return phase;
        }

        /// <summary>One simulated frame of the whole exterior system (Edit Mode has no frame loop).</summary>
        private static void StepAll(CinematicHelicopterFlight flight, CinematicDialogueSequenceStarter starter,
            CinematicRadioDialogueController runner)
        {
            flight.AdvanceFlight(1f / 60f);
            starter.AdvanceStarter(1f / 60f);
            runner.AdvanceSequence(1f / 60f);
        }

        [Test]
        public void DialogueRunnerIsHeldDuringExteriorFlight()
        {
            var dialogue = CreateTmpText("Dialogue");
            var speaker = CreateTmpText("Speaker");
            var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
            SetPrivate(runner, "speakerLabel", speaker);
            SetPrivate(runner, "dialogueText", dialogue);
            runner.SetDialogueLines(new[]
            {
                MakeLine("COMMAND", "Copy that, keep moving.", 10f, 0.2f, 0.3f),
                MakeLine("RAVEN", "Kane, status.", 10f, 0f, 0.3f),
                MakeLine("KANE", "Holding position.", 10f, 0f, 0.3f),
            });

            var phase = MakePhase(runner, out var starter);
            var flight = _flightGo.GetComponent<CinematicHelicopterFlight>();
            Assert.IsTrue(runner.DialogueHeld, "The runner must be held as soon as the exterior phase begins.");
            Assert.IsTrue(phase.IsExteriorPhaseActive, "The exterior phase must be active at Play.");

            // 6 full seconds of the exterior establishing flight: the starter's 0.5s delayed
            // start fires well inside this window — the sequence STARTS but must stay frozen.
            for (int i = 0; i < 360; i++)
            {
                StepAll(flight, starter, runner);
                Assert.IsTrue(runner.DialogueHeld, "The hold must persist for the whole exterior flight (frame " + (i + 1) + ").");
                Assert.IsFalse(runner.IsComplete, "The story must not complete during the exterior flight (frame " + (i + 1) + ").");
                Assert.AreEqual(0, runner.CurrentLineIndex, "No dialogue progression during the exterior flight (frame " + (i + 1) + ").");
                Assert.AreEqual("", dialogue.text, "No subtitle text during the exterior flight (frame " + (i + 1) + ").");
                Assert.AreEqual(0, dialogue.maxVisibleCharacters, "No subtitle reveal during the exterior flight (frame " + (i + 1) + ").");
                Assert.IsFalse(runner.IsTyping, "No typewriter activity during the exterior flight (frame " + (i + 1) + ").");
                Assert.AreEqual("", speaker.text, "No speaker label during the exterior flight (frame " + (i + 1) + ").");
            }

            // Sanity: the helicopter really was flying (the exterior flight is active).
            Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase, "The exterior flight must be in cruise the whole time.");
            Assert.Greater(flight.DistanceTravelled, 30f, "The exterior helicopter must actually be flying.");
        }

        [Test]
        public void NoSubtitleOrDialogueProgressionOccursDuringExterior()
        {
            // Progression probes over the exterior window: the starter has already started the
            // sequence (its delay elapsed), so IsPlaying is true — yet NOTHING may progress.
            var dialogue = CreateTmpText("Dialogue");
            var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
            SetPrivate(runner, "dialogueText", dialogue);
            runner.SetDialogueLines(new[]
            {
                MakeLine("COMMAND", "Say again.", 100f, 0f, 0.05f), // 0.01s reveal: would finish almost instantly if not held
                MakeLine("RAVEN", "Loud and clear.", 100f, 0f, 0.05f),
            });

            var phase = MakePhase(runner, out var starter);
            var flight = _flightGo.GetComponent<CinematicHelicopterFlight>();

            for (int i = 0; i < 120; i++) // 2 seconds of exterior flight
            {
                StepAll(flight, starter, runner);
                Assert.AreEqual("", dialogue.text, "No subtitle may appear during the exterior (frame " + (i + 1) + ").");
                Assert.AreEqual(0, dialogue.maxVisibleCharacters, "No subtitle reveal may occur during the exterior (frame " + (i + 1) + ").");
                Assert.AreEqual(0, runner.CurrentLineIndex, "The line index must not advance during the exterior (frame " + (i + 1) + ").");
            }

            Assert.IsTrue(runner.IsPlaying,
                "Sanity: the starter DID start the sequence during the hold (it is frozen, not un-started).");
            Assert.IsFalse(runner.IsComplete, "The sequence must be frozen, not completed, during the exterior.");
        }

        [Test]
        public void StoryResumesExactlyOnceAfterExteriorHandoff()
        {
            var dialogue = CreateTmpText("Dialogue");
            var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
            SetPrivate(runner, "dialogueText", dialogue);
            runner.SetDialogueLines(new[]
            {
                MakeLine("COMMAND", "Copy that, keep moving.", 10f, 0f, 0.1f),
                MakeLine("RAVEN", "Kane, status.", 10f, 0f, 0.1f),
                MakeLine("KANE", "Holding position.", 10f, 0f, 0.1f),
            });
            int completed = 0;
            var completionEvent = (UnityEvent)typeof(CinematicRadioDialogueController)
                .GetField("onSequenceCompleted", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(runner);
            Assert.IsNotNull(completionEvent, "The completion event must be live on a dynamic component.");
            completionEvent.AddListener(() => completed++);

            var phase = MakePhase(runner, out var starter);
            var flight = _flightGo.GetComponent<CinematicHelicopterFlight>();
            int handedOffEvents = 0;
            var handoffEvent = (UnityEvent)typeof(CinematicExteriorFlightPhase)
                .GetField("onHandedOffToStory", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(phase);
            Assert.IsNotNull(handoffEvent, "The handoff event must be live on a dynamic component.");
            handoffEvent.AddListener(() => handedOffEvents++);

            // Exterior flight: 2 seconds, fully held.
            for (int i = 0; i < 120; i++) StepAll(flight, starter, runner);
            Assert.AreEqual(0, runner.CurrentLineIndex, "Held: no progression before the handoff.");
            Assert.AreEqual("", dialogue.text, "Held: no subtitle before the handoff.");

            // THE explicit exterior -> story handoff.
            phase.HandOffToStoryPhase();

            Assert.IsFalse(runner.DialogueHeld, "The handoff must release the dialogue hold.");
            Assert.IsTrue(phase.HasHandedOff, "The phase must report the handoff.");
            Assert.IsFalse(phase.IsExteriorPhaseActive, "The exterior phase must no longer be active after the handoff.");

            // The story resumes: the frozen (already started) sequence now progresses.
            StepAll(flight, starter, runner);
            Assert.AreEqual("Copy that, keep moving.", dialogue.text,
                "Line 0 must begin presenting right after the handoff.");

            // Run to natural completion.
            for (int i = 0; i < 600; i++) StepAll(flight, starter, runner);
            Assert.IsTrue(runner.IsComplete, "The story must complete after resuming.");
            Assert.AreEqual(1, completed, "Exactly one completion event after the handoff.");
            Assert.AreEqual(1, handedOffEvents, "The handoff event must fire exactly once.");

            // Repeated handoffs are no-ops: no second release, no restart, no second event.
            phase.HandOffToStoryPhase();
            phase.HandOffToStoryPhase();
            phase.HandOffToStoryPhase();
            Assert.AreEqual(1, handedOffEvents,
                "The handoff event must stay at 1 no matter how often HandOffToStoryPhase is called.");
            Assert.IsTrue(runner.IsComplete, "Repeated handoffs must not restart the completed story.");
            Assert.AreEqual(1, completed, "Repeated handoffs must not double the completion event.");
        }

        [Test]
        public void HandOffBeforeDialogueStartStillLetsTheStoryRunOnce()
        {
            // The handoff may happen before the starter's delayed start fires: the release is
            // not lost — the story still starts (unheld) and runs to completion exactly once.
            int completed = 0;
            var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
            runner.SetDialogueLines(new[]
            {
                MakeLine("COMMAND", "A", 100f, 0f, 0.05f),
                MakeLine("KANE", "B", 100f, 0f, 0.05f),
            });
            var completionEvent = (UnityEvent)typeof(CinematicRadioDialogueController)
                .GetField("onSequenceCompleted", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(runner);
            completionEvent.AddListener(() => completed++);

            var phase = MakePhase(runner, out var starter);
            var flight = _flightGo.GetComponent<CinematicHelicopterFlight>();
            Assert.IsTrue(runner.DialogueHeld, "Sanity: held at Play.");

            phase.HandOffToStoryPhase(); // immediately, before the starter's 0.5s delay fires
            Assert.IsFalse(runner.DialogueHeld, "The hold must be released by the early handoff.");

            for (int i = 0; i < 300; i++) StepAll(flight, starter, runner);
            Assert.IsTrue(runner.IsComplete, "The story must start after the starter fires and run to completion.");
            Assert.AreEqual(1, completed, "Exactly one completion: an early handoff must not swallow or duplicate the story.");
        }

        [Test]
        public void EnteringExteriorPhaseRearmsTheHoldAfterHandoff()
        {
            var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
            runner.SetDialogueLines(new[] { MakeLine("COMMAND", "A", 100f, 0f, 0.05f) });
            int handedOffEvents = 0;

            var phase = _directorGo.AddComponent<CinematicExteriorFlightPhase>();
            phase.DialogueRunner = runner;
            var handoffEvent = (UnityEvent)typeof(CinematicExteriorFlightPhase)
                .GetField("onHandedOffToStory", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(phase);
            handoffEvent.AddListener(() => handedOffEvents++);

            Assert.IsTrue(runner.DialogueHeld, "The hold must be armed while the exterior phase is active.");

            phase.HandOffToStoryPhase();
            Assert.IsFalse(runner.DialogueHeld, "The first handoff releases the hold.");

            // Re-entry: a new exterior phase holds again and hands off exactly once more.
            phase.EnterExteriorPhase();
            Assert.IsTrue(runner.DialogueHeld, "Re-entering the exterior phase must re-arm the hold.");
            Assert.IsFalse(phase.HasHandedOff, "A re-entered exterior phase is a fresh phase (not yet handed off).");
            Assert.IsTrue(phase.IsExteriorPhaseActive);

            phase.HandOffToStoryPhase();
            Assert.IsFalse(runner.DialogueHeld, "The second handoff releases the hold again.");
            Assert.AreEqual(2, handedOffEvents, "Each exterior phase must hand off exactly once.");
        }

        [Test]
        public void LateAssignedRunnerIsHeldWhileExteriorActive()
        {
            // The component is enabled (OnEnable ran) with NO runner assigned; assigning one
            // later, while the exterior phase is still active, must arm the hold immediately.
            var phase = _directorGo.AddComponent<CinematicExteriorFlightPhase>();
            var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
            runner.SetDialogueLines(new[] { MakeLine("COMMAND", "A", 100f, 0f, 0.05f) });

            Assert.IsFalse(runner.DialogueHeld, "Sanity: not held before assignment.");
            phase.DialogueRunner = runner;
            Assert.IsTrue(runner.DialogueHeld,
                "A late-assigned runner must be held immediately while the exterior phase is active.");

            phase.HandOffToStoryPhase();
            Assert.IsFalse(runner.DialogueHeld, "The handoff releases the late-assigned runner too.");

            // A runner assigned AFTER the handoff must NOT re-hold an already-released story.
            var go2 = new GameObject("RadioDialogue2");
            try
            {
                var runner2 = go2.AddComponent<CinematicRadioDialogueController>();
                phase.DialogueRunner = runner2;
                Assert.IsFalse(runner2.DialogueHeld,
                    "Assigning a runner after the handoff must not re-hold the released story.");
            }
            finally { Object.DestroyImmediate(go2); }
        }

        [Test]
        public void ExteriorPhaseDoesNotChangeAirborneStartFlightBehavior()
        {
            // Reference: a bare airborne-start flight (the current working behavior).
            var refGo = new GameObject("RefFlightRoot");
            try
            {
                var refFlight = refGo.AddComponent<CinematicHelicopterFlight>();
                refFlight.StartAirborne = true;

                // Test: the same flight with the exterior phase + held runner alongside.
                var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
                runner.SetDialogueLines(new[] { MakeLine("COMMAND", "A", 100f, 0f, 0.05f) });
                var phase = MakePhase(runner, out _);
                var flight = _flightGo.GetComponent<CinematicHelicopterFlight>();
                Assert.IsTrue(runner.DialogueHeld, "Sanity: the hold is active during this flight.");

                // 3 seconds: the flight path/rotation must be IDENTICAL to the reference.
                for (int i = 0; i < 180; i++)
                {
                    refFlight.AdvanceFlight(1f / 60f);
                    flight.AdvanceFlight(1f / 60f);
                    Assert.AreEqual(refGo.transform.position, _flightGo.transform.position,
                        "The airborne-start flight path must be identical with the exterior phase present (frame " + (i + 1) + ").");
                    Assert.AreEqual(0f, Quaternion.Angle(refGo.transform.rotation, _flightGo.transform.rotation), 1e-4f,
                        "The airborne-start rotation must be identical with the exterior phase present (frame " + (i + 1) + ").");
                }

                Assert.AreEqual(FlightPhase.Cruise, flight.CurrentPhase,
                    "Airborne-start must still begin directly in Cruise (no regression).");
                Assert.AreEqual(8f, flight.CurrentSpeed, 1e-4f,
                    "Airborne-start must still have full cruise speed from frame one (no regression).");
            }
            finally { Object.DestroyImmediate(refGo); }
        }

        // ---- dialogue presentation UI hide/restore ----
        // The CinematicDialogueCanvas (speaker label, dialogue text, panel/background) must not
        // be visible during the exterior shot: the phase hides the presentation root when the
        // exterior begins and restores it exactly once on the handoff — without ever touching
        // the CinematicRadioDialogueController itself.

        [Test]
        public void DialogueUiIsHiddenDuringExteriorFlight()
        {
            var (ui, panel, background, speaker, text) = MakeDialogueUiRoot();
            var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
            runner.SetDialogueLines(new[] { MakeLine("COMMAND", "A", 100f, 0f, 0.05f) });

            var phase = _directorGo.AddComponent<CinematicExteriorFlightPhase>();
            SetPrivate(phase, "dialoguePresentationRoot", ui); // Inspector-style assignment
            phase.EnterExteriorPhase(); // the exterior phase begins (OnEnable does this at Play start)

            Assert.IsFalse(ui.activeSelf, "The dialogue presentation root must be hidden when the exterior phase begins.");
            Assert.IsFalse(panel.activeInHierarchy, "The dialogue panel must be hidden (child of the hidden root).");
            Assert.IsFalse(background.activeInHierarchy, "The panel background must be hidden.");
            Assert.IsFalse(speaker.activeInHierarchy, "The speaker label must be hidden.");
            Assert.IsFalse(text.activeInHierarchy, "The dialogue text must be hidden.");
            Assert.IsTrue(phase.DialogueUiHiddenByPhase, "The phase must report that it owns the hidden state.");

            // The controller itself must NOT be destroyed or disabled:
            Assert.IsTrue(_dialogueGo.activeSelf, "The dialogue controller's GameObject must stay active.");
            Assert.IsTrue(runner.enabled, "The dialogue controller component must stay enabled.");

            // Through the exterior flight the UI stays hidden and the controller stays alive:
            var flight = _flightGo.AddComponent<CinematicHelicopterFlight>();
            flight.StartAirborne = true;
            for (int i = 0; i < 60; i++)
            {
                flight.AdvanceFlight(1f / 60f);
                runner.AdvanceSequence(1f / 60f);
                Assert.IsFalse(ui.activeSelf, "The dialogue UI must stay hidden during the exterior flight (frame " + (i + 1) + ").");
                Assert.IsTrue(runner.enabled, "The dialogue controller must stay enabled during the exterior flight (frame " + (i + 1) + ").");
            }

            // A root assigned late while the exterior phase is active is hidden immediately too:
            var ui2 = new GameObject("SecondDialogueUi");
            try
            {
                phase.DialoguePresentationRoot = ui2;
                Assert.IsFalse(ui2.activeSelf,
                    "A late-assigned presentation root must be hidden immediately while the exterior phase is active.");
            }
            finally { Object.DestroyImmediate(ui2); }
        }

        [Test]
        public void DialogueTextDoesNotProgressWhileUiHidden()
        {
            var (ui, _, _, _, _) = MakeDialogueUiRoot();
            var dialogue = CreateTmpText("Dialogue");
            var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
            SetPrivate(runner, "dialogueText", dialogue);
            runner.SetDialogueLines(new[]
            {
                MakeLine("COMMAND", "Say again.", 100f, 0f, 0.05f), // would finish almost instantly if not held
                MakeLine("RAVEN", "Loud and clear.", 100f, 0f, 0.05f),
            });

            var phase = MakePhase(runner, out var starter);
            SetPrivate(phase, "dialoguePresentationRoot", ui);
            phase.EnterExteriorPhase(); // fresh exterior entry with the UI root
            Assert.IsFalse(ui.activeSelf, "Sanity: the UI is hidden for the exterior.");

            var flight = _flightGo.GetComponent<CinematicHelicopterFlight>();
            for (int i = 0; i < 180; i++) // 3 seconds of exterior flight
            {
                StepAll(flight, starter, runner);
                Assert.AreEqual("", dialogue.text, "No dialogue text may progress while held (frame " + (i + 1) + ").");
                Assert.AreEqual(0, dialogue.maxVisibleCharacters, "No subtitle reveal may progress while held (frame " + (i + 1) + ").");
                Assert.AreEqual(0, runner.CurrentLineIndex, "No line progression while held (frame " + (i + 1) + ").");
                Assert.IsFalse(ui.activeSelf, "The dialogue UI must remain hidden while held (frame " + (i + 1) + ").");
            }

            Assert.IsTrue(runner.IsPlaying,
                "Sanity: the sequence was started by the starter and is frozen (held), not skipped.");
        }

        [Test]
        public void DialogueUiIsRestoredOnHandoff()
        {
            var (ui, panel, _, _, _) = MakeDialogueUiRoot();
            var dialogue = CreateTmpText("Dialogue");
            var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
            SetPrivate(runner, "dialogueText", dialogue);
            runner.SetDialogueLines(new[]
            {
                MakeLine("COMMAND", "Copy that.", 10f, 0f, 0.1f),
                MakeLine("KANE", "Moving.", 10f, 0f, 0.1f),
            });

            var phase = MakePhase(runner, out var starter);
            SetPrivate(phase, "dialoguePresentationRoot", ui);
            phase.EnterExteriorPhase();
            Assert.IsFalse(ui.activeSelf, "Sanity: the UI is hidden during the exterior.");

            var flight = _flightGo.GetComponent<CinematicHelicopterFlight>();
            for (int i = 0; i < 60; i++) StepAll(flight, starter, runner);
            Assert.AreEqual("", dialogue.text, "Sanity: nothing progressed before the handoff.");

            phase.HandOffToStoryPhase();

            Assert.IsTrue(ui.activeSelf, "The dialogue presentation root must be restored on the handoff.");
            Assert.IsTrue(panel.activeInHierarchy, "The dialogue panel must be visible again on the handoff.");
            Assert.IsFalse(phase.DialogueUiHiddenByPhase, "The phase must no longer own the hidden state after the restore.");
            Assert.IsFalse(runner.DialogueHeld, "The dialogue hold must be released on the handoff.");

            StepAll(flight, starter, runner);
            Assert.AreEqual("Copy that.", dialogue.text,
                "Dialogue must progress (with the UI visible) right after the handoff.");
        }

        [Test]
        public void DialogueUiRestoreHappensExactlyOnce()
        {
            // The canvas is authored INACTIVE (a realistic pre-story state): the phase must
            // remember that, and the single restore must return it to INACTIVE. The scene then
            // enables it for the story — and no later repeated handoff may ever re-apply the
            // recorded state (which would be the only observable effect of a second restore).
            var (ui, _, _, _, _) = MakeDialogueUiRoot();
            ui.SetActive(false);
            var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
            runner.SetDialogueLines(new[] { MakeLine("COMMAND", "A", 100f, 0f, 0.05f) });

            var phase = MakePhase(runner, out _);
            SetPrivate(phase, "dialoguePresentationRoot", ui);
            phase.EnterExteriorPhase();
            Assert.IsFalse(ui.activeSelf, "Sanity: hidden (already inactive) during the exterior.");
            Assert.IsTrue(phase.DialogueUiHiddenByPhase, "The phase must own the hidden state.");

            phase.HandOffToStoryPhase();
            Assert.IsFalse(ui.activeSelf,
                "The single restore must return the canvas to its pre-exterior state (inactive).");
            Assert.IsFalse(phase.DialogueUiHiddenByPhase,
                "After the restore the phase must no longer own the hidden state.");

            // The scene (story phase) enables the canvas for the story:
            ui.SetActive(true);
            Assert.IsTrue(ui.activeSelf, "Sanity: the scene enabled the canvas.");

            phase.HandOffToStoryPhase();
            phase.HandOffToStoryPhase();
            phase.HandOffToStoryPhase();
            Assert.IsTrue(ui.activeSelf,
                "The restore must have happened EXACTLY ONCE: repeated handoffs must not re-apply the recorded inactive state.");
        }

        [Test]
        public void ReEnteringExteriorFlightHidesDialogueUiAgain()
        {
            var (ui, _, _, _, _) = MakeDialogueUiRoot();
            var runner = _dialogueGo.AddComponent<CinematicRadioDialogueController>();
            runner.SetDialogueLines(new[] { MakeLine("COMMAND", "A", 100f, 0f, 0.05f) });
            int handedOffEvents = 0;

            var phase = _directorGo.AddComponent<CinematicExteriorFlightPhase>();
            phase.DialogueRunner = runner;
            SetPrivate(phase, "dialoguePresentationRoot", ui);
            var handoffEvent = (UnityEvent)typeof(CinematicExteriorFlightPhase)
                .GetField("onHandedOffToStory", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(phase);
            handoffEvent.AddListener(() => handedOffEvents++);

            // Exterior phase #1 (OnEnable already entered with no root; this entry applies it):
            phase.EnterExteriorPhase();
            Assert.IsFalse(ui.activeSelf, "Entering the exterior phase must hide the dialogue UI.");
            Assert.IsTrue(runner.DialogueHeld, "Entering the exterior phase must hold the runner.");

            // Handoff #1: restore the UI, release the hold.
            phase.HandOffToStoryPhase();
            Assert.IsTrue(ui.activeSelf, "Handoff #1 must restore the dialogue UI.");
            Assert.IsFalse(runner.DialogueHeld, "Handoff #1 must release the hold.");

            // Exterior phase #2 (re-entry): hide the UI again and hold again.
            phase.EnterExteriorPhase();
            Assert.IsFalse(ui.activeSelf, "Re-entering the exterior phase must hide the dialogue UI again.");
            Assert.IsTrue(runner.DialogueHeld, "Re-entering the exterior phase must re-arm the hold.");

            // Handoff #2: restore again — exactly once per phase.
            phase.HandOffToStoryPhase();
            Assert.IsTrue(ui.activeSelf, "Handoff #2 must restore the dialogue UI again.");
            Assert.IsFalse(runner.DialogueHeld, "Handoff #2 must release the hold again.");
            Assert.AreEqual(2, handedOffEvents, "Each exterior phase must hand off exactly once.");
        }
    }
}
