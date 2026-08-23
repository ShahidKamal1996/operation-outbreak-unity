using System.Collections.Generic;
using System.Reflection;
using OperationOutbreak.Enemies;
using OperationOutbreak.Mission;
using OperationOutbreak.Player;
using OperationOutbreak.UI;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 — the ONE director for Mission 01's story flow. Manages: opening cinematic
    /// → gameplay → radio beats → outro → result UI. Creates all story runtime components at
    /// runtime if they don't exist. Only activates when Mission 01 is the active mission.
    ///
    /// Flow: opening sequence (gameplay locked) → SequenceCompleted enables MissionSectionController
    /// (gameplay starts) → radio beats at Z thresholds → EncounterCompleted suppresses result UI +
    /// plays outro → outro SequenceCompleted releases result UI. Skip works at every stage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionStoryDirector : MonoBehaviour
    {
        private enum Phase { Idle, Opening, Gameplay, Outro, Done }

        [SerializeField] private MissionDefinition mission01;

        private StorySequenceRunner _runner;
        private StoryCameraController _storyCam;
        private HelicopterPlaceholder _heli;
        private MissionSectionController _sections;
        private MissionCompleteController _resultCtrl;
        private EnemySpawner _spawner;
        private Transform _player;

        private Phase _phase = Phase.Idle;
        private StorySequenceDefinition _openingSeq;
        private StorySequenceDefinition _outroSeq;
        private bool _beat1Fired, _beat2Fired;

        private void Awake()
        {
            _runner = FindAnyObjectByType<StorySequenceRunner>();
            if (_runner == null)
            {
                CreateCoreStoryComponents();
            }

            // 1Z.1 QA fix #6 — ALWAYS ensure presentation components exist, even if a runner
            // was already created by StoryQaHarness (which creates runner+subtitle+audio+lock
            // but NOT camera/helicopter). Without this, the camera/helicopter were never
            // instantiated when the QA harness's runner was found first, causing all CameraCue
            // and EventCue beats to be dead-lettered.
            EnsurePresentationComponents();

            _sections = FindAnyObjectByType<MissionSectionController>();
            _resultCtrl = FindAnyObjectByType<MissionCompleteController>();
            _spawner = FindAnyObjectByType<EnemySpawner>();
            var pc = FindAnyObjectByType<PlayerController>();
            _player = pc != null ? pc.transform : null;

            // Resolve M1 from active context or Resources.
            if (mission01 == null) mission01 = ActiveMissionContext.Current;
            if (mission01 == null)
                mission01 = Resources.Load<MissionDefinition>("MissionDefinitions/Mission_01");

            // Load sequences from Resources.
            _openingSeq = Resources.Load<StorySequenceDefinition>("StorySequences/Chapter01_Mission01_Opening");
            _outroSeq = Resources.Load<StorySequenceDefinition>("StorySequences/Chapter01_Mission01_Outro");
        }

        private void OnEnable()
        {
            if (_runner != null)
            {
                _runner.SequenceStarted += OnSeqStarted;
                _runner.SequenceCompleted += OnSeqCompleted;
            }
            if (_spawner != null) _spawner.EncounterCompleted += OnEncounterCompleted;

            // Start opening if this is M1 and it has a pre-mission sequence.
            if (mission01 != null && mission01.MissionId == "mission_01"
                && _openingSeq != null && _runner != null)
            {
                _phase = Phase.Opening;
                if (_sections != null) _sections.enabled = false; // delay gameplay until opening completes
                Debug.Log("[STORY M01] Opening started.");
                _runner.LoadSequence(_openingSeq);
            }
        }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _runner.SequenceStarted -= OnSeqStarted;
                _runner.SequenceCompleted -= OnSeqCompleted;
            }
            if (_spawner != null) _spawner.EncounterCompleted -= OnEncounterCompleted;
        }

        private void Update()
        {
            // Radio beats during gameplay (not during opening/outro).
            if (_phase != Phase.Gameplay || _player == null || _runner == null) return;
            if (_runner.IsRunning) return; // don't overlap

            float z = _player.position.z;

            if (!_beat1Fired && z >= 18f)
            {
                _beat1Fired = true;
                _runner.LoadSequence(BuildRadioBeat("Checkpoint signs are still up. No evac traffic.",
                    "Keep moving. We need eyes on the checkpoint."));
                Debug.Log("[STORY M01] Radio beat: early route.");
            }
            else if (!_beat2Fired && z >= 36f)
            {
                _beat2Fired = true;
                _runner.LoadSequence(BuildRadioBeat(
                    "Reyes... I've got abandoned transports. No personnel.", "Check the checkpoint."));
                Debug.Log("[STORY M01] Radio beat: approaching checkpoint.");
            }
        }

        // ---- story flow callbacks ----

        private void OnSeqStarted(StorySequenceDefinition seq)
        {
            if (seq == _outroSeq) Debug.Log("[STORY M01] Outro started.");
        }

        private void OnSeqCompleted(StorySequenceDefinition seq, bool skipped)
        {
            if (seq == _openingSeq)
            {
                _phase = Phase.Gameplay;
                if (_sections != null) _sections.enabled = true;
                Debug.Log("[STORY M01] Gameplay handoff complete.");
            }
            else if (seq == _outroSeq)
            {
                _phase = Phase.Done;
                if (_resultCtrl != null) _resultCtrl.ReleaseResultDisplay();
                Debug.Log("[STORY M01] Outro complete -> result UI.");
            }
        }

        private void OnEncounterCompleted()
        {
            if (_phase != Phase.Gameplay) return;
            if (mission01 == null || mission01.PostMissionSequence == null) return;
            if (_outroSeq == null || _runner == null) return;

            _phase = Phase.Outro;
            if (_resultCtrl != null) _resultCtrl.SuppressResultDisplay();
            Debug.Log("[STORY M01] Encounter complete -> suppressing result, starting outro.");
            _runner.LoadSequence(_outroSeq);
        }

        // ---- helpers ----

        // 1Z.1 QA fix #6 — ensures camera + helicopter exist and are subscribed BEFORE the
        // runner can execute any CameraCue/EventCue beat. Called from Awake unconditionally.
        private void EnsurePresentationComponents()
        {
            _storyCam = FindAnyObjectByType<StoryCameraController>();
            if (_storyCam == null)
            {
                _storyCam = new GameObject("[Story] CameraController").AddComponent<StoryCameraController>();
                _storyCam.transform.SetParent(transform, false);
                Debug.Log("[STORY M01] StoryCameraController created.");
            }

            _heli = FindAnyObjectByType<HelicopterPlaceholder>();
            if (_heli == null)
            {
                _heli = new GameObject("[Story] HelicopterPlaceholder").AddComponent<HelicopterPlaceholder>();
                _heli.transform.SetParent(transform, false);
                Debug.Log("[STORY M01] HelicopterPlaceholder created.");
            }
        }

        private void CreateCoreStoryComponents()
        {
            var sub = new GameObject("[Story] Subtitle");
            sub.transform.SetParent(transform, false);
            sub.AddComponent<SubtitleController>();

            var aud = new GameObject("[Story] Audio");
            aud.transform.SetParent(transform, false);
            aud.AddComponent<AudioSource>();
            aud.AddComponent<DialogueAudioController>();

            var lok = new GameObject("[Story] LockAuthority");
            lok.transform.SetParent(transform, false);
            lok.AddComponent<GameplayLockAuthority>();

            _storyCam = new GameObject("[Story] CameraController").AddComponent<StoryCameraController>();
            _storyCam.transform.SetParent(transform, false);

            _heli = new GameObject("[Story] HelicopterPlaceholder").AddComponent<HelicopterPlaceholder>();
            _heli.transform.SetParent(transform, false);

            var run = new GameObject("[Story] Runner");
            run.transform.SetParent(transform, false);
            _runner = run.AddComponent<StorySequenceRunner>();
        }

        private static StorySequenceDefinition BuildRadioBeat(string kaneText, string reyesText)
        {
            var seq = ScriptableObject.CreateInstance<StorySequenceDefinition>();
            SetField(seq, "sequenceId", "radio_beat");
            SetField(seq, "displayName", "Radio Beat");
            SetField(seq, "sequenceType", StorySequenceType.Radio);
            SetField(seq, "skippable", true);
            SetField(seq, "autoStart", true);
            SetField(seq, "beats", new List<StoryBeatDefinition>
            {
                new StoryBeatDefinition
                {
                    beatType = StoryBeatType.Dialogue, autoAdvance = true, duration = 1f,
                    dialogue = new StoryDialogueLine { speakerId = "adrian_kane", text = kaneText }
                },
                new StoryBeatDefinition
                {
                    beatType = StoryBeatType.Dialogue, autoAdvance = true, duration = 1f,
                    dialogue = new StoryDialogueLine { speakerId = "sofia_reyes", text = reyesText, isRadio = true }
                }
            });
            return seq;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null) f.SetValue(target, value);
        }
    }
}
