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

        [SetUp]
        public void SetUp()
        {
            _directorGo = new GameObject("CinematicDirector");
            _flightGo = new GameObject("HelicopterFlightRoot");
            _dialogueGo = new GameObject("RadioDialogue");
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_flightGo != null) Object.DestroyImmediate(_flightGo);
            if (_dialogueGo != null) Object.DestroyImmediate(_dialogueGo);
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
    }
}
