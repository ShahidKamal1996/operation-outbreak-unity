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
    /// Milestone 1Z.1 QA fix #7 — revised director for M1's helicopter interior opening.
    /// Creates interior rig + cinematic Kane on m01_interior_setup. Manages HUD visibility.
    /// Interior is at y=-300 (far below gameplay). Transition to exterior via cues.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionStoryDirector : MonoBehaviour
    {
        private enum Phase { Idle, Opening, Gameplay, Outro, Done }

        [SerializeField] private MissionDefinition mission01;

        private StorySequenceRunner _runner;
        private StoryCameraController _storyCam;
        private HelicopterPlaceholder _heli;
        private HelicopterInteriorRig _interiorRig;
        private StoryHudVisibilityController _hudCtrl;
        private MissionSectionController _sections;
        private MissionCompleteController _resultCtrl;
        private EnemySpawner _spawner;
        private Transform _player;

        private Phase _phase = Phase.Idle;
        private StorySequenceDefinition _openingSeq;
        private StorySequenceDefinition _outroSeq;
        private bool _beat1Fired, _beat2Fired;

        // Interior rig world position — far from gameplay lane, invisible from gameplay camera.
        private static readonly Vector3 InteriorWorldPos = new Vector3(0f, -300f, 0f);

        private void Awake()
        {
            _runner = FindAnyObjectByType<StorySequenceRunner>();
            if (_runner == null)
                CreateCoreStoryComponents();

            EnsurePresentationComponents();

            _sections = FindAnyObjectByType<MissionSectionController>();
            _resultCtrl = FindAnyObjectByType<MissionCompleteController>();
            _spawner = FindAnyObjectByType<EnemySpawner>();
            var pc = FindAnyObjectByType<PlayerController>();
            _player = pc != null ? pc.transform : null;

            if (mission01 == null) mission01 = ActiveMissionContext.Current;
            if (mission01 == null)
                mission01 = Resources.Load<MissionDefinition>("MissionDefinitions/Mission_01");

            _openingSeq = Resources.Load<StorySequenceDefinition>("StorySequences/Chapter01_Mission01_Opening");
            _outroSeq = Resources.Load<StorySequenceDefinition>("StorySequences/Chapter01_Mission01_Outro");

            // HUD visibility controller
            _hudCtrl = new GameObject("[Story] HudVisibility").AddComponent<StoryHudVisibilityController>();
            _hudCtrl.transform.SetParent(transform, false);

            // Subscribe to cue events for interior/exterior management
            StoryCueEvents.EventCue += OnEventCue;
        }

        private void OnEnable()
        {
            if (_runner != null)
            {
                _runner.SequenceStarted += OnSeqStarted;
                _runner.SequenceCompleted += OnSeqCompleted;
            }
            if (_spawner != null) _spawner.EncounterCompleted += OnEncounterCompleted;

            if (mission01 != null && mission01.MissionId == "mission_01"
                && _openingSeq != null && _runner != null)
            {
                _phase = Phase.Opening;
                if (_sections != null) _sections.enabled = false;
                _hudCtrl.HideGameplayHud();
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
            StoryCueEvents.EventCue -= OnEventCue;
        }

        private void OnDestroy()
        {
            StoryCueEvents.EventCue -= OnEventCue;
        }

        private void Update()
        {
            if (_phase != Phase.Gameplay || _player == null || _runner == null) return;
            if (_runner.IsRunning) return;

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

        // ---- Cue event handler for interior/exterior management ----

        private void OnEventCue(string cueId)
        {
            switch (cueId)
            {
                case "m01_interior_setup":
                    if (_interiorRig == null)
                    {
                        _interiorRig = new GameObject("[Story] M01 Interior").AddComponent<HelicopterInteriorRig>();
                        _interiorRig.transform.SetParent(transform, false);
                    }
                    _interiorRig.Setup(InteriorWorldPos);
                    if (_storyCam != null) _storyCam.SetInteriorRig(_interiorRig);
                    // Hide the real player during cinematic
                    if (_player != null) _player.gameObject.SetActive(false);
                    break;

                case "m01_interior_teardown":
                    if (_interiorRig != null) _interiorRig.Teardown();
                    break;

                case "m01_player_grounded":
                    // Show the real player at insertion point
                    if (_player != null)
                    {
                        _player.gameObject.SetActive(true);
                        _player.position = new Vector3(0f, 1f, 0f);
                    }
                    if (_interiorRig != null) _interiorRig.Teardown();
                    Debug.Log("[STORY M01] Kane insertion complete.");
                    break;

                case "helicopter_approach":
                    Debug.Log("[STORY M01] Transitioning to exterior approach.");
                    break;
            }
        }

        // ---- Story flow callbacks ----

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
                if (_hudCtrl != null) _hudCtrl.RestoreGameplayHud();
                if (_player != null && !_player.gameObject.activeSelf)
                    _player.gameObject.SetActive(true);
                if (_interiorRig != null) _interiorRig.Teardown();
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

        private void EnsurePresentationComponents()
        {
            _storyCam = FindAnyObjectByType<StoryCameraController>();
            if (_storyCam == null)
            {
                _storyCam = new GameObject("[Story] CameraController").AddComponent<StoryCameraController>();
                _storyCam.transform.SetParent(transform, false);
            }

            _heli = FindAnyObjectByType<HelicopterPlaceholder>();
            if (_heli == null)
            {
                _heli = new GameObject("[Story] HelicopterPlaceholder").AddComponent<HelicopterPlaceholder>();
                _heli.transform.SetParent(transform, false);
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
