using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// One line of a cinematic radio dialogue sequence: who speaks, what is typed, which voice
    /// clip plays, an optional radio-open SFX, and the line's timing. All fields are
    /// Inspector-assignable; no dialogue content is hard-coded anywhere in the system.
    /// </summary>
    [Serializable]
    public sealed class RadioDialogueLine
    {
        [Tooltip("Speaker name shown in the speaker label.")]
        public string SpeakerName = "";

        [TextArea(2, 4)]
        [Tooltip("Complete subtitle text for this line (assigned to the TMP text once).")]
        public string DialogueText = "";

        [Tooltip("Voice clip played for this line. May be null (text/typewriter timing only).")]
        public AudioClip VoiceClip;

        [Tooltip("Optional radio-open SFX played at the start of this line's transmission. May be null.")]
        public AudioClip OpeningSfx;

        [Tooltip("Typewriter reveal speed in characters per second. <= 0 reveals the line instantly.")]
        public float CharactersPerSecond = 40f;

        [Tooltip("Seconds to wait before this line starts (clamped to >= 0).")]
        public float DelayBeforeLine = 0f;

        [Tooltip("Seconds to wait after this line completes, before the next line (clamped to >= 0).")]
        public float DelayAfterLine = 0.35f;
    }

    /// <summary>
    /// Binds a dialogue speaker to a character Animator talking gesture (e.g., a seated
    /// soldier's `IsTalking` bool). When a dialogue line whose SpeakerName EXACTLY matches
    /// SpeakerName begins, the Animator's talking bool is set true for the duration of that
    /// line's presentation (text + voice window) and set false as soon as the line finishes;
    /// non-matching bound animators are forced false at line start; everything resets to
    /// false on stop/restart/natural completion.
    /// </summary>
    [Serializable]
    public sealed class SpeakerAnimationBinding
    {
        [Tooltip("Must exactly match the dialogue line's SpeakerName (case-sensitive).")]
        public string SpeakerName = "";

        [Tooltip("The character Animator driving the talking gesture. May be null (skipped safely).")]
        public Animator animator;

        [Tooltip("Animator bool parameter for the talking state.")]
        public string TalkingParameter = "IsTalking";
    }

    /// <summary>
    /// Reusable cinematic radio dialogue foundation: speaker name display, typewriter subtitle
    /// reveal, voice playback, optional radio-open SFX, and deterministic line sequencing.
    ///
    /// INTENDED USE
    /// ------------
    /// Attach to any cinematic scene, assign the two TMP texts and the two dedicated
    /// AudioSources, and fill the Dialogue Lines array with the imported voice/SFX clips
    /// (Inspector assignment only — this system never loads audio by path or from Resources).
    /// Call PlaySequence() when the cinematic wants the dialogue to run.
    ///
    /// TYPEWRITER (garbage-free)
    /// -------------------------
    /// The COMPLETE line text is assigned to the TMP text exactly once per line; the reveal is
    /// driven purely by <c>maxVisibleCharacters</c> (no per-frame substrings). Punctuation,
    /// rich-text markup, and the final complete text are preserved because the string itself
    /// is never modified. At line completion maxVisibleCharacters exposes the complete text.
    /// Missing TMP references are handled gracefully (the line's text is then instantaneous).
    ///
    /// TIMING MODEL (deterministic, testable)
    /// --------------------------------------
    /// Each line is a small state machine: WAIT_BEFORE (DelayBeforeLine) -> PRESENT (typewriter
    /// + voice begin together) -> WAIT_AFTER (DelayAfterLine). The PRESENT phase completes only
    /// when BOTH the text reveal is complete AND the voice playback is complete (voice gate =
    /// the clip's own length, so sequencing is fully deterministic and testable without a
    /// running audio device; VoiceClip null -> voice gate is zero). A line with no text and no
    /// voice completes instantly; an all-empty sequence completes immediately.
    ///
    /// The single driver is the public <see cref="AdvanceSequence"/>: Update() calls it every
    /// frame in play mode (scaled or unscaled per useUnscaledTime), and EditMode tests call it
    /// directly with fixed steps. There are no coroutines, no condition polling, no per-frame
    /// substring work, and no infinite loops: each advance either consumes the frame's time or
    /// completes a bounded number of phases.
    ///
    /// STOP / RESTART
    /// --------------
    /// StopSequence() immediately stops everything (voice and SFX sources, state) and never
    /// invokes OnSequenceCompleted. RestartSequence() = Stop + Play from line 0. Repeated
    /// PlaySequence() calls stop the active run first, so sequences can never overlap.
    ///
    /// SPEAKER ANIMATION BINDINGS (optional)
    /// --------------------------------------
    /// SpeakerAnimationBinding entries map a speaker name to a character Animator bool
    /// parameter (default name: IsTalking). While a matching line's presentation (text +
    /// voice window) is active the bound talking bool is true; it is set false as soon as
    /// the line finishes, non-matching bound animators are forced false at line start, and
    /// everything resets to false on stop/restart/natural completion. Missing animators,
    /// missing parameter names, and parameters absent from the Animator are skipped safely.
    /// With an empty binding list (the default) the controller behaves exactly as before —
    /// no animation is driven.
    ///
    /// SCOPE: dialogue / voice / transmission SFX (+ optional speaker talking gestures).
    /// Helicopter ambience, scene transitions, skipping, branching, localization, and
    /// lip sync are intentionally NOT part of this foundation.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Radio Dialogue Controller")]
    public sealed class CinematicRadioDialogueController : MonoBehaviour
    {
        private enum Phase { WaitingBefore, Presenting, WaitingAfter }

        [Header("Text (TextMeshPro)")]
        [Tooltip("TMP label that displays the speaker name. May be null (skipped gracefully).")]
        [SerializeField] private TMP_Text speakerLabel;

        [Tooltip("TMP text that displays the dialogue. The typewriter animates only its maxVisibleCharacters.")]
        [SerializeField] private TMP_Text dialogueText;

        [Header("Audio (dedicated sources)")]
        [Tooltip("AudioSource used for voice playback. StopSequence() stops this source.")]
        [SerializeField] private AudioSource voiceAudioSource;

        [Tooltip("AudioSource used for transmission SFX (radio open). StopSequence() stops this source.")]
        [SerializeField] private AudioSource sfxAudioSource;

        [Header("Sequence")]
        [Tooltip("Use unscaled time for all sequence timing. Off by default (same safe default as the " +
                 "helicopter flight, QA #5D.1): with unscaled time ON, the Play-start editor stall is " +
                 "consumed in one giant first tick.")]
        [SerializeField] private bool useUnscaledTime = false;

        [Tooltip("Dialogue lines, played in order. Assign the imported voice/SFX clips here in the Inspector.")]
        [SerializeField] private RadioDialogueLine[] dialogueLines = new RadioDialogueLine[0];

        [Tooltip("Optional speaker -> character Animator talking bindings. Empty (the default) preserves " +
                 "the previous behavior exactly: no animation is driven at all.")]
        [SerializeField] private SpeakerAnimationBinding[] speakerAnimationBindings = new SpeakerAnimationBinding[0];

        [Tooltip("Invoked exactly once when the sequence completes naturally (never from StopSequence).")]
        [SerializeField] private UnityEvent onSequenceCompleted = new UnityEvent();

        // ---- runtime state (never serialized) ----
        private bool _playing;
        private bool _typing;
        private bool _complete;
        private int _lineIndex = -1;
        private Phase _phase;
        private float _phaseTimeRemaining;
        private float _voiceRemaining;
        private float _revealProgress;
        private int _textLength;

        /// <summary>True while a sequence is running.</summary>
        public bool IsPlaying => _playing;

        /// <summary>True while a line's typewriter reveal is in progress.</summary>
        public bool IsTyping => _typing;

        /// <summary>True after the sequence completes naturally.</summary>
        public bool IsComplete => _complete;

        /// <summary>Index of the active line, or -1 when no line is active (before start / after stop or completion).</summary>
        public int CurrentLineIndex => _lineIndex;

        /// <summary>Current time-mode setting (serialized default is false = scaled time).</summary>
        public bool UseUnscaledTime => useUnscaledTime;

        private void Update()
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            AdvanceSequence(dt);
        }

        /// <summary>Replaces the dialogue lines (kept as given). Call before PlaySequence to reconfigure.</summary>
        public void SetDialogueLines(RadioDialogueLine[] lines)
        {
            dialogueLines = lines ?? new RadioDialogueLine[0];
        }

        /// <summary>
        /// Replaces the speaker -> Animator talking bindings (kept as given). Null clears them
        /// (previous behavior: no animation driven). Call before PlaySequence to reconfigure.
        /// </summary>
        public void SetSpeakerAnimationBindings(SpeakerAnimationBinding[] bindings)
        {
            speakerAnimationBindings = bindings ?? new SpeakerAnimationBinding[0];
        }

        /// <summary>
        /// (Re)starts the sequence from line 0. If a sequence is already active it is stopped
        /// first, so repeated calls never create overlapping sequences or audio. An empty
        /// (or null) line set completes immediately and invokes OnSequenceCompleted once.
        /// </summary>
        public void PlaySequence()
        {
            StopSequenceInternal();
            _playing = true;
            _complete = false;

            if (dialogueLines == null || dialogueLines.Length == 0)
            {
                CompleteSequence();
                return;
            }

            SelectLine(0);
        }

        /// <summary>
        /// Stops immediately: no playback continues, the voice and SFX sources are stopped, and
        /// all sequence state resets (CurrentLineIndex = -1, IsPlaying/IsTyping/IsComplete = false).
        /// OnSequenceCompleted is NOT invoked (natural completion only). The subtitle text is
        /// left as-is — clearing it is a presentation decision for the scene.
        /// </summary>
        public void StopSequence()
        {
            StopSequenceInternal();
        }

        /// <summary>Safely stops any active playback, resets sequence state, and starts again from line 0.</summary>
        public void RestartSequence()
        {
            StopSequence();
            PlaySequence();
        }

        /// <summary>
        /// Advances the sequence by <paramref name="deltaTime"/> seconds — the single
        /// deterministic driver (Update in play mode, direct calls from EditMode tests).
        /// Non-positive steps are ignored safely.
        /// </summary>
        public void AdvanceSequence(float deltaTime)
        {
            if (!_playing) return;
            float t = deltaTime > 0f ? deltaTime : 0f;

            // Bounded: a single advance can complete at most 3 phases per line (before/present/after).
            int maxSteps = (dialogueLines != null ? dialogueLines.Length : 0) * 3 + 4;
            int guard = 0;
            while (_playing && t > 0f && guard++ < maxSteps)
            {
                switch (_phase)
                {
                    case Phase.WaitingBefore:
                        if (t < _phaseTimeRemaining) { _phaseTimeRemaining -= t; t = 0f; break; }
                        t -= _phaseTimeRemaining;
                        _phaseTimeRemaining = 0f;
                        BeginPresenting();
                        break;

                    case Phase.Presenting:
                        {
                            float need = PresentationNeed();
                            if (t >= need)
                            {
                                // Completes within this frame's budget: finish it and carry
                                // the EXACT remaining time into the after-delay.
                                CompletePresenting();
                                _typing = false;
                                EndSpeakerBindingForLine(LineAt(_lineIndex)); // this line finished -> its speaker goes idle
                                t -= need;
                                _phase = Phase.WaitingAfter;
                                _phaseTimeRemaining = Mathf.Max(0f, LineAt(_lineIndex) != null ? LineAt(_lineIndex).DelayAfterLine : 0f);
                            }
                            else
                            {
                                AdvancePresenting(t); // partial progress, the frame's time is consumed
                                t = 0f;
                            }
                        }
                        break;

                    case Phase.WaitingAfter:
                        if (t < _phaseTimeRemaining) { _phaseTimeRemaining -= t; t = 0f; break; }
                        t -= _phaseTimeRemaining;
                        _phaseTimeRemaining = 0f;
                        if (_lineIndex + 1 < (dialogueLines != null ? dialogueLines.Length : 0)) SelectLine(_lineIndex + 1);
                        else CompleteSequence();
                        break;
                }
            }
        }

        // ===================================================================== line lifecycle

        private void SelectLine(int index)
        {
            _lineIndex = index;
            _phase = Phase.WaitingBefore;
            _phaseTimeRemaining = Mathf.Max(0f, LineAt(index) != null ? LineAt(index).DelayBeforeLine : 0f);
            _typing = false;
            _revealProgress = 0f;
            _textLength = 0;
            _voiceRemaining = 0f;
        }

        private void BeginPresenting()
        {
            RadioDialogueLine line = LineAt(_lineIndex);
            string text = line != null && line.DialogueText != null ? line.DialogueText : "";

            if (speakerLabel != null)
                speakerLabel.text = line != null ? line.SpeakerName : "";

            bool canType = dialogueText != null
                           && text.Length > 0
                           && line != null
                           && line.CharactersPerSecond > 0f;

            if (dialogueText != null)
            {
                // Assign the COMPLETE text exactly once, then reveal via maxVisibleCharacters
                // only (no per-frame substrings; rich text and final text are preserved).
                dialogueText.text = text;
                dialogueText.ForceMeshUpdate();
                dialogueText.maxVisibleCharacters = 0;
            }

            // SFX first, then voice — both as part of the same line presentation.
            if (line != null && line.OpeningSfx != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(line.OpeningSfx);
            if (line != null && line.VoiceClip != null && voiceAudioSource != null)
                voiceAudioSource.PlayOneShot(line.VoiceClip);

            // Deterministic voice gate: the clip's own length. The controller never cuts the
            // voice off when typing finishes — the line completes when BOTH are done.
            _voiceRemaining = line != null && line.VoiceClip != null ? line.VoiceClip.length : 0f;
            _textLength = canType ? text.Length : 0;
            _revealProgress = 0f;
            _typing = canType;
            _phase = Phase.Presenting;

            // Line presentation is starting: the matching speaker's talking gesture goes on,
            // every other bound speaker's goes off.
            StartSpeakerBindingForLine(line);
        }

        private void AdvancePresenting(float t)
        {
            if (_typing)
            {
                _revealProgress = Mathf.Min(_textLength, _revealProgress + LineCps() * t);
                int visible = Mathf.Min(_textLength, Mathf.FloorToInt(_revealProgress + 1e-6f));
                if (dialogueText != null && visible != dialogueText.maxVisibleCharacters)
                    dialogueText.maxVisibleCharacters = visible;
            }

            if (_voiceRemaining > 0f)
                _voiceRemaining = Mathf.Max(0f, _voiceRemaining - t);
        }

        /// <summary>Seconds still needed (from now) for BOTH the text reveal and the voice to finish.</summary>
        private float PresentationNeed()
        {
            float revealNeed = 0f;
            if (_typing)
            {
                float cps = LineCps();
                if (cps > 0f) revealNeed = (_textLength - _revealProgress) / cps;
            }
            return Mathf.Max(revealNeed, _voiceRemaining);
        }

        /// <summary>Brings the presentation to completion (full reveal, voice gate closed).</summary>
        private void CompletePresenting()
        {
            _revealProgress = _textLength;
            _voiceRemaining = 0f;
            FinalizeTypewriter();
        }

        private void FinalizeTypewriter()
        {
            // At line completion the complete text must be exposed.
            if (dialogueText != null)
                dialogueText.maxVisibleCharacters = _textLength;
        }

        private void StopSequenceInternal()
        {
            _playing = false;
            _typing = false;
            _complete = false;
            _lineIndex = -1;
            _phase = Phase.WaitingBefore;
            _phaseTimeRemaining = 0f;
            _voiceRemaining = 0f;
            _revealProgress = 0f;
            _textLength = 0;

            if (voiceAudioSource != null) voiceAudioSource.Stop();
            if (sfxAudioSource != null) sfxAudioSource.Stop();
            ResetSpeakerBindings();
        }

        private void CompleteSequence()
        {
            _playing = false;
            _typing = false;
            _complete = true;
            _lineIndex = -1;
            ResetSpeakerBindings();
            if (onSequenceCompleted != null) onSequenceCompleted.Invoke();
        }

        // ===================================================================== speaker animation bindings

        /// <summary>
        /// Line presentation starting: the matching bound speaker's talking bool goes true and
        /// every non-matching bound speaker's goes false (so the previous line's speaker is
        /// always cleared, even mid-voice). No-op when no bindings are configured.
        /// </summary>
        private void StartSpeakerBindingForLine(RadioDialogueLine line)
        {
            if (speakerAnimationBindings == null || speakerAnimationBindings.Length == 0) return;
            string speaker = line != null ? line.SpeakerName : "";
            foreach (var binding in speakerAnimationBindings)
            {
                if (binding == null || binding.animator == null) continue;
                bool isMatch = !string.IsNullOrEmpty(binding.SpeakerName)
                               && string.Equals(speaker, binding.SpeakerName, StringComparison.Ordinal);
                SetBindingBool(binding, isMatch);
            }
        }

        /// <summary>
        /// Line presentation finished: the line's own bound speaker (if any) goes idle.
        /// </summary>
        private void EndSpeakerBindingForLine(RadioDialogueLine line)
        {
            if (speakerAnimationBindings == null || line == null) return;
            string speaker = line.SpeakerName;
            foreach (var binding in speakerAnimationBindings)
            {
                if (binding == null || binding.animator == null) continue;
                if (string.Equals(speaker, binding.SpeakerName, StringComparison.Ordinal))
                    SetBindingBool(binding, false);
            }
        }

        /// <summary>Sets every bound talking bool to false (stop/restart/natural completion).</summary>
        private void ResetSpeakerBindings()
        {
            if (speakerAnimationBindings == null) return;
            foreach (var binding in speakerAnimationBindings)
            {
                if (binding == null || binding.animator == null) continue;
                SetBindingBool(binding, false);
            }
        }

        /// <summary>
        /// Null- and error-safe parameter write: missing animators, missing/empty parameter
        /// names, and parameters that do not exist on the Animator are all skipped silently.
        /// </summary>
        private static void SetBindingBool(SpeakerAnimationBinding binding, bool talking)
        {
            var animator = binding.animator;
            string parameter = binding.TalkingParameter;
            if (animator == null || string.IsNullOrEmpty(parameter)) return;
            if (!animator.HasBool(parameter)) return;
            animator.SetBool(parameter, talking);
        }

        // ===================================================================== helpers

        private RadioDialogueLine LineAt(int index)
        {
            return dialogueLines != null && index >= 0 && index < dialogueLines.Length
                ? dialogueLines[index]
                : null;
        }

        private float LineCps()
        {
            RadioDialogueLine line = LineAt(_lineIndex);
            return line != null && line.CharactersPerSecond > 0f ? line.CharactersPerSecond : 0f;
        }
    }
}
