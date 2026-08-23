using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z QA harness — DEV/PLAY-MODE ONLY. Lets a developer trigger three controlled
    /// story-sequence tests from Gameplay_Prototype via F7/F8/F9 to verify the cinematic/dialogue
    /// foundation works in real Play Mode. Creates its own runner/subtitle/audio/lock components
    /// so it needs zero scene wiring beyond this component itself.
    ///
    /// F7 = Full cinematic test (lock → dialogue → wait → dialogue → unlock → complete)
    /// F8 = Radio test (no lock, gameplay active)
    /// F9 = Full cinematic test (same as F7, intended for skip testing — press Space/Escape)
    ///
    /// Test sequences are built IN MEMORY (no asset files); they use canonical speakers
    /// (sofia_reyes, raven_ortitz) with obvious temporary text. They are NOT assigned to any
    /// MissionDefinition and do NOT affect production story/mission/reward/progression.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StoryQaHarness : MonoBehaviour
    {
        private StorySequenceRunner _runner;
        private bool _setup;

        private void Awake() => Setup();

        private void OnEnable()
        {
            if (_runner != null)
            {
                _runner.SequenceStarted += OnStarted;
                _runner.SequenceCompleted += OnCompleted;
            }
        }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _runner.SequenceStarted -= OnStarted;
                _runner.SequenceCompleted -= OnCompleted;
            }
        }

        private void Setup()
        {
            if (_setup) return;
            _setup = true;

            // Create the runtime story components if they don't already exist in the scene.
            _runner = FindAnyObjectByType<StorySequenceRunner>();
            if (_runner == null)
            {
                // Subtitle + audio (DialogueAudioController needs an AudioSource).
                GameObject subtitleGo = new GameObject("[QA] SubtitleController");
                subtitleGo.transform.SetParent(transform, false);
                subtitleGo.AddComponent<SubtitleController>();

                GameObject audioGo = new GameObject("[QA] DialogueAudioController");
                audioGo.transform.SetParent(transform, false);
                audioGo.AddComponent<AudioSource>();
                audioGo.AddComponent<DialogueAudioController>();

                GameObject lockGo = new GameObject("[QA] GameplayLockAuthority");
                lockGo.transform.SetParent(transform, false);
                lockGo.AddComponent<GameplayLockAuthority>();

                // Runner resolves subtitle/audio/lock via FindAnyObjectByType in its Awake.
                GameObject runnerGo = new GameObject("[QA] StorySequenceRunner");
                runnerGo.transform.SetParent(transform, false);
                _runner = runnerGo.AddComponent<StorySequenceRunner>();
            }

            if (_runner != null)
            {
                _runner.SequenceStarted += OnStarted;
                _runner.SequenceCompleted += OnCompleted;
            }
        }

        private void Update()
        {
            if (_runner == null || _runner.IsRunning) return;

            if (Keyboard.current == null) return;

            if (Keyboard.current.f7Key.wasPressedThisFrame)
            {
                Debug.Log("[1Z QA] Cinematic test started (F7)");
                _runner.LoadSequence(BuildCinematicSequence());
            }
            else if (Keyboard.current.f8Key.wasPressedThisFrame)
            {
                Debug.Log("[1Z QA] Radio test started (F8)");
                _runner.LoadSequence(BuildRadioSequence());
            }
            else if (Keyboard.current.f9Key.wasPressedThisFrame)
            {
                Debug.Log("[1Z QA] Cinematic test started for skip (F9)");
                _runner.LoadSequence(BuildCinematicSequence());
            }
        }

        private void OnStarted(StorySequenceDefinition seq)
        {
            // Per-sequence log only (not per-beat) to avoid spam.
        }

        private void OnCompleted(StorySequenceDefinition seq, bool skipped)
        {
            if (skipped)
                Debug.Log("[1Z QA] Cinematic skipped — no stale lock should remain.");
            else
                Debug.Log("[1Z QA] Cinematic/radio completed.");
        }

        // ---- in-memory test sequences (no asset files) ----

        private static StorySequenceDefinition BuildCinematicSequence()
        {
            StorySequenceDefinition seq = ScriptableObject.CreateInstance<StorySequenceDefinition>();
            SetField(seq, "sequenceId", "qa_cinematic");
            SetField(seq, "displayName", "QA Cinematic Test");
            SetField(seq, "sequenceType", StorySequenceType.PreMission);
            SetField(seq, "skippable", true);
            SetField(seq, "autoStart", true);

            var beats = new List<StoryBeatDefinition>
            {
                new StoryBeatDefinition { beatType = StoryBeatType.GameplayLock },
                new StoryBeatDefinition
                {
                    beatType = StoryBeatType.Dialogue,
                    autoAdvance = true,
                    duration = 1f, // wait-after
                    dialogue = new StoryDialogueLine
                    {
                        speakerId = "sofia_reyes",
                        text = "QA CINEMATIC TEST — MOVEMENT SHOULD BE LOCKED."
                    }
                },
                new StoryBeatDefinition { beatType = StoryBeatType.Wait, duration = 2f },
                new StoryBeatDefinition
                {
                    beatType = StoryBeatType.Dialogue,
                    autoAdvance = true,
                    duration = 1f,
                    dialogue = new StoryDialogueLine
                    {
                        speakerId = "raven_ortiz",
                        text = "QA CINEMATIC TEST — CONTROL WILL RETURN NOW."
                    }
                },
                new StoryBeatDefinition { beatType = StoryBeatType.GameplayUnlock },
            };

            SetField(seq, "beats", beats);
            return seq;
        }

        private static StorySequenceDefinition BuildRadioSequence()
        {
            StorySequenceDefinition seq = ScriptableObject.CreateInstance<StorySequenceDefinition>();
            SetField(seq, "sequenceId", "qa_radio");
            SetField(seq, "displayName", "QA Radio Test");
            SetField(seq, "sequenceType", StorySequenceType.Radio);
            SetField(seq, "skippable", true);
            SetField(seq, "autoStart", true);

            var beats = new List<StoryBeatDefinition>
            {
                new StoryBeatDefinition
                {
                    beatType = StoryBeatType.Dialogue,
                    autoAdvance = true,
                    duration = 1f,
                    dialogue = new StoryDialogueLine
                    {
                        speakerId = "raven_ortiz",
                        text = "QA RADIO TEST — GAMEPLAY MUST REMAIN ACTIVE.",
                        isRadio = true
                    }
                }
            };

            SetField(seq, "beats", beats);
            return seq;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null) f.SetValue(target, value);
        }
    }
}
