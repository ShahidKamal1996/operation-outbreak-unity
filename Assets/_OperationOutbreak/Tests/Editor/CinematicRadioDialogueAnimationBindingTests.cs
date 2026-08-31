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
    /// FIXTURE NOTE (QA fix #3): a <c>new AnimatorController()</c> created purely in code and
    /// assigned to <c>runtimeAnimatorController</c> during EditMode execution does not expose
    /// its parameters through <c>Animator.parameters</c> (the controller has no properly
    /// initialized native representation until it is a real asset), so the production
    /// HasBoolParameter gate correctly found no parameter and the talking bool was never
    /// driven. The fixture therefore builds each test controller as a REAL temporary
    /// .controller asset (AssetDatabase.CreateAsset) — exactly the production shape (a
    /// controller asset with a bool parameter) — and deletes the assets in TearDown.
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
                if (AssetDatabase.IsValidAsset(path)) AssetDatabase.DeleteAsset(path);
            }
            _tempAssetPaths.Clear();
            if (AssetDatabase.IsValidFolder(TempAssetRoot))
            {
                var remaining = AssetDatabase.FindAssets("t:Asset", new[] { TempAssetRoot });
                if (remaining.Length == 0) AssetDatabase.DeleteAsset(TempAssetRoot);
            }
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

        /// <summary>
        /// Builds a REAL temporary .controller asset with the given parameters. A real asset is
        /// required: only then does the Animator's native side expose the parameters in EditMode
        /// (a code-only AnimatorController does not — see class note).
        /// </summary>
        private AnimatorController CreateControllerAsset(string assetName, params (string Name, AnimatorControllerParameterType Type)[] parameters)
        {
            if (!AssetDatabase.IsValidFolder(TempAssetRoot))
            {
                AssetDatabase.CreateFolder("Assets/_OperationOutbreak/Tests/Editor", "_TempDialogueTestAssets");
            }

            string path = TempAssetRoot + "/" + assetName + ".controller";
            if (AssetDatabase.IsValidAsset(path)) AssetDatabase.DeleteAsset(path); // stale leftover from a crashed run

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
        /// EditMode-safe parameter read: flush any pending parameter state first (no-op in play
        /// mode), then read. The production writes go through Animator.SetBool; this mirrors how
        /// the editor's Animation window makes parameter state observable outside a play loop.
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
            var kane = CreateAnimatorWithParams("KaneAnimator", "KaneTalking",
                ("IsTalking", AnimatorControllerParameterType.Bool));
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "Hello", 10f, 0f, 0f) }); // 0.5s reveal

            Assert.IsFalse(ReadBool(kane, "IsTalking"), "Kane must idle by default (before the sequence).");

            c.PlaySequence();
            Step(c, 0.1f); // mid line 0 (inside the 0.5s presentation)
            Assert.IsTrue(ReadBool(kane, "IsTalking"), "A matching Kane line must set IsTalking true at line start.");
        }

        // ---- 2. non-matching lines never activate the bound speaker ----

        [Test]
        public void NonKaneLinesDoNotActivateKane()
        {
            var kane = CreateAnimatorWithParams("KaneAnimator", "KaneTalking",
                ("IsTalking", AnimatorControllerParameterType.Bool));
            var reyes = CreateAnimatorWithParams("ReyesAnimator", "ReyesTalking",
                ("IsTalking", AnimatorControllerParameterType.Bool));
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane), MakeBinding("Reyes", reyes) });
            c.SetDialogueLines(new[] { MakeLine("Reyes", "Silent", 10f, 0f, 0.1f) }); // 0.6s reveal

            c.PlaySequence();
            Step(c, 0.2f); // mid Reyes line
            Assert.IsTrue(ReadBool(reyes, "IsTalking"), "The matching Reyes line must activate the Reyes binding.");
            Assert.IsFalse(ReadBool(kane, "IsTalking"), "A Reyes line must NOT activate Kane's talking gesture.");
        }

        // ---- 3. finishing the line resets the bool false ----

        [Test]
        public void FinishingKaneLineResetsIsTalkingFalse()
        {
            var kane = CreateAnimatorWithParams("KaneAnimator", "KaneTalking",
                ("IsTalking", AnimatorControllerParameterType.Bool));
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "Hello", 10f, 0f, 0f) }); // 0.5s reveal, no after-delay

            c.PlaySequence();
            Step(c, 0.1f);
            Assert.IsTrue(ReadBool(kane, "IsTalking"), "Sanity: mid-line the gesture must be active.");

            Step(c, 0.5f); // t = 0.6s >= 0.5s -> line finished
            Assert.IsTrue(c.IsComplete, "The single-line sequence must be complete.");
            Assert.IsFalse(ReadBool(kane, "IsTalking"), "IsTalking must be false as soon as the Kane line finishes.");
        }

        // ---- 4. StopSequence resets false ----

        [Test]
        public void StopSequenceResetsIsTalkingFalse()
        {
            var kane = CreateAnimatorWithParams("KaneAnimator", "KaneTalking",
                ("IsTalking", AnimatorControllerParameterType.Bool));
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "AAAAAAAAAA", 10f, 0f, 0f) }); // 1.0s presentation

            c.PlaySequence();
            Step(c, 0.5f); // mid presentation
            Assert.IsTrue(ReadBool(kane, "IsTalking"), "Sanity: mid-presentation the gesture must be active.");

            c.StopSequence();
            Assert.IsFalse(ReadBool(kane, "IsTalking"), "StopSequence must immediately reset IsTalking false.");
        }

        // ---- 5. RestartSequence resets state correctly ----

        [Test]
        public void RestartSequenceResetsStateCorrectly()
        {
            var kane = CreateAnimatorWithParams("KaneAnimator", "KaneTalking",
                ("IsTalking", AnimatorControllerParameterType.Bool));
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane) });
            c.SetDialogueLines(new[] { MakeLine("Kane", "AAAAAAAAAA", 10f, 0f, 0f) }); // 1.0s presentation

            c.PlaySequence();
            Step(c, 0.5f);
            Assert.IsTrue(ReadBool(kane, "IsTalking"), "Sanity: mid-presentation the gesture must be active.");

            c.RestartSequence();
            Assert.IsFalse(ReadBool(kane, "IsTalking"), "RestartSequence must reset IsTalking false immediately (before the next frame).");

            Step(c, 1f / 60f); // line 0 (Kane) re-presents
            Assert.IsTrue(ReadBool(kane, "IsTalking"), "After the restart, the Kane line must drive the gesture again.");

            Step(c, 1f);
            Assert.IsTrue(c.IsComplete, "The restarted sequence must still complete naturally.");
            Assert.IsFalse(ReadBool(kane, "IsTalking"), "After natural completion the gesture must be off.");
        }

        // ---- 6. sequence completion resets false ----

        [Test]
        public void SequenceCompletionResetsIsTalkingFalse()
        {
            var kane = CreateAnimatorWithParams("KaneAnimator", "KaneTalking",
                ("IsTalking", AnimatorControllerParameterType.Bool));
            var raven = CreateAnimatorWithParams("RavenAnimator", "RavenTalking",
                ("IsTalking", AnimatorControllerParameterType.Bool));
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
            Assert.IsTrue(ReadBool(kane, "IsTalking"), "Sanity: Kane gesture active on his line.");
            Assert.IsFalse(ReadBool(raven, "IsTalking"), "Sanity: Raven gesture off on Kane's line.");

            Step(c, 2f); // full run completes (~0.8s)
            Assert.IsTrue(c.IsComplete, "The sequence must complete.");
            Assert.AreEqual(1, completed, "Exactly one completion event.");
            Assert.IsFalse(ReadBool(kane, "IsTalking"), "Completion must reset the Kane gesture off.");
            Assert.IsFalse(ReadBool(raven, "IsTalking"), "Completion must reset the Raven gesture off.");
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
            var kane = CreateAnimatorWithParams("KaneAnimator", "KaneTalking",
                ("IsTalking", AnimatorControllerParameterType.Bool));
            var reyes = CreateAnimatorWithParams("ReyesAnimator", "ReyesTalking",
                ("IsTalking", AnimatorControllerParameterType.Bool));
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetSpeakerAnimationBindings(new[] { MakeBinding("Kane", kane), MakeBinding("Reyes", reyes) });
            c.SetDialogueLines(new[]
            {
                MakeLine("Reyes", "Hi", 10f, 0f, 0.2f), // 0.2s reveal + 0.2s after = 0.4s
                MakeLine("Kane", "Yo", 10f, 0f, 0f),
            });

            c.PlaySequence();
            Step(c, 0.1f); // mid Reyes line
            Assert.IsTrue(ReadBool(reyes, "IsTalking"), "Reyes gesture must be active on his line.");
            Assert.IsFalse(ReadBool(kane, "IsTalking"), "Kane gesture must be off on Reyes' line.");

            Step(c, 0.4f); // t = 0.5s: Reyes line ended at 0.4s, Kane line now presenting
            Assert.IsTrue(ReadBool(kane, "IsTalking"), "Kane gesture must be active once his line begins.");
            Assert.IsFalse(ReadBool(reyes, "IsTalking"),
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
