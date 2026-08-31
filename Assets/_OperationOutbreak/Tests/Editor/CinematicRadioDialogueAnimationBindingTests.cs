using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.Cinematic;
using UnityEngine;
using UnityEngine.Events;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Tests for the optional speaker -> Animator talking bindings on
    /// <see cref="CinematicRadioDialogueController"/> — driven by fixed 60fps steps.
    /// </summary>
    public sealed class CinematicRadioDialogueAnimationBindingTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp() => _go = new GameObject("DialogueWithBindings");

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go); // destroys the child animator GameObjects too
        }

        private static UnityEvent GetCompletionEvent(CinematicRadioDialogueController c)
        {
            var f = c.GetType().GetField("onSequenceCompleted", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "onSequenceCompleted must exist.");
            return (UnityEvent)f.GetValue(c);
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

        /// <summary>Child GameObject with an Animator whose controller exposes the given bool parameter.</summary>
        private Animator CreateAnimatorWithBool(string paramName)
        {
            var go = new GameObject(paramName + "AnimatorGo");
            go.transform.SetParent(_go.transform, false);
            var animator = go.AddComponent<Animator>();
            var controller = new AnimatorController();
            controller.name = paramName + "_TestCtrl";
            controller.AddParameter(new AnimatorControllerParameter { name = paramName, type = AnimatorControllerParameterType.Bool });
            animator.runtimeAnimatorController = controller;
            return animator;
        }

        private static SpeakerAnimationBinding MakeBinding(string speaker, Animator animator, string parameter = "IsTalking")
        {
            return new SpeakerAnimationBinding { SpeakerName = speaker, animator = animator, TalkingParameter = parameter };
        }

        private static void Step(CinematicRadioDialogueController c, float seconds, float dt = 1f / 60f)
        {
            int frames = (int)Mathf.Round(seconds / dt);
            for (int i = 0; i < frames; i++) c.AdvanceSequence(dt);
        }

        // ---- 1. matching line sets the talking bool true ----

        [Test]
        public void MatchingKaneLineSetsIsTalkingTrue()
        {
            var kane = CreateAnimatorWithBool("IsTalking");
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "Hello", 10f, 0f, 0f) }); // 0.5s reveal

            Assert.IsFalse(kane.GetBool("IsTalking"), "Kane must idle by default (before the sequence).");

            c.PlaySequence();
            Step(c, 0.1f); // mid line 0 (inside the 0.5s presentation)
            Assert.IsTrue(kane.GetBool("IsTalking"), "A matching Kane line must set IsTalking true at line start.");
        }

        // ---- 2. non-matching lines never activate the bound speaker ----

        [Test]
        public void NonKaneLinesDoNotActivateKane()
        {
            var kane = CreateAnimatorWithBool("IsTalking");
            var reyes = CreateAnimatorWithBool("IsTalking");
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane), MakeBinding("Reyes", reyes) });
            c.SetDialogueLines(new[] { MakeLine("Reyes", "Silent", 10f, 0f, 0.1f) }); // 0.6s reveal

            c.PlaySequence();
            Step(c, 0.2f); // mid Reyes line
            Assert.IsTrue(reyes.GetBool("IsTalking"), "The matching Reyes line must activate the Reyes binding.");
            Assert.IsFalse(kane.GetBool("IsTalking"), "A Reyes line must NOT activate Kane's talking gesture.");
        }

        // ---- 3. finishing the line resets the bool false ----

        [Test]
        public void FinishingKaneLineResetsIsTalkingFalse()
        {
            var kane = CreateAnimatorWithBool("IsTalking");
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "Hello", 10f, 0f, 0f) }); // 0.5s reveal, no after-delay

            c.PlaySequence();
            Step(c, 0.1f);
            Assert.IsTrue(kane.GetBool("IsTalking"), "Sanity: mid-line the gesture must be active.");

            Step(c, 0.5f); // t = 0.6s >= 0.5s -> line finished
            Assert.IsTrue(c.IsComplete, "The single-line sequence must be complete.");
            Assert.IsFalse(kane.GetBool("IsTalking"), "IsTalking must be false as soon as the Kane line finishes.");
        }

        // ---- 4. StopSequence resets false ----

        [Test]
        public void StopSequenceResetsIsTalkingFalse()
        {
            var kane = CreateAnimatorWithBool("IsTalking");
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "AAAAAAAAAA", 10f, 0f, 0f) }); // 1.0s presentation

            c.PlaySequence();
            Step(c, 0.5f); // mid presentation
            Assert.IsTrue(kane.GetBool("IsTalking"), "Sanity: mid-presentation the gesture must be active.");

            c.StopSequence();
            Assert.IsFalse(kane.GetBool("IsTalking"), "StopSequence must immediately reset IsTalking false.");
        }

        // ---- 5. RestartSequence resets state correctly ----

        [Test]
        public void RestartSequenceResetsStateCorrectly()
        {
            var kane = CreateAnimatorWithBool("IsTalking");
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "AAAAAAAAAA", 10f, 0f, 0f) }); // 1.0s presentation

            c.PlaySequence();
            Step(c, 0.5f);
            Assert.IsTrue(kane.GetBool("IsTalking"), "Sanity: mid-presentation the gesture must be active.");

            c.RestartSequence();
            Assert.IsFalse(kane.GetBool("IsTalking"), "RestartSequence must reset IsTalking false immediately (before the next frame).");

            Step(c, 1f / 60f); // line 0 (Kane) re-presents
            Assert.IsTrue(kane.GetBool("IsTalking"), "After the restart, the Kane line must drive the gesture again.");

            Step(c, 1f);
            Assert.IsTrue(c.IsComplete, "The restarted sequence must still complete naturally.");
            Assert.IsFalse(kane.GetBool("IsTalking"), "After natural completion the gesture must be off.");
        }

        // ---- 6. sequence completion resets false ----

        [Test]
        public void SequenceCompletionResetsIsTalkingFalse()
        {
            var kane = CreateAnimatorWithBool("IsTalking");
            var raven = CreateAnimatorWithBool("IsTalking");
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane), MakeBinding("Raven", raven) });
            c.SetDialogueLines(new[]
            {
                MakeLine("Kane", "AB", 10f, 0f, 0.2f),
                MakeLine("Raven", "CD", 10f, 0f, 0.2f),
            });
            int completed = 0;
            GetCompletionEvent(c).AddListener(() => completed++);

            c.PlaySequence();
            Step(c, 0.1f); // mid Kane line (inside the 0.2s reveal)
            Assert.IsTrue(kane.GetBool("IsTalking"), "Sanity: Kane gesture active on his line.");
            Assert.IsFalse(raven.GetBool("IsTalking"), "Sanity: Raven gesture off on Kane's line.");

            Step(c, 2f); // full run completes (~0.8s)
            Assert.IsTrue(c.IsComplete, "The sequence must complete.");
            Assert.AreEqual(1, completed, "Exactly one completion event.");
            Assert.IsFalse(kane.GetBool("IsTalking"), "Completion must reset the Kane gesture off.");
            Assert.IsFalse(raven.GetBool("IsTalking"), "Completion must reset the Raven gesture off.");
        }

        // ---- 7. missing animator is safe ----

        [Test]
        public void MissingAnimatorIsSafe()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[]
            {
                null,                                // null binding entry
                MakeBinding("Kane", null),           // null animator reference
            });
            c.SetDialogueLines(new[] { MakeLine("Kane", "Hi", 10f, 0f, 0f) });

            Assert.DoesNotThrow(() => { c.PlaySequence(); Step(c, 1f); },
                "Null binding entries and null animators must fail safely.");
            Assert.IsTrue(c.IsComplete, "The sequence must still complete normally.");
        }

        // ---- 8. missing/invalid parameter is safe ----

        [Test]
        public void MissingOrInvalidParameterIsSafe()
        {
            var animator = CreateAnimatorWithBool("OtherParam"); // the controller does NOT have IsTalking/DoesNotExist
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[]
            {
                MakeBinding("Kane", animator, "DoesNotExist"), // parameter absent from the Animator
                MakeBinding("Raven", animator, ""),            // empty parameter name
            });
            c.SetDialogueLines(new[]
            {
                MakeLine("Kane", "Hi", 10f, 0f, 0f),
                MakeLine("Raven", "Yo", 10f, 0f, 0f),
            });

            Assert.DoesNotThrow(() => { c.PlaySequence(); Step(c, 1f); },
                "Missing or empty parameter names must be skipped without throwing.");
            Assert.IsTrue(c.IsComplete, "The sequence must still complete normally.");
            Assert.IsFalse(animator.GetBool("OtherParam"), "Unrelated parameters must remain untouched.");
        }

        // ---- 9. empty binding list preserves previous behavior ----

        [Test]
        public void EmptyBindingsPreservePreviousBehavior()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            // No SetSpeakerAnimationBindings call at all (default empty list) — plus the
            // null-clearing overload must be safe.
            c.SetSpeakerAnimationBindings(null);
            c.SetDialogueLines(new[] { MakeLine("Kane", "Hi", 10f, 0f, 0f) });
            int completed = 0;
            GetCompletionEvent(c).AddListener(() => completed++);

            Assert.DoesNotThrow(() => { c.PlaySequence(); Step(c, 1f); },
                "A controller with no animation bindings must behave exactly as before.");
            Assert.IsTrue(c.IsComplete, "The sequence must complete exactly as before.");
            Assert.AreEqual(1, completed, "The completion event must fire exactly once as before.");
            Assert.IsFalse(c.IsPlaying, "No sequence may be playing after completion.");
        }

        // ---- 10. line start clears other bound speakers ----

        [Test]
        public void LineStartClearsOtherBoundSpeakers()
        {
            var kane = CreateAnimatorWithBool("IsTalking");
            var reyes = CreateAnimatorWithBool("IsTalking");
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane), MakeBinding("Reyes", reyes) });
            c.SetDialogueLines(new[]
            {
                MakeLine("Reyes", "Hi", 10f, 0f, 0.2f), // 0.2s reveal + 0.2s after = 0.4s
                MakeLine("Kane", "Yo", 10f, 0f, 0f),
            });

            c.PlaySequence();
            Step(c, 0.1f); // mid Reyes line
            Assert.IsTrue(reyes.GetBool("IsTalking"), "Reyes gesture must be active on his line.");
            Assert.IsFalse(kane.GetBool("IsTalking"), "Kane gesture must be off on Reyes' line.");

            Step(c, 0.4f); // t = 0.5s: Reyes line ended at 0.4s, Kane line now presenting
            Assert.IsTrue(kane.GetBool("IsTalking"), "Kane gesture must be active once his line begins.");
            Assert.IsFalse(reyes.GetBool("IsTalking"),
                "The previous speaker must be off once the new line begins (line-end + line-start clears).");
        }

        // ---- 11. same-name NON-Bool parameter is skipped safely ----

        [Test]
        public void SameNameNonBoolParameterIsSkippedSafely()
        {
            // The Animator exposes a FLOAT parameter named "IsTalking" and a Bool "OtherParam".
            // A name match alone must NOT be accepted: the type must be Bool.
            var go = new GameObject("MixedParamAnimatorGo");
            go.transform.SetParent(_go.transform, false);
            var animator = go.AddComponent<Animator>();
            var controller = new AnimatorController();
            controller.name = "MixedParam_TestCtrl";
            controller.AddParameter(new AnimatorControllerParameter { name = "IsTalking", type = AnimatorControllerParameterType.Float });
            controller.AddParameter(new AnimatorControllerParameter { name = "OtherParam", type = AnimatorControllerParameterType.Bool });
            animator.runtimeAnimatorController = controller;

            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", animator, "IsTalking") });
            c.SetDialogueLines(new[] { MakeLine("Kane", "Hi", 10f, 0f, 0f) });

            Assert.DoesNotThrow(() => { c.PlaySequence(); Step(c, 1f); },
                "A same-name non-Bool parameter must be skipped without throwing.");
            Assert.IsTrue(c.IsComplete, "The sequence must still complete normally.");
            Assert.AreEqual(0f, animator.GetFloat("IsTalking"), "A Float parameter must never be written by a Bool binding.");
        }
    }
}
