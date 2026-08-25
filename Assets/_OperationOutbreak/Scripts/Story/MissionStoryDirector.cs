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
    /// Milestone 1Z.1 QA fix #8 / #10 — director for Mission 01's helicopter interior opening.
    ///
    /// QA fix #10: this director is now AUTHORITATIVE over whether the opening may start. Rather
    /// than waiting for another component to push a flag onto it (which was order-dependent), it
    /// asks OpeningStoryStartPermission — including a scan of serialized scene intent — every time
    /// it considers starting. See <see cref="IsOpeningStartAllowed"/>.
    ///
    /// Owns the interior rig lifecycle, the cinematic fade (so the camera never visibly travels
    /// between the gameplay world and the y=-300 interior), HUD visibility, and the real/cinematic
    /// Kane swap. The interior uses a VISUAL-ONLY clone of the production Toon Soldier (same model,
    /// material, avatar, controller) so the player recognises "that is my Kane"; the authoritative
    /// gameplay Player is simply hidden during the briefing and re-shown at insertion.
    ///
    /// Cue flow (see Chapter01_Mission01_Opening.asset):
    ///   m01_interior_setup (black + build + clone Kane + hide player)
    ///   -> m01_interior_kane (camera snap + fade IN, revealing seated Kane)
    ///   -> interior dialogue + reframes
    ///   -> m01_interior_fade_out -> Wait -> m01_interior_teardown -> m01_exterior_approach (snap)
    ///   -> helicopter_approach -> m01_fade_in (reveal exterior) -> helicopter_insert
    ///   -> m01_insertion -> m01_player_grounded (real Kane on ground) -> gameplay_handoff
    ///   -> helicopter_depart -> GameplayUnlock
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
        private StoryFadeController _fade;
        private StoryHudVisibilityController _hudCtrl;
        private MissionSectionController _sections;
        private MissionCompleteController _resultCtrl;
        private EnemySpawner _spawner;
        private Transform _player;

        private Phase _phase = Phase.Idle;
        private StorySequenceDefinition _openingSeq;
        private StorySequenceDefinition _outroSeq;
        private bool _beat1Fired, _beat2Fired;

        /// <summary>
        /// Legacy QA fix #8 local gate. RETAINED for compatibility, but it is no longer the
        /// authority — it is now only one of three independent reasons to defer. See
        /// <see cref="IsOpeningStartAllowed"/> for the full decision.
        /// </summary>
        public bool HoldOpeningSequence { get; set; }

        /// <summary>
        /// QA fix #10 — THE authoritative answer to "may the Mission 01 opening start now?".
        ///
        /// The opening starts only when ALL THREE agree:
        ///   1. no local legacy hold,
        ///   2. the process-wide permission is allowed (a holder has registered a token), and
        ///   3. no active scene component declares it owns Mission 01 startup.
        ///
        /// Check 3 is what removes the initialization-order race. Checks 1 and 2 can only be true
        /// once some other component's Awake has already run; check 3 reads serialized intent
        /// directly off the scene, and Unity deserializes [SerializeField] state before ANY Awake
        /// executes. So this is correct even when MissionStoryDirector.OnEnable runs first.
        /// </summary>
        public bool IsOpeningStartAllowed =>
            !HoldOpeningSequence
            && OpeningStoryStartPermission.IsAllowed
            && !OpeningStoryStartPermission.AnyActiveHoldSourceRequestsHold();

        /// <summary>
        /// Releases the local gate and attempts to start the opening (public API for the 1Z.1C
        /// handoff). This clears only the director's OWN hold; if an exterior cinematic still
        /// claims startup ownership the opening correctly stays deferred. Use
        /// <see cref="ForceReleaseAllOpeningHolds"/> for a full handoff.
        /// </summary>
        public void ReleaseOpeningSequence()
        {
            HoldOpeningSequence = false;
            TryStartOpening();
        }

        /// <summary>
        /// Full 1Z.1C handoff: clears the local gate, tells every active hold source to
        /// relinquish its claim, then starts the opening. NOTHING calls this automatically in
        /// QA fix #10 — the exterior cinematic deliberately keeps the story held.
        /// </summary>
        public void ForceReleaseAllOpeningHolds()
        {
            HoldOpeningSequence = false;
            OpeningStoryStartPermission.ReleaseSceneHoldSources();
            TryStartOpening();
        }

        // Interior rig world position — far from gameplay lane, invisible from gameplay camera.
        private static readonly Vector3 InteriorWorldPos = new Vector3(0f, -300f, 0f);

        // Fade durations (short, cinematic).
        private const float EntryFadeIn = 0.9f;
        private const float ExitFade = 0.6f;

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

            // HUD visibility controller.
            _hudCtrl = new GameObject("[Story] HudVisibility").AddComponent<StoryHudVisibilityController>();
            _hudCtrl.transform.SetParent(transform, false);

            StoryCueEvents.EventCue += OnEventCue;
            StoryCueEvents.CameraCue += OnCameraCue;
        }

        private void OnEnable()
        {
            if (_runner != null)
            {
                _runner.SequenceStarted += OnSeqStarted;
                _runner.SequenceCompleted += OnSeqCompleted;
            }
            if (_spawner != null) _spawner.EncounterCompleted += OnEncounterCompleted;

            TryStartOpening();
        }

        /// <summary>
        /// QA fix #8 — extracted from OnEnable so the gate can defer the auto-start without
        /// preventing the director's normal initialization (event subscriptions, references).
        /// QA fix #10 — the gate check is now the authoritative three-way
        /// <see cref="IsOpeningStartAllowed"/> test instead of a single local bool.
        /// The director stays enabled and fully initialized either way.
        /// </summary>
        private void TryStartOpening()
        {
            // Evaluate the scene scan once and reuse it for the diagnostic, so a deferred start
            // costs exactly one scan rather than three.
            bool permissionAllowed = OpeningStoryStartPermission.IsAllowed;
            bool sceneRequestsHold = OpeningStoryStartPermission.AnyActiveHoldSourceRequestsHold();

            if (HoldOpeningSequence || !permissionAllowed || sceneRequestsHold)
            {
                Debug.Log("[STORY M01] Opening deferred — startup is owned elsewhere " +
                          $"(localHold={HoldOpeningSequence}, permissionAllowed={permissionAllowed}, " +
                          $"sceneRequestsHold={sceneRequestsHold}).");
                return;
            }

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
            StoryCueEvents.CameraCue -= OnCameraCue;
        }

        private void OnDestroy()
        {
            StoryCueEvents.EventCue -= OnEventCue;
            StoryCueEvents.CameraCue -= OnCameraCue;
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

        // ---- Camera cue handler (fade triggers only; framing lives in StoryCameraController) ----

        private void OnCameraCue(string cueId)
        {
            // Reveal the interior: the camera has already snapped to the first anchor; fade in.
            if (cueId == "m01_interior_kane" && _fade != null)
                _fade.FadeFromBlack(EntryFadeIn);
        }

        // ---- Event cue handler (rig / fade / helicopter / Kane swap) ----

        private void OnEventCue(string cueId)
        {
            switch (cueId)
            {
                case "m01_interior_setup":
                {
                    // Cover the screen BEFORE any visible frame, then build + clone + hide player.
                    if (_fade != null) _fade.SetBlackInstant();
                    Transform sourceVisual = ResolveSourceKaneVisual();
                    if (_interiorRig == null)
                    {
                        _interiorRig = new GameObject("[Story] M01 Interior").AddComponent<HelicopterInteriorRig>();
                        _interiorRig.transform.SetParent(transform, false);
                    }
                    _interiorRig.Setup(InteriorWorldPos, sourceVisual);
                    if (_storyCam != null) _storyCam.SetInteriorRig(_interiorRig);
                    if (_player != null) _player.gameObject.SetActive(false);
                    Debug.Log("[STORY M01] Interior setup behind black: cabin built, Kane cloned, real player hidden.");
                    break;
                }

                case "m01_interior_fade_out":
                    if (_fade != null) _fade.FadeToBlack(ExitFade);
                    break;

                case "m01_interior_teardown":
                    if (_interiorRig != null) _interiorRig.Teardown();
                    break;

                case "m01_fade_in":
                    if (_fade != null) _fade.FadeFromBlack(ExitFade);
                    break;

                case "m01_player_grounded":
                    // Show the real, authoritative player at the insertion point.
                    if (_player != null)
                    {
                        _player.gameObject.SetActive(true);
                        _player.position = new Vector3(0f, 1f, 0f);
                    }
                    if (_interiorRig != null) _interiorRig.Teardown();
                    Debug.Log("[STORY M01] Kane insertion complete — real player grounded.");
                    break;

                case "helicopter_approach":
                    Debug.Log("[STORY M01] Transitioning to exterior approach.");
                    break;
            }
        }

        /// <summary>The live gameplay Toon Soldier transform to clone for the cinematic Kane.</summary>
        private Transform ResolveSourceKaneVisual()
        {
            if (_player == null) return null;
            var animator = _player.GetComponentInChildren<Animator>();
            return animator != null ? animator.transform : _player;
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
                if (_fade != null) _fade.ClearInstant();   // never leave a black screen
                // On a normal finish the helicopter_depart beat already started a graceful fly-away
                // (the placeholder hides itself on reaching its depart point). Only on a SKIP do we
                // yank it instantly so no stray helicopter lingers over gameplay.
                if (skipped && _heli != null) _heli.HideNow();
                Debug.Log("[STORY M01] Opening complete -> gameplay (skip=" + skipped + ").");
            }
            else if (seq == _outroSeq)
            {
                _phase = Phase.Done;
                if (_resultCtrl != null) _resultCtrl.ReleaseResultDisplay();
                if (_fade != null) _fade.ClearInstant();
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

            _fade = FindAnyObjectByType<StoryFadeController>();
            if (_fade == null)
            {
                _fade = new GameObject("[Story] FadeController").AddComponent<StoryFadeController>();
                _fade.transform.SetParent(transform, false);
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
