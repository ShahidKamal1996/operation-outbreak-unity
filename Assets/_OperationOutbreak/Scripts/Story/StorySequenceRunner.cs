using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z — the ONE runtime story-sequence authority. Loads a StorySequenceDefinition,
    /// executes its ordered beats, presents subtitles, plays optional voice clips, publishes cue
    /// events, locks/unlocks gameplay, handles skip, and completes exactly once.
    ///
    /// No per-mission sequence controller. No Mission01CutsceneController. One runner, data-driven.
    /// Radio sequences do not lock gameplay (the subtitle/audio layer still shows). Pre/post-mission
    /// sequences may lock gameplay via GameplayLock beats.
    ///
    /// State safety: all state is instance-only. OnDisable/OnDestroy skip + force-unlock + clear
    /// subtitles/audio so no stale lock, subtitle, or voice survives a scene reload / Retry /
    /// Game Over / interruption.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StorySequenceRunner : MonoBehaviour
    {
        [SerializeField] private SubtitleController subtitleController;
        [SerializeField] private DialogueAudioController audioController;
        [SerializeField] private GameplayLockAuthority lockAuthority;

        private StorySequenceDefinition _sequence;
        private int _beatIndex;
        private float _beatTimer;
        private bool _running;
        private bool _completed;
        private bool _skipped;

        /// <summary>True while a sequence is actively running (not yet completed/skipped).</summary>
        public bool IsRunning => _running && !_completed;

        /// <summary>The sequence currently or most recently loaded. Null if none.</summary>
        public StorySequenceDefinition CurrentSequence => _sequence;

        /// <summary>Raised when a sequence starts. Carries the sequence definition.</summary>
        public event Action<StorySequenceDefinition> SequenceStarted;

        /// <summary>Raised when the active beat changes. Carries the zero-based beat index.</summary>
        public event Action<int> BeatChanged;

        /// <summary>Raised when a sequence completes (normally OR via skip). Exactly once.</summary>
        public event Action<StorySequenceDefinition, bool> SequenceCompleted;

        private void Awake()
        {
            if (subtitleController == null) subtitleController = FindAnyObjectByType<SubtitleController>();
            if (audioController == null) audioController = FindAnyObjectByType<DialogueAudioController>();
            if (lockAuthority == null) lockAuthority = FindAnyObjectByType<GameplayLockAuthority>();
        }

        private void OnDisable()
        {
            // Scene reload / destroy safety: clean everything up.
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        /// <summary>Loads and optionally auto-starts a sequence. Returns false if already running.</summary>
        public bool LoadSequence(StorySequenceDefinition sequence)
        {
            if (sequence == null || _running) return false;

            _sequence = sequence;
            _beatIndex = 0;
            _beatTimer = 0f;
            _completed = false;
            _skipped = false;

            if (sequence.AutoStart)
            {
                StartSequence();
            }

            return true;
        }

        /// <summary>Begins executing the loaded sequence's beats. Raises SequenceStarted.</summary>
        public void StartSequence()
        {
            if (_sequence == null || _running) return;

            _running = true;
            SequenceStarted?.Invoke(_sequence);
            ExecuteBeat(0);
        }

        private void Update()
        {
            if (!_running || _completed) return;

            // Skip input (desktop QA: Space or Escape). Uses the Input System (activeInputHandler=1).
            if (_sequence != null && _sequence.Skippable)
            {
                if (Keyboard.current != null
                    && (Keyboard.current.spaceKey.wasPressedThisFrame
                        || Keyboard.current.escapeKey.wasPressedThisFrame))
                {
                    Skip();
                    return;
                }
            }

            // Advance timed beats.
            if (_beatTimer > 0f)
            {
                _beatTimer -= Time.deltaTime;
                if (_beatTimer <= 0f)
                {
                    _beatTimer = 0f;
                    Advance();
                }
            }
        }

        /// <summary>
        /// Skips the current sequence. Stops audio, hides subtitles, fires required-on-skip cues,
        /// releases locks, and fires SequenceCompleted exactly once.
        /// </summary>
        public void Skip()
        {
            if (!_running || _completed) return;

            _skipped = true;

            // Fire any required-on-skip cues that have NOT been executed yet.
            if (_sequence != null && _sequence.Beats != null)
            {
                for (int i = _beatIndex; i < _sequence.Beats.Count; i++)
                {
                    StoryBeatDefinition beat = _sequence.Beats[i];
                    if (beat == null) continue;

                    if ((beat.beatType == StoryBeatType.CameraCue || beat.beatType == StoryBeatType.EventCue)
                        && beat.cuePolicy == StoryCuePolicy.RequiredOnSkip
                        && !string.IsNullOrEmpty(beat.cueId))
                    {
                        PublishCue(beat);
                    }
                }
            }

            CompleteSequence();
        }

        private void ExecuteBeat(int index)
        {
            if (_sequence == null || _sequence.Beats == null || index < 0 || index >= _sequence.Beats.Count)
            {
                CompleteSequence();
                return;
            }

            _beatIndex = index;
            StoryBeatDefinition beat = _sequence.Beats[index];
            BeatChanged?.Invoke(index);

            if (beat == null) { Advance(); return; }

            switch (beat.beatType)
            {
                case StoryBeatType.Dialogue:
                    ExecuteDialogue(beat);
                    break;
                case StoryBeatType.Wait:
                    _beatTimer = Mathf.Max(0.01f, beat.duration);
                    break;
                case StoryBeatType.GameplayLock:
                    if (lockAuthority != null) lockAuthority.Lock();
                    Advance();
                    break;
                case StoryBeatType.GameplayUnlock:
                    if (lockAuthority != null) lockAuthority.Unlock();
                    Advance();
                    break;
                case StoryBeatType.CameraCue:
                case StoryBeatType.EventCue:
                    PublishCue(beat);
                    Advance();
                    break;
                default:
                    Advance();
                    break;
            }
        }

        private void ExecuteDialogue(StoryBeatDefinition beat)
        {
            StoryDialogueLine line = beat.dialogue;
            string speakerName = ResolveSpeakerName(line);

            if (subtitleController != null)
            {
                subtitleController.ShowDialogue(speakerName, line.text, line.isRadio);
            }

            if (audioController != null)
            {
                audioController.PlayLine(line.voiceClip);
            }

            float duration = ResolveDuration(beat, line);

            if (beat.autoAdvance)
            {
                _beatTimer = duration;
            }
            // If not auto-advance, the player must call AdvanceDialogue() (future tap/space).
            // For now, auto-advance is the only path; non-auto-advance defaults to duration anyway.
            else
            {
                _beatTimer = duration;
            }
        }

        private float ResolveDuration(StoryBeatDefinition beat, StoryDialogueLine line)
        {
            if (line.durationOverride > 0f) return line.durationOverride;
            if (audioController != null && line.voiceClip != null) return line.voiceClip.length;
            // Default reading time: ~14 chars/sec, minimum 2 seconds, plus authored wait-after.
            float estimated = line.text != null ? Mathf.Max(2f, line.text.Length / 14f) : 2f;
            return estimated + beat.duration; // beat.duration serves as wait-after for dialogue
        }

        private string ResolveSpeakerName(StoryDialogueLine line)
        {
            if (line == null || string.IsNullOrEmpty(line.speakerId)) return string.Empty;
            // SubtitleController can resolve from a speaker registry if available.
            // For now return the speaker id as-is; the subtitle controller can map it.
            return line.speakerId.ToUpperInvariant();
        }

        private void PublishCue(StoryBeatDefinition beat)
        {
            if (beat.beatType == StoryBeatType.CameraCue)
                StoryCueEvents.RaiseCameraCue(beat.cueId);
            else
                StoryCueEvents.RaiseEventCue(beat.cueId);
        }

        /// <summary>Manually advance to the next beat (future tap-to-advance).</summary>
        public void AdvanceDialogue()
        {
            if (!_running || _completed) return;
            Advance();
        }

        private void Advance()
        {
            if (_sequence == null || _beatIndex + 1 >= _sequence.Beats.Count)
            {
                CompleteSequence();
                return;
            }

            // Clear subtitle when moving past a dialogue beat.
            if (subtitleController != null) subtitleController.Hide();

            ExecuteBeat(_beatIndex + 1);
        }

        private void CompleteSequence()
        {
            if (_completed) return;

            _completed = true;
            _running = false;

            // Clean up: stop audio, hide subtitle, release any lingering lock.
            if (audioController != null) audioController.Stop();
            if (subtitleController != null) subtitleController.Hide();
            if (lockAuthority != null) lockAuthority.ForceUnlock();

            SequenceCompleted?.Invoke(_sequence, _skipped);
        }

        private void Cleanup()
        {
            if (!_completed)
            {
                _completed = true;
                _running = false;
            }

            if (audioController != null) audioController.Stop();
            if (subtitleController != null) subtitleController.Hide();
            if (lockAuthority != null) lockAuthority.ForceUnlock();
        }
    }
}
