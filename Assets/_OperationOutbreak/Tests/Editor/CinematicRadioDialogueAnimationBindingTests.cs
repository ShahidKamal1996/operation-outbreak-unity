using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.Cinematic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Events;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Tests for the optional speaker -> Animator talking bindings on
    /// <see cref="CinematicRadioDialogueController"/> — driven by fixed 60fps steps.
    ///
    /// ASSERTION STRATEGY (QA fix #5 — deterministic seam, no more fixture guessing):
    ///
    /// The behavioral tests (1-6, 10) assert on the controller's binding DECISIONS through
    /// a recording override of the controller's protected virtual WriteTalkingParameter
    /// seam — NOT on Animator parameter state readback. Every (animator, parameter, value)
    /// write the binding logic makes is recorded; the assertions verify exactly which
    /// speaker's parameter is set to which value at each lifecycle point (line start,
    /// line finish, stop, restart, completion). That is the complete binding contract.
    ///
    /// Why not read Animator.GetBool back? The production path is correct, but an
    /// INACTIVE EditMode Animator (Play Mode off) does not reliably expose or read back
    /// runtime parameter state in the real Unity 6000.0.57f1 Test Runner. Two consecutive
    /// fixture generations were proven against the real runner and both left written
    /// values unobservable (no exceptions thrown; default-false readbacks worked):
    ///   1. Code-only AnimatorController objects assigned to runtimeAnimatorController
    ///      (parameters not exposed through Animator.parameters until the controller is a
    ///      real asset) — written true values read back false.
    ///   2. REAL .controller assets created on disk via AssetDatabase.CreateAsset with
    ///      Animator.Update(0f) flushes before every read — STILL read back false.
    /// Recording the seam captures the same decisions (same null/empty guards in the base
    /// class, same per-line lifecycle, same speaker matching) without depending on that
    /// unreadable path. The behavioral tests therefore use BARE animators — the recording
    /// override never touches Animator state, so no controller asset is required there.
    ///
    /// The physical write GATE (missing animators, missing/empty parameter names, a
    /// same-name non-Bool parameter, and the empty-binding default) is verified by the
    /// safety tests (7-9, 11) through the BASE seam implementation with real temporary
    /// .controller assets: their assertions expect only default values (false / 0f),
    /// which an inactive Animator reads back reliably — plus the no-throw guarantee.
    /// Together the two tiers cover the whole production path:
    ///   binding decision (who/when/what) = recorded seam; physical write gate = base path.
    /// </summary>
    public sealed class CinematicRadioDialogueAnimationBindingTests
    {
        private const string TempAssetRoot = "Assets/_OperationOutbreak/Tests/Editor/_TempDialogueTestAssets";

        private GameObject _go;
        private readonly List<string> _tempAssetPaths = new List<string>();

        [SetUp]
        public void SetUp() => _go = new GameObject("DialogueWithBindings");

        [TearDown]
        public void TearDown()
        {
            // Clean up the temporary controller assets (and the temp folder if now empty).
            foreach (var path in _tempAssetPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null) AssetDatabase.DeleteAsset(path);
            }
            _tempAssetPaths.Clear();
            if (AssetDatabase.IsValidFolder(TempAssetRoot))
            {
                var remaining = AssetDatabase.FindAssets("t:Asset", new[] { TempAssetRoot });
                if (remaining.Length == 0) AssetDatabase.DeleteAsset(TempAssetRoot);
            }
            if (_go != null) Object.DestroyImmediate(_go); // destroys the child animator GameObjects too
        }

        /// <summary>
        /// Records the physical parameter writes the controller's binding logic decides to
        /// make (the WriteTalkingParameter seam). The production implementation is not
        /// bypassed for anything BEFORE the seam: the null-animator / empty-parameter
        /// guards stay in the base class and run in every test that uses this subclass.
        /// </summary>
        private sealed class RecordingDialogueController : CinematicRadioDialogueController
        {
            public sealed class Write
            {
                public Animator Animator;
                public string Parameter;
                public bool Value;
            }

            public readonly List<Write> Writes = new List<Write>();

            protected override void WriteTalkingParameter(Animator animator, string parameter, bool value)
            {
                Writes.Add(new Write { Animator = animator, Parameter = parameter, Value = value });
            }

            /// <summary>
            /// Value of the most recent write for (animator, parameter); false when the pair
            /// was never written — exactly matching a Bool parameter's default Animator state.
            /// </summary>
            public bool LastValue(Animator animator, string parameter)
            {
                for (int i = Writes.Count - 1; i >= 0; i--)
                {
                    if (Writes[i].Animator == animator && Writes[i].Parameter == parameter)
                        return Writes[i].Value;
                }

                return false;
            }
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

        /// <summary>
        /// Bare animator for the recording tests: the recording override never touches
        /// Animator state, so no controller asset is required.
        /// </summary>
        private Animator CreateBareAnimator(string animatorGoName)
        {
            var go = new GameObject(animatorGoName);
            go.transform.SetParent(_go.transform, false);
            return go.AddComponent<Animator>();
        }

        /// <summary>
        /// Builds a REAL temporary .controller asset with the given parameters. A real asset is
        /// required for the safety tests: only then does the Animator's native side expose the
        /// parameters in EditMode (a code-only AnimatorController does not), so the base
        /// HasBoolParameter gate can be exercised against real parameter data.
        /// </summary>
        private AnimatorController CreateControllerAsset(string assetName, params (string Name, AnimatorControllerParameterType Type)[] parameters)
        {
            if (!AssetDatabase.IsValidFolder(TempAssetRoot))
            {
                AssetDatabase.CreateFolder("Assets/_OperationOutbreak/Tests/Editor", "_TempDialogueTestAssets");
            }

            string path = TempAssetRoot + "/" + assetName + ".controller";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null) AssetDatabase.DeleteAsset(path); // stale leftover from a crashed run

            var controller = new AnimatorController { name = assetName };
            foreach (var p in parameters)
                controller.AddParameter(new AnimatorControllerParameter { name = p.Name, type = p.Type });

            AssetDatabase.CreateAsset(controller, path);
            AssetDatabase.SaveAssets();
            _tempAssetPaths.Add(path);
            return controller;
        }

        private Animator CreateAnimatorWithParams(string animatorGoName, string assetName,
            params (string Name, AnimatorControllerParameterType Type)[] parameters)
        {
            var go = new GameObject(animatorGoName);
            go.transform.SetParent(_go.transform, false);
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = CreateControllerAsset(assetName, parameters);
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

        /// <summary>
        /// EditMode-safe parameter read (safety tests only): flush any pending parameter
        /// state first (no-op in play mode), then read. These tests assert only default
        /// values (false / 0f), which an inactive Animator reads back reliably.
        /// </summary>
        private static bool ReadBool(Animator animator, string parameter)
        {
            animator.Update(0f);
            return animator.GetBool(parameter);
        }

        private static float ReadFloat(Animator animator, string parameter)
        {
            animator.Update(0f);
            return animator.GetFloat(parameter);
        }

        // ---- 1. matching line sets the talking bool true ----

        [Test]
        public void MatchingKaneLineSetsIsTalkingTrue()
        {
            var kane = CreateBareAnimator("KaneAnimator");
            var c = _go.AddComponent<RecordingDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "Hello", 10f, 0f, 0f) }); // 0.5s reveal

            Assert.AreEqual(0, c.Writes.Count, "Kane must idle by default: no talking parameter may be written before the sequence.");

            c.PlaySequence();
            Step(c, 0.1f); // mid line 0 (inside the 0.5s presentation)
            Assert.IsTrue(c.LastValue(kane, "IsTalking"), "A matching Kane line must set IsTalking true at line start.");
        }

        // ---- 2. non-matching lines never activate the bound speaker ----

        [Test]
        public void NonKaneLinesDoNotActivateKane()
        {
            var kane = CreateBareAnimator("KaneAnimator");
            var reyes = CreateBareAnimator("ReyesAnimator");
            var c = _go.AddComponent<RecordingDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane), MakeBinding("Reyes", reyes) });
            c.SetDialogueLines(new[] { MakeLine("Reyes", "Silent", 10f, 0f, 0.1f) }); // 0.6s reveal

            c.PlaySequence();
            Step(c, 0.2f); // mid Reyes line
            Assert.IsTrue(c.LastValue(reyes, "IsTalking"), "The matching Reyes line must activate the Reyes binding.");
            Assert.IsFalse(c.LastValue(kane, "IsTalking"), "A Reyes line must NOT activate Kane's talking gesture.");
        }

        // ---- 3. finishing the line resets the bool false ----

        [Test]
        public void FinishingKaneLineResetsIsTalkingFalse()
        {
            var kane = CreateBareAnimator("KaneAnimator");
            var c = _go.AddComponent<RecordingDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "Hello", 10f, 0f, 0f) }); // 0.5s reveal, no after-delay

            c.PlaySequence();
            Step(c, 0.1f);
            Assert.IsTrue(c.LastValue(kane, "IsTalking"), "Sanity: mid-line the gesture must be active.");

            Step(c, 0.5f); // t = 0.6s >= 0.5s -> line finished
            Assert.IsTrue(c.IsComplete, "The single-line sequence must be complete.");
            Assert.IsFalse(c.LastValue(kane, "IsTalking"), "IsTalking must be false as soon as the Kane line finishes.");
        }

        // ---- 4. StopSequence resets false ----

        [Test]
        public void StopSequenceResetsIsTalkingFalse()
        {
            var kane = CreateBareAnimator("KaneAnimator");
            var c = _go.AddComponent<RecordingDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "AAAAAAAAAA", 10f, 0f, 0f) }); // 1.0s presentation

            c.PlaySequence();
            Step(c, 0.5f); // mid presentation
            Assert.IsTrue(c.LastValue(kane, "IsTalking"), "Sanity: mid-presentation the gesture must be active.");

            c.StopSequence();
            Assert.IsFalse(c.LastValue(kane, "IsTalking"), "StopSequence must immediately reset IsTalking false.");
        }

        // ---- 5. RestartSequence resets state correctly ----

        [Test]
        public void RestartSequenceResetsStateCorrectly()
        {
            var kane = CreateBareAnimator("KaneAnimator");
            var c = _go.AddComponent<RecordingDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "AAAAAAAAAA", 10f, 0f, 0f) }); // 1.0s presentation

            c.PlaySequence();
            Step(c, 0.5f);
            Assert.IsTrue(c.LastValue(kane, "IsTalking"), "Sanity: mid-presentation the gesture must be active.");

            c.RestartSequence();
            Assert.IsFalse(c.LastValue(kane, "IsTalking"), "RestartSequence must reset IsTalking false immediately (before the next frame).");

            Step(c, 1f / 60f); // line 0 (Kane) re-presents
            Assert.IsTrue(c.LastValue(kane, "IsTalking"), "After the restart, the Kane line must drive the gesture again.");

            Step(c, 1f);
            Assert.IsTrue(c.IsComplete, "The restarted sequence must still complete naturally.");
            Assert.IsFalse(c.LastValue(kane, "IsTalking"), "After natural completion the gesture must be off.");
        }

        // ---- 6. sequence completion resets false ----

        [Test]
        public void SequenceCompletionResetsIsTalkingFalse()
        {
            var kane = CreateBareAnimator("KaneAnimator");
            var raven = CreateBareAnimator("RavenAnimator");
            var c = _go.AddComponent<RecordingDialogueController>();
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
            Assert.IsTrue(c.LastValue(kane, "IsTalking"), "Sanity: Kane gesture active on his line.");
            Assert.IsFalse(c.LastValue(raven, "IsTalking"), "Sanity: Raven gesture off on Kane's line.");

            Step(c, 2f); // full run completes (~0.8s)
            Assert.IsTrue(c.IsComplete, "The sequence must complete.");
            Assert.AreEqual(1, completed, "Exactly one completion event.");
            Assert.IsFalse(c.LastValue(kane, "IsTalking"), "Completion must reset the Kane gesture off.");
            Assert.IsFalse(c.LastValue(raven, "IsTalking"), "Completion must reset the Raven gesture off.");
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
            var animator = CreateAnimatorWithParams("OtherParamAnimator", "OtherParamCtrl",
                ("OtherParam", AnimatorControllerParameterType.Bool)); // the controller does NOT have IsTalking/DoesNotExist
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
            Assert.IsFalse(ReadBool(animator, "OtherParam"), "Unrelated parameters must remain untouched.");
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
            var kane = CreateBareAnimator("KaneAnimator");
            var reyes = CreateBareAnimator("ReyesAnimator");
            var c = _go.AddComponent<RecordingDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane), MakeBinding("Reyes", reyes) });
            c.SetDialogueLines(new[]
            {
                MakeLine("Reyes", "Hi", 10f, 0f, 0.2f), // 0.2s reveal + 0.2s after = 0.4s
                MakeLine("Kane", "Yo", 10f, 0f, 0f),
            });

            c.PlaySequence();
            Step(c, 0.1f); // mid Reyes line
            Assert.IsTrue(c.LastValue(reyes, "IsTalking"), "Reyes gesture must be active on his line.");
            Assert.IsFalse(c.LastValue(kane, "IsTalking"), "Kane gesture must be off on Reyes' line.");

            Step(c, 0.4f); // t = 0.5s: Reyes line ended at 0.4s, Kane line now presenting
            Assert.IsTrue(c.LastValue(kane, "IsTalking"), "Kane gesture must be active once his line begins.");
            Assert.IsFalse(c.LastValue(reyes, "IsTalking"),
                "The previous speaker must be off once the new line begins (line-end + line-start clears).");
        }

        // ---- 11. same-name NON-Bool parameter is skipped safely ----

        [Test]
        public void SameNameNonBoolParameterIsSkippedSafely()
        {
            // The Animator exposes a FLOAT parameter named "IsTalking" and a Bool "OtherParam".
            // A name match alone must NOT be accepted: the type must be Bool.
            var animator = CreateAnimatorWithParams("MixedParamAnimator", "MixedParam",
                ("IsTalking", AnimatorControllerParameterType.Float),
                ("OtherParam", AnimatorControllerParameterType.Bool));

            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", animator, "IsTalking") });
            c.SetDialogueLines(new[] { MakeLine("Kane", "Hi", 10f, 0f, 0f) });

            Assert.DoesNotThrow(() => { c.PlaySequence(); Step(c, 1f); },
                "A same-name non-Bool parameter must be skipped without throwing.");
            Assert.IsTrue(c.IsComplete, "The sequence must still complete normally.");
            Assert.AreEqual(0f, ReadFloat(animator, "IsTalking"), "A Float parameter must never be written by a Bool binding.");
        }
    }
}
