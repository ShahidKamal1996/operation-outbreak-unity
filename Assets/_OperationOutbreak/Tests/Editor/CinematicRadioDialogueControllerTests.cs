using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.Cinematic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Tests for <see cref="CinematicRadioDialogueController"/> — deterministic state/API
    /// behavior driven by fixed <c>AdvanceSequence</c> steps (no real-time waits).
    /// </summary>
    public sealed class CinematicRadioDialogueControllerTests
    {
        private GameObject _go;
        private GameObject _canvasGo;

        [SetUp]
        public void SetUp() => _go = new GameObject("RadioDialogue");

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, field + " must exist on " + target.GetType().Name + ".");
            f.SetValue(target, value);
        }

        private static UnityEvent GetCompletionEvent(CinematicRadioDialogueController c)
        {
            var f = c.GetType().GetField("onSequenceCompleted", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "onSequenceCompleted must exist.");
            var evt = (UnityEvent)f.GetValue(c);
            // Regression guard (QA fix #1): a component created with AddComponent in EditMode is
            // not serialized through a scene/prefab, so the field initializer is its only
            // initialization. It must be a live (non-null) UnityEvent, not null.
            Assert.IsNotNull(evt, "onSequenceCompleted must be non-null on a dynamically created component (field initialization).");
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

        /// <summary>Steps the sequence by an EXACT whole-frame count at 60fps.</summary>
        private static void Step(CinematicRadioDialogueController c, float seconds, float step = 1f / 60f)
        {
            int frames = (int)Mathf.Round(seconds / step);
            for (int i = 0; i < frames; i++) c.AdvanceSequence(step);
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

        // ---- 1. initial state ----

        [Test]
        public void InitialStateIsCorrect()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            Assert.IsFalse(c.IsPlaying, "Not playing before PlaySequence.");
            Assert.IsFalse(c.IsTyping, "Not typing before PlaySequence.");
            Assert.IsFalse(c.IsComplete, "Not complete before PlaySequence.");
            Assert.AreEqual(-1, c.CurrentLineIndex, "No active line before PlaySequence.");
            Assert.IsFalse(c.UseUnscaledTime, "Scaled time must be the default (useUnscaledTime = false).");
        }

        // ---- 2. empty sequence ----

        [Test]
        public void EmptySequenceStartsAndCompletesSafely()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            int completed = 0;
            GetCompletionEvent(c).AddListener(() => completed++);

            c.SetDialogueLines(new RadioDialogueLine[0]);
            Assert.DoesNotThrow(() => c.PlaySequence(), "An empty sequence must start without throwing.");
            Assert.IsFalse(c.IsPlaying, "An empty sequence must not stay playing.");
            Assert.IsTrue(c.IsComplete, "An empty sequence must complete.");
            Assert.AreEqual(-1, c.CurrentLineIndex, "An empty sequence has no active line.");
            Assert.AreEqual(1, completed, "OnSequenceCompleted must fire exactly once for an empty sequence.");

            Assert.DoesNotThrow(() => Step(c, 1f), "Advancing after an empty-sequence completion must be safe.");
            Assert.IsTrue(c.IsComplete && !c.IsPlaying, "State must be stable after completion.");
            Assert.AreEqual(1, completed, "The completion event must not fire a second time.");

            // A null line set is handled the same way.
            c.SetDialogueLines(null);
            c.StopSequence();
            Assert.DoesNotThrow(() => c.PlaySequence(), "A null line set must start without throwing.");
            Assert.IsTrue(c.IsComplete, "A null line set must complete.");
        }

        // ---- 3. stop resets active playback state ----

        [Test]
        public void StopSequenceResetsActivePlaybackState()
        {
            var voiceGo = new GameObject("VoiceSource");
            voiceGo.transform.SetParent(_go.transform, false);
            var sfxGo = new GameObject("SfxSource");
            sfxGo.transform.SetParent(_go.transform, false);

            var c = _go.AddComponent<CinematicRadioDialogueController>();
            SetPrivate(c, "dialogueText", CreateTmpText("Dialogue"));
            SetPrivate(c, "voiceAudioSource", voiceGo.AddComponent<AudioSource>());
            SetPrivate(c, "sfxAudioSource", sfxGo.AddComponent<AudioSource>());
            c.SetDialogueLines(new[]
            {
                MakeLine("A", "Hello world", null, null, 10f, 0.1f, 10f), // long after-delay keeps the run active
                MakeLine("B", "Next", null, null, 10f, 0f, 0f),
            });

            c.PlaySequence();
            Step(c, 0.2f);
            Assert.IsTrue(c.IsPlaying, "Sanity: the sequence must be mid-line-0 when stopped.");
            Assert.AreEqual(0, c.CurrentLineIndex, "Sanity: line 0 must be active.");
            Assert.IsTrue(c.IsTyping, "Sanity: the typewriter must be mid-reveal.");

            Assert.DoesNotThrow(() => c.StopSequence(), "Stopping with assigned audio sources must not throw.");
            Assert.IsFalse(c.IsPlaying, "Stop must clear IsPlaying.");
            Assert.IsFalse(c.IsTyping, "Stop must clear IsTyping.");
            Assert.IsFalse(c.IsComplete, "Stop must not mark the sequence complete.");
            Assert.AreEqual(-1, c.CurrentLineIndex, "Stop must reset CurrentLineIndex to -1.");

            Step(c, 5f);
            Assert.IsFalse(c.IsPlaying && !c.IsComplete, "No sequence may continue after a stop.");
            Assert.AreEqual(-1, c.CurrentLineIndex, "No line may become active after a stop.");
        }

        // ---- 4. restart resets and starts from beginning ----

        [Test]
        public void RestartSequenceResetsIndexAndStartsFromBeginning()
        {
            var dialogue = CreateTmpText("Dialogue");
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            SetPrivate(c, "dialogueText", dialogue);
            int completed = 0;
            GetCompletionEvent(c).AddListener(() => completed++);
            c.SetDialogueLines(new[]
            {
                MakeLine("A", "First", null, null, 100f, 0f, 0.05f),
                MakeLine("B", "Second", null, null, 100f, 0f, 0.05f),
            });

            c.PlaySequence();
            Step(c, 0.15f);
            Assert.AreEqual(1, c.CurrentLineIndex, "Sanity: the run must be on line 1 before the restart.");
            Assert.IsTrue(c.IsPlaying, "Sanity: the run must still be active.");

            c.RestartSequence();
            Assert.IsTrue(c.IsPlaying, "Restart must start the sequence again.");
            Assert.IsFalse(c.IsComplete, "Restart must clear the completion flag.");
            Assert.AreEqual(0, c.CurrentLineIndex, "Restart must go back to line 0.");

            Step(c, 1f / 60f);
            Assert.AreEqual("First", dialogue.text, "Restart must re-present line 0's text.");

            Step(c, 1f);
            Assert.IsTrue(c.IsComplete, "The restarted sequence must still complete naturally.");
            Assert.AreEqual(1, completed, "Exactly one completion event across the interrupted run + the restart.");
        }

        // ---- 5. invalid/null entries ----

        [Test]
        public void InvalidOrNullEntriesDoNotThrow()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetDialogueLines(new[]
            {
                null,
                MakeLine(null, null, null, null, 0f, 0f, 0f),
                MakeLine("", "", null, null, -5f, -1f, -2f), // negative timings must be clamped, not hang
            });

            Assert.DoesNotThrow(() => c.PlaySequence(), "Invalid entries must not throw on start.");
            Assert.DoesNotThrow(() => Step(c, 2f), "Invalid entries must not throw or hang while advancing.");
            Assert.IsTrue(c.IsComplete, "A sequence of invalid entries must still complete safely.");
            Assert.IsFalse(c.IsPlaying, "The sequence must not stay playing after invalid entries.");
        }

        // ---- 6. CharactersPerSecond <= 0 ----

        [Test]
        public void ZeroCharactersPerSecondNeverHangs()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetDialogueLines(new[] { MakeLine("S", "Hello", null, null, 0f, 0f, 0.1f) });

            c.PlaySequence();
            Assert.IsFalse(c.IsTyping, "With CharactersPerSecond <= 0 there is nothing to type.");

            // Bounded run: if <=0 cps caused an infinite reveal, this would never complete.
            Step(c, 5f);
            Assert.IsTrue(c.IsComplete, "A zero-cps line must be treated as instantly revealed, not infinite.");
            Assert.IsFalse(c.IsPlaying, "The sequence must end after the zero-cps line.");
        }

        // ---- 7. completion state ----

        [Test]
        public void CompletionStateIsReachedCorrectly()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            int completed = 0;
            GetCompletionEvent(c).AddListener(() => completed++);
            // Two identical lines: 0.2s before + 0.2s reveal (2 chars @ 10cps) + 0.3s after = 0.7s each.
            c.SetDialogueLines(new[]
            {
                MakeLine("S", "AB", null, null, 10f, 0.2f, 0.3f),
                MakeLine("S", "AB", null, null, 10f, 0.2f, 0.3f),
            });

            c.PlaySequence();
            Assert.IsTrue(c.IsPlaying && !c.IsComplete, "A fresh non-empty sequence must be playing, not complete.");

            Step(c, 0.5f);
            Assert.IsTrue(c.IsPlaying && !c.IsComplete, "Mid-sequence the run must still be active.");
            Assert.AreEqual(0, c.CurrentLineIndex, "At 0.5s the run must still be inside line 0 (0.7s total).");

            Step(c, 0.5f); // t = 1.0s
            Assert.AreEqual(1, c.CurrentLineIndex, "At 1.0s the run must be on line 1 (line 0 ends at 0.7s).");
            Assert.IsFalse(c.IsComplete, "The sequence must not be complete before line 1 ends (~1.4s).");

            Step(c, 0.5f); // t = 1.5s >= 1.4s
            Assert.IsFalse(c.IsPlaying, "Natural completion must clear IsPlaying.");
            Assert.IsFalse(c.IsTyping, "Natural completion must clear IsTyping.");
            Assert.IsTrue(c.IsComplete, "Natural completion must set IsComplete.");
            Assert.AreEqual(-1, c.CurrentLineIndex, "No line may be active after completion.");
            Assert.AreEqual(1, completed, "OnSequenceCompleted must fire exactly once.");

            Step(c, 1f);
            Assert.AreEqual(1, completed, "The completion event must never fire again.");
        }

        // ---- 8. stop prevents continuation ----

        [Test]
        public void StopSequencePreventsContinuation()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            int completed = 0;
            GetCompletionEvent(c).AddListener(() => completed++);
            c.SetDialogueLines(new[] { MakeLine("S", "AB", null, null, 10f, 0.1f, 1f) });

            c.PlaySequence();
            Step(c, 0.15f); // mid line 0 (line lasts ~1.3s)
            Assert.IsTrue(c.IsPlaying, "Sanity: mid-sequence before the stop.");

            c.StopSequence();
            Step(c, 5f);
            Assert.IsFalse(c.IsPlaying, "Nothing may be playing after a stop.");
            Assert.IsFalse(c.IsComplete, "A stopped sequence must never be reported complete.");
            Assert.AreEqual(-1, c.CurrentLineIndex, "No line may advance after a stop.");
            Assert.AreEqual(0, completed, "StopSequence must not invoke the completion callback.");
        }

        // ---- 9. CurrentLineIndex transitions ----

        [Test]
        public void CurrentLineIndexTransitionsCorrectly()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            // Three quick lines: 1 char @ 100cps (0.01s) + 0.1s after each => ~0.11s per line.
            c.SetDialogueLines(new[]
            {
                MakeLine("A", "A", null, null, 100f, 0f, 0.1f),
                MakeLine("B", "B", null, null, 100f, 0f, 0.1f),
                MakeLine("C", "C", null, null, 100f, 0f, 0.1f),
            });

            c.PlaySequence();
            Step(c, 0.05f); // 3 frames — inside line 0 (ends ~0.11s)
            Assert.AreEqual(0, c.CurrentLineIndex, "Index must be 0 inside line 0.");

            Step(c, 0.1f); // t ~ 0.15s — line 0 done (~0.11s), line 1 active
            Assert.AreEqual(1, c.CurrentLineIndex, "Index must be 1 inside line 1.");

            Step(c, 0.1f); // t ~ 0.25s — line 1 done (~0.22s), line 2 active
            Assert.AreEqual(2, c.CurrentLineIndex, "Index must be 2 inside line 2.");

            Step(c, 0.2f); // t ~ 0.45s — everything done (~0.33s)
            Assert.IsTrue(c.IsComplete, "All three lines must have completed.");
            Assert.AreEqual(-1, c.CurrentLineIndex, "Index must return to -1 after completion.");
        }

        // ---- 10. repeated PlaySequence calls ----

        [Test]
        public void RepeatedPlaySequenceCallsDoNotOverlap()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            int completed = 0;
            GetCompletionEvent(c).AddListener(() => completed++);
            c.SetDialogueLines(new[]
            {
                MakeLine("A", "AA", null, null, 10f, 0f, 0.2f), // 0.4s per line
                MakeLine("B", "BB", null, null, 10f, 0f, 0.2f),
            });

            c.PlaySequence();
            Step(c, 0.1f); // mid first run
            c.PlaySequence(); // must stop the active run, not stack a second one
            c.PlaySequence(); // again

            Assert.IsTrue(c.IsPlaying, "After re-plays the sequence must be playing.");
            Assert.AreEqual(0, c.CurrentLineIndex, "Re-plays must restart from line 0.");

            Step(c, 1f);
            Assert.IsTrue(c.IsComplete, "The single restarted sequence must complete.");
            Assert.IsFalse(c.IsPlaying, "Nothing may be playing after completion.");
            Assert.AreEqual(1, completed,
                "Exactly ONE completion event: interrupted runs never complete, only the final run does.");
        }

        // ---- 11. typewriter via maxVisibleCharacters ----

        [Test]
        public void TypewriterRevealsProgressivelyViaMaxVisibleCharacters()
        {
            var speaker = CreateTmpText("Speaker");
            var dialogue = CreateTmpText("Dialogue");
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            SetPrivate(c, "speakerLabel", speaker);
            SetPrivate(c, "dialogueText", dialogue);
            // "Hello" (5 chars) @ 10 cps => 0.5s reveal; no delays.
            c.SetDialogueLines(new[] { MakeLine("Reyes", "Hello", null, null, 10f, 0f, 0f) });

            c.PlaySequence();

            Step(c, 0.1f); // 6 frames
            Assert.AreEqual(1, dialogue.maxVisibleCharacters, "After 0.1s exactly 1 character must be visible.");
            Assert.AreEqual("Hello", dialogue.text, "The full text must be assigned once, never substrings.");
            Assert.AreEqual("Reyes", speaker.text, "The speaker label must show the line's speaker.");
            Assert.IsTrue(c.IsTyping, "The typewriter must be active mid-reveal.");

            Step(c, 0.1f);
            Assert.AreEqual(2, dialogue.maxVisibleCharacters, "After 0.2s exactly 2 characters must be visible.");
            Step(c, 0.1f);
            Assert.AreEqual(3, dialogue.maxVisibleCharacters, "After 0.3s exactly 3 characters must be visible.");
            Step(c, 0.1f);
            Assert.AreEqual(4, dialogue.maxVisibleCharacters, "After 0.4s exactly 4 characters must be visible.");

            Step(c, 0.2f); // reveal completes at 0.5s
            Assert.IsTrue(c.IsComplete, "The line must complete after the full reveal.");
            Assert.IsFalse(c.IsTyping, "Typing must end at completion.");
            Assert.AreEqual(5, dialogue.maxVisibleCharacters, "At completion the COMPLETE text must be exposed.");
            Assert.AreEqual(dialogue.text.Length, dialogue.maxVisibleCharacters, "maxVisibleCharacters must equal the text length.");
            Assert.AreEqual("Hello", dialogue.text, "The final text must be the complete original text.");
        }

        // ---- 12. empty and very short lines ----

        [Test]
        public void TypewriterHandlesEmptyAndShortLines()
        {
            var dialogue = CreateTmpText("Dialogue");
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            SetPrivate(c, "dialogueText", dialogue);
            c.SetDialogueLines(new[]
            {
                MakeLine("", "", null, null, 40f, 0f, 0f), // empty line: instant
                MakeLine("S", "Q", null, null, 40f, 0f, 0f), // 1 char @ 40cps => 0.025s
            });

            c.PlaySequence();

            Step(c, 0.01f); // 1 frame
            Assert.AreEqual(1, c.CurrentLineIndex, "The empty line must be skipped instantly to line 1.");
            Assert.IsTrue(c.IsTyping, "The short line must be mid-reveal.");
            Assert.AreEqual(0, dialogue.maxVisibleCharacters, "A 1-char reveal is not complete after one frame.");
            Assert.AreEqual("Q", dialogue.text, "The short line's full text must be assigned.");

            Step(c, 0.01f); // 2nd frame (0.025s needed; 2 frames = 0.033s)
            Assert.IsTrue(c.IsComplete, "The short line must complete right after its reveal time.");
            Assert.AreEqual(1, dialogue.maxVisibleCharacters, "The complete short text must be exposed at completion.");
            Assert.AreEqual(-1, c.CurrentLineIndex, "Index must return to -1 after completion.");
        }

        // ---- 13. missing TMP references ----

        [Test]
        public void MissingTmpReferencesAreSafe()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            // No speakerLabel / dialogueText assigned at all.
            c.SetDialogueLines(new[] { MakeLine("S", "Some words", null, null, 40f, 0.05f, 0.1f) });

            Assert.DoesNotThrow(() => c.PlaySequence(), "Missing TMP references must not throw on start.");
            Assert.DoesNotThrow(() => Step(c, 1f), "Missing TMP references must not throw while advancing.");
            Assert.IsTrue(c.IsComplete, "Without a TMP text the reveal is instantaneous and the line completes.");
            Assert.IsFalse(c.IsPlaying, "The sequence must end normally.");
        }

        // ---- 14. voice clip extends line duration deterministically ----

        [Test]
        public void VoiceClipExtendsLineDurationDeterministically()
        {
            // No audio source assigned: the deterministic voice gate is the clip's own length.
            var voice = AudioClip.Create("TestVoice", 48000, 1, 48000, false); // exactly 1.0s
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            c.SetDialogueLines(new[]
            {
                MakeLine("", "", voice, null, 40f, 0f, 0.2f), // 1.0s voice + 0.2s after
                MakeLine("S", "Done", null, null, 40f, 0f, 0f),
            });

            c.PlaySequence();

            Step(c, 0.5f);
            Assert.IsTrue(c.IsPlaying, "At 0.5s the 1.0s voice must still be gating the line.");
            Assert.AreEqual(0, c.CurrentLineIndex, "Line 0 must still be active at 0.5s.");
            Assert.IsFalse(c.IsTyping, "A voice-only line has nothing to type.");

            Step(c, 0.6f); // t = 1.1s: voice done at 1.0s, after-delay (to 1.2s) still running
            Assert.AreEqual(0, c.CurrentLineIndex, "Line 0 must still be active during its after-delay.");
            Assert.IsFalse(c.IsComplete, "Line 1 must not start before line 0's after-delay ends (~1.2s).");

            Step(c, 0.3f); // t = 1.4s: line 1 (0.1s reveal) is done
            Assert.IsTrue(c.IsComplete, "The full sequence must complete after voice + delays + line 1.");
            Assert.AreEqual(-1, c.CurrentLineIndex, "Index must return to -1 after completion.");
        }

        // ---- 15. stop with assigned audio sources ----

        [Test]
        public void StopSequenceStopsAssignedAudioSources()
        {
            var voiceGo = new GameObject("VoiceSource");
            voiceGo.transform.SetParent(_go.transform, false);
            var voice = voiceGo.AddComponent<AudioSource>();
            var sfxGo = new GameObject("SfxSource");
            sfxGo.transform.SetParent(_go.transform, false);
            var sfx = sfxGo.AddComponent<AudioSource>();

            var voiceClip = AudioClip.Create("TestVoice2", 96000, 1, 48000, false); // 2.0s
            var sfxClip = AudioClip.Create("TestSfx", 4800, 1, 48000, false); // 0.1s

            var c = _go.AddComponent<CinematicRadioDialogueController>();
            SetPrivate(c, "voiceAudioSource", voice);
            SetPrivate(c, "sfxAudioSource", sfx);
            c.SetDialogueLines(new[] { MakeLine("S", "", voiceClip, sfxClip, 40f, 0f, 5f) });

            c.PlaySequence();
            Step(c, 0.05f); // line 0 presenting (SFX + voice were requested this frame)
            Assert.IsTrue(c.IsPlaying, "Sanity: mid-sequence before the stop.");

            Assert.DoesNotThrow(() => c.StopSequence(), "Stopping active audio sources must not throw.");
            Assert.IsFalse(c.IsPlaying, "Stop must clear IsPlaying.");
            Assert.AreEqual(-1, c.CurrentLineIndex, "Stop must reset CurrentLineIndex.");
            Assert.IsFalse(voice.isPlaying, "The voice source must not keep playing after StopSequence.");
            Assert.IsFalse(sfx.isPlaying, "The SFX source must not keep playing after StopSequence.");

            Step(c, 2f);
            Assert.IsFalse(c.IsPlaying, "No playback may resume after a stop.");
        }

        // ---- 16. QA fix #1 regression: dynamically created components expose a live completion event ----

        [Test]
        public void DynamicallyCreatedComponentExposesLiveCompletionEvent()
        {
            // QA fix #1: the serialized UnityEvent was declared without an initializer, so it was
            // null on components created via AddComponent in EditMode (never serialized through a
            // scene/prefab); the five completion-event tests all threw NullReferenceException on
            // AddListener. This locks in the discovered scenario explicitly.
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            int completed = 0;
            var evt = GetCompletionEvent(c); // asserts the event is non-null on a dynamic component
            Assert.DoesNotThrow(() => evt.AddListener(() => completed++),
                "AddListener on a dynamically created component's completion event must not throw.");

            c.SetDialogueLines(new RadioDialogueLine[0]);
            c.PlaySequence();
            Assert.IsTrue(c.IsComplete, "An empty sequence must complete immediately.");
            Assert.AreEqual(1, completed, "The completion event must fire exactly once on natural completion.");
        }

        // ---- 17. dialogue hold (exterior flight pause) ----
        // While DialogueHeld is true, AdvanceSequence does nothing: no line starts, no
        // subtitle text is assigned/revealed, no voice/SFX (BeginPresenting — which plays the
        // audio — is the same gated path), no speaker binding changes. The hold is how scene
        // orchestration pauses ALL radio dialogue and subtitle progression during the exterior
        // establishing helicopter flight.

        [Test]
        public void DialogueHeldDefaultsFalsePreservingExistingBehavior()
        {
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            Assert.IsFalse(c.DialogueHeld, "DialogueHeld must default to false (existing behavior unchanged).");

            c.SetDialogueLines(new[] { MakeLine("S", "A", null, null, 100f, 0f, 0f) });
            c.PlaySequence();
            Step(c, 1f);
            Assert.IsTrue(c.IsComplete, "An unheld sequence must run normally to completion.");
        }

        [Test]
        public void DialogueHeldFreezesAllSubtitleAndDialogueProgression()
        {
            var dialogue = CreateTmpText("Dialogue");
            var speaker = CreateTmpText("Speaker");
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            SetPrivate(c, "speakerLabel", speaker);
            SetPrivate(c, "dialogueText", dialogue);

            // Line 0: 0.2s before + max(1.3s reveal, 1.0s voice) + 0.3s after => ~1.8s, so a
            // multi-second hold has real progression available to freeze.
            var voice = AudioClip.Create("HoldVoice", 48000, 1, 48000, false); // exactly 1.0s
            c.SetDialogueLines(new[]
            {
                MakeLine("COMMAND", "Hold position", voice, null, 10f, 0.2f, 0.3f),
                MakeLine("RAVEN", "Copy", null, null, 10f, 0f, 0.3f),
            });

            c.PlaySequence();
            Assert.IsTrue(c.IsPlaying && c.CurrentLineIndex == 0,
                "Sanity: the sequence must be started and waiting on line 0.");

            c.DialogueHeld = true;
            Step(c, 3f); // well past line 0's full ~1.8s duration

            Assert.IsTrue(c.IsPlaying, "A held sequence stays started (frozen, not stopped).");
            Assert.IsFalse(c.IsComplete, "A held sequence must not complete while held.");
            Assert.AreEqual(0, c.CurrentLineIndex, "No line progression while held.");
            Assert.AreEqual("", dialogue.text, "No subtitle text may be assigned while held.");
            Assert.AreEqual(0, dialogue.maxVisibleCharacters, "No typewriter reveal while held.");
            Assert.IsFalse(c.IsTyping, "No typewriter activity while held.");
            Assert.AreEqual("", speaker.text, "No speaker label while held.");

            // The explicit handoff: release the hold — progression resumes from where it froze.
            c.DialogueHeld = false;
            Step(c, 0.1f); // still inside line 0's 0.2s before-delay: nothing yet
            Assert.AreEqual("", dialogue.text, "Still inside the before-delay after release: no text yet.");
            Step(c, 0.2f); // t = 0.3s: line 0 presentation has begun
            Assert.AreEqual("Hold position", dialogue.text,
                "After the hold releases, line presentation must begin (text assigned once).");
            Assert.IsTrue(c.IsTyping, "The typewriter must run after release.");

            Step(c, 5f);
            Assert.IsTrue(c.IsComplete, "A held-then-released sequence must still complete normally.");
        }

        [Test]
        public void SequenceStartedWhileHeldWaitsAndResumesExactlyOnce()
        {
            // The starter scenario: the sequence start fires DURING the hold (e.g. the starter's
            // playOnStart while the exterior flight is active). The sequence must wait frozen and
            // run exactly once after the release — never skipped, never double-run.
            var c = _go.AddComponent<CinematicRadioDialogueController>();
            int completed = 0;
            GetCompletionEvent(c).AddListener(() => completed++);
            c.SetDialogueLines(new[]
            {
                MakeLine("COMMAND", "A", null, null, 100f, 0f, 0.05f),
                MakeLine("KANE", "B", null, null, 100f, 0f, 0.05f),
            });

            c.DialogueHeld = true;      // exterior flight active
            c.PlaySequence();           // the starter requests the start during the hold
            Assert.IsTrue(c.IsPlaying, "The sequence may be started while held.");
            Step(c, 2f);
            Assert.AreEqual(0, c.CurrentLineIndex, "Frozen at line 0 while held.");
            Assert.AreEqual(0, completed, "Nothing may complete while held.");

            c.DialogueHeld = false;     // the explicit handoff
            Step(c, 0.1f);
            Assert.IsTrue(c.IsPlaying || c.IsComplete, "Progression must resume after release.");
            Step(c, 2f);
            Assert.IsTrue(c.IsComplete, "The sequence must complete after the hold releases.");
            Assert.AreEqual(1, completed, "Exactly one completion: the hold must not re-run the sequence.");

            Step(c, 1f);
            Assert.AreEqual(1, completed, "The completion event must never fire again.");
        }
    }
}
