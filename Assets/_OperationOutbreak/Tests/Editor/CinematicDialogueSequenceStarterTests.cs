using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.Cinematic;
using UnityEngine;
using UnityEngine.Events;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Tests for <see cref="CinematicDialogueSequenceStarter"/> — deterministic behavior driven
    /// by fixed 60fps <c>AdvanceStarter</c> steps (no real-time waits). Frame-exact assertions
    /// are verified against a float32-faithful transcription of the starter + controller
    /// state machines.
    /// </summary>
    public sealed class CinematicDialogueSequenceStarterTests
    {
        private GameObject _go;
        private GameObject _ctrlGo;

        [SetUp]
        public void SetUp() => _go = new GameObject("StarterHost");

        [TearDown]
        public void TearDown()
        {
            if (_ctrlGo != null) Object.DestroyImmediate(_ctrlGo);
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, field + " must exist on " + target.GetType().Name + ".");
            f.SetValue(target, value);
        }

        private static object GetPrivate(object target, string field)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, field + " must exist on " + target.GetType().Name + ".");
            return f.GetValue(target);
        }

        private static UnityEvent GetCompletionEvent(CinematicRadioDialogueController c)
        {
            var f = c.GetType().GetField("onSequenceCompleted", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "onSequenceCompleted must exist.");
            var evt = (UnityEvent)f.GetValue(c);
            Assert.IsNotNull(evt, "onSequenceCompleted must be non-null on a dynamically created component.");
            return evt;
        }

        private static RadioDialogueLine MakeLine(string speaker, string text, AudioClip voice,
            AudioClip sfx, float cps, float before, float after)
        {
            return new RadioDialogueLine
            {
                SpeakerName = speaker,
                DialogueText = text,
                VoiceClip = voice,
                OpeningSfx = sfx,
                CharactersPerSecond = cps,
                DelayBeforeLine = before,
                DelayAfterLine = after,
            };
        }

        private static CinematicRadioDialogueController CreateController(out GameObject host, params RadioDialogueLine[] lines)
        {
            host = new GameObject("DialogueController");
            var c = host.AddComponent<CinematicRadioDialogueController>();
            c.SetDialogueLines(lines);
            return c;
        }

        /// <summary>Steps the starter and the controller together, frame by frame (starter first).</summary>
        private static void Step(CinematicDialogueSequenceStarter starter, CinematicRadioDialogueController controller,
            float seconds, float dt = 1f / 60f)
        {
            int frames = (int)Mathf.Round(seconds / dt);
            for (int i = 0; i < frames; i++)
            {
                starter.AdvanceStarter(dt);
                controller.AdvanceSequence(dt);
            }
        }

        // ---- 1. missing controller ----

        [Test]
        public void MissingControllerIsSafe()
        {
            var starter = _go.AddComponent<CinematicDialogueSequenceStarter>();
            // dialogueController intentionally left null (the default).
            Assert.DoesNotThrow(() => starter.Play(), "Play() with no assigned controller must not throw.");
            Assert.DoesNotThrow(() => starter.AdvanceStarter(1f), "Advancing with no assigned controller must not throw.");
            Assert.IsFalse(starter.IsStartIssued, "A null controller must not consume the start.");
            Assert.IsFalse(starter.IsStartPending, "Nothing may be pending without a controller.");

            // A later (Inspector-style) assignment still allows a start.
            var controller = CreateController(out var host, MakeLine("S", "A", null, null, 100f, 0f, 0.1f));
            _ctrlGo = host;
            SetPrivate(starter, "dialogueController", controller);
            starter.Play();
            Step(starter, controller, 1f);
            Assert.IsTrue(controller.IsComplete, "After a later assignment, Play() must start the sequence normally.");
        }

        // ---- 2. defaults ----

        [Test]
        public void PlayOnStartDefaultsAreSane()
        {
            var starter = _go.AddComponent<CinematicDialogueSequenceStarter>();
            Assert.IsTrue((bool)GetPrivate(starter, "playOnStart"), "playOnStart must default to true.");
            Assert.AreEqual(0f, (float)GetPrivate(starter, "startDelay"), "startDelay must default to 0.");
            Assert.IsFalse((bool)GetPrivate(starter, "useUnscaledTime"), "Scaled time must be the default (useUnscaledTime = false).");
            Assert.IsFalse(starter.UseUnscaledTime, "The public time-mode view must agree with the serialized default.");
            Assert.IsNull(GetPrivate(starter, "dialogueController"), "No controller must be assigned by default.");
            Assert.IsFalse(starter.IsStartPending, "Nothing may be pending before the first frame.");
            Assert.IsFalse(starter.IsStartIssued, "Nothing may be issued before the first frame.");
        }

        // ---- 3. negative delay ----

        [Test]
        public void NegativeDelayStartsImmediatelyAndIsSafe()
        {
            var controller = CreateController(out var host, MakeLine("S", "AB", null, null, 10f, 0f, 0.2f));
            _ctrlGo = host;
            var starter = _go.AddComponent<CinematicDialogueSequenceStarter>();
            SetPrivate(starter, "dialogueController", controller);
            SetPrivate(starter, "startDelay", -5f); // negative: must clamp to immediate, not wait or hang

            Assert.DoesNotThrow(() =>
            {
                starter.AdvanceStarter(1f / 60f);
                controller.AdvanceSequence(1f / 60f);
            }, "A negative start delay must be handled safely.");
            Assert.IsTrue(controller.IsPlaying, "A negative delay must be clamped to an immediate start on the first frame.");
            Assert.IsFalse(starter.IsStartPending, "No pending countdown may remain after an immediate start.");
            Assert.IsTrue(starter.IsStartIssued, "The immediate start must be recorded as issued.");

            Step(starter, controller, 2f);
            Assert.IsTrue(controller.IsComplete, "The sequence must still complete normally after the immediate start.");
        }

        // ---- 4. zero delay, manual ----

        [Test]
        public void ZeroDelayPlayStartsSynchronously()
        {
            var controller = CreateController(out var host, MakeLine("S", "A", null, null, 100f, 0f, 0.3f));
            _ctrlGo = host;
            var starter = _go.AddComponent<CinematicDialogueSequenceStarter>();
            SetPrivate(starter, "dialogueController", controller);
            SetPrivate(starter, "playOnStart", false);
            SetPrivate(starter, "startDelay", 0f);

            starter.Play();
            Assert.IsTrue(controller.IsPlaying, "A zero delay must start the sequence in the current call.");
            Assert.IsFalse(starter.IsStartPending, "No countdown may remain for a zero delay.");
            Assert.IsTrue(starter.IsStartIssued, "The start must be recorded as issued.");
        }

        // ---- 5. delayed auto start waits for the configured delay ----

        [Test]
        public void DelayedAutoStartWaitsForConfiguredDelay()
        {
            var controller = CreateController(out var host, MakeLine("S", "", null, null, 40f, 0f, 5f)); // long line: stays playing
            _ctrlGo = host;
            var starter = _go.AddComponent<CinematicDialogueSequenceStarter>();
            SetPrivate(starter, "dialogueController", controller);
            SetPrivate(starter, "startDelay", 0.15f); // playOnStart defaults to true

            Assert.IsFalse(controller.IsPlaying, "Nothing may start before the first frame.");

            for (int f = 1; f <= 9; f++)
            {
                starter.AdvanceStarter(1f / 60f);
                controller.AdvanceSequence(1f / 60f);
            }
            Assert.IsTrue(starter.IsStartPending, "During the delay a pending start must be visible.");
            Assert.IsFalse(controller.IsPlaying, "The sequence must not start before the 0.15s delay elapses.");

            starter.AdvanceStarter(1f / 60f); // frame 10 (model-verified float32 fire frame for 0.15s @60fps)
            controller.AdvanceSequence(1f / 60f);
            Assert.IsTrue(controller.IsPlaying, "The sequence must start once the delay has elapsed (frame 10, model-verified).");
            Assert.IsFalse(starter.IsStartPending, "The pending countdown must be consumed at the fire frame.");
        }

        // ---- 6. manual Play() never creates duplicate starts ----

        [Test]
        public void ManualPlayNeverCreatesDuplicateStarts()
        {
            var controller = CreateController(out var host, MakeLine("S", "", null, null, 40f, 0f, 1f)); // 1.0s line
            _ctrlGo = host;
            var starter = _go.AddComponent<CinematicDialogueSequenceStarter>();
            SetPrivate(starter, "dialogueController", controller);
            SetPrivate(starter, "playOnStart", false);
            SetPrivate(starter, "startDelay", 0.2f);
            int completed = 0;
            GetCompletionEvent(controller).AddListener(() => completed++);

            int firstPlayingFrame = -1;
            int completeFrame = -1;
            for (int f = 1; f <= 90; f++)
            {
                if (f == 1 || f == 3 || f == 10) starter.Play(); // repeated while the delayed start is pending
                if (f == 15) starter.Play();                     // repeated while the sequence is active
                starter.AdvanceStarter(1f / 60f);
                controller.AdvanceSequence(1f / 60f);
                if (controller.IsPlaying && firstPlayingFrame < 0) firstPlayingFrame = f;
                if (controller.IsComplete && completeFrame < 0) completeFrame = f;
            }

            Assert.AreEqual(13, firstPlayingFrame,
                "Exactly one start, at the configured 0.2s delay (model-verified frame 13 @60fps).");
            Assert.AreEqual(73, completeFrame,
                "Exactly one completion at the single-start timeline (model-verified frame 73 @60fps); a duplicated start would shift it.");
            Assert.AreEqual(1, completed,
                "A duplicated start would interrupt/restart the run; the completion event must fire exactly once.");
            Assert.IsTrue(controller.IsComplete && !controller.IsPlaying, "The sequence must end naturally.");
        }
    }
}
