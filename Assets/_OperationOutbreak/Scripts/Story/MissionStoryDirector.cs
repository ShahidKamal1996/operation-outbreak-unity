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
    /// Milestone 1Z.1 QA fix #10 Step 2A — Mission 01 story director.
    ///
    /// OWNERSHIP BOUNDARY (corrected by manual QA):
    ///
    /// The RAVEN/Kane helicopter-interior sequence is part of the GLOBAL GAME-OPENING CINEMATIC.
    /// It is NOT a Mission 01 cinematic that plays merely because Mission 01 started. This
    /// director therefore NEVER auto-starts it. OnEnable only resolves WHO owns startup:
    ///
    ///   global opening cinematic present -> stand down, wait for StartOpeningStorySequence()
    ///   no opening cinematic owns startup -> EnterGameplayWithoutOpening() (dev bypass)
    ///
    /// What this director still owns:
    ///   - EXECUTING the opening story when the global pipeline explicitly asks it to (it owns the
    ///     interior rig, fades, HUD and Kane swap that the sequence's cues drive),
    ///   - Mission 01 runtime story events: gameplay radio beats, encounter beats, mission outro,
    ///   - the ONE authoritative Mission 01 gameplay-start transition.
    ///
    /// What it does NOT own: the decision of when the global opening cinematic runs.
    ///
    /// Owns the interior rig lifecycle, the cinematic fade (so the camera never visibly travels
    /// between the gameplay world and the y=-300 interior), HUD visibility, and the real/cinematic
    /// Kane swap. The interior uses a VISUAL-ONLY clone of the production Toon Soldier (same model,
    /// material, avatar, controller) so the player recognises "that is my Kane"; the authoritative
    /// gameplay Player is simply hidden during the briefing and re-shown at insertion.
    ///
    /// Cue flow (see Chapter01_Mission01_Opening.asset):
    /// Cue flow below applies ONLY once the global opening cinematic hands off.
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
        /// QA fix #10 — is the opening STORY SEQUENCE permitted to run right now?
        ///
        /// Step 2A note: this is no longer an auto-start decision. The director never auto-starts
        /// the opening story any more (see <see cref="OnEnable"/>). This is now the guard on the
        /// EXPLICIT entry point <see cref="StartOpeningStorySequence"/>, so the global opening
        /// cinematic pipeline stays the only thing that can run that sequence.
        ///
        /// Permitted only when ALL THREE agree:
        ///   1. no local legacy hold,
        ///   2. the process-wide permission is allowed, and
        ///   3. no active scene component still claims exclusive ownership of the opening.
        ///
        /// Check 3 reads serialized intent directly off the scene (Unity deserializes
        /// [SerializeField] state before ANY Awake runs), which is what keeps the answer correct
        /// regardless of component initialization order.
        /// </summary>
        public bool IsOpeningStartAllowed =>
            !HoldOpeningSequence
            && OpeningStoryStartPermission.IsAllowed
            && !OpeningStoryStartPermission.AnyActiveHoldSourceRequestsHold();

        /// <summary>
        /// Step 2A — true when a global opening cinematic in the scene owns game startup, so this
        /// director must NOT put Mission 01 into gameplay by itself. Answerable from serialized
        /// state before the cinematic initializes.
        /// </summary>
        public bool IsOpeningOwnedByGlobalCinematic =>
            OpeningStoryStartPermission.AnyActiveHoldSourceRequestsHold();

        /// <summary>True once Mission 01 is in its normal, playable gameplay state.</summary>
        public bool IsInGameplayPhase => _phase == Phase.Gameplay;

        /// <summary>True while the RAVEN/Kane opening story sequence is running.</summary>
        public bool IsPlayingOpeningStory => _phase == Phase.Opening;

        /// <summary>
        /// Step 2A — THE explicit entry point for the GLOBAL OPENING CINEMATIC pipeline to run the
        /// RAVEN/Kane helicopter-interior sequence. Nothing calls this automatically; the opening
        /// cinematic coordinator invokes it during the 1Z.1C handoff.
        ///
        /// Ownership boundary: this director still EXECUTES the sequence (it owns the interior rig,
        /// fades, HUD and Kane swap that the sequence's cues drive), but it no longer DECIDES when
        /// the sequence runs. Returns true if the sequence actually started.
        /// </summary>
        public bool StartOpeningStorySequence()
        {
            if (_phase != Phase.Idle)
            {
                Debug.LogWarning("[STORY M01] Opening story requested but the director has already " +
                                 "left Idle (phase=" + _phase + ") — ignoring duplicate request.");
                return false;
            }

            if (!IsOpeningStartAllowed)
            {
                Debug.LogWarning("[STORY M01] Opening story requested but it is not permitted yet " +
                                 "(a hold is still active). Release the hold before handing off.");
                return false;
            }

            if (mission01 == null || mission01.MissionId != "mission_01"
                || _openingSeq == null || _runner == null)
            {
                Debug.LogWarning("[STORY M01] Opening story requested but Mission 01 / sequence / " +
                                 "runner references are missing — entering gameplay directly.");
                EnterMission01GameplayState(skipped: true);
                return false;
            }

            _phase = Phase.Opening;
            if (_sections != null) _sections.enabled = false;
            if (_hudCtrl != null) _hudCtrl.HideGameplayHud();
            Debug.Log("[STORY M01] Opening story started (requested by the global opening cinematic).");
            _runner.LoadSequence(_openingSeq);
            return true;
        }

        /// <summary>
        /// Step 2A — DEVELOPMENT BYPASS entry point. Puts Mission 01 straight into the exact
        /// gameplay state that previously existed only AFTER the opening completed or was skipped.
        ///
        /// This deliberately routes through the SAME single transition
        /// (<see cref="EnterMission01GameplayState"/>) the completed/skipped opening uses, so there
        /// is exactly one gameplay-start path and no duplicated initialization.
        /// </summary>
        public void EnterGameplayWithoutOpening()
        {
            if (_phase != Phase.Idle)
            {
                Debug.Log("[STORY M01] Gameplay entry requested but the director already left Idle " +
                          "(phase=" + _phase + ") — ignoring.");
                return;
            }

            Debug.Log("[STORY M01] No global opening cinematic owns startup — entering Mission 01 " +
                      "gameplay directly (opening story bypassed).");
            EnterMission01GameplayState(skipped: false);
        }

        /// <summary>
        /// Legacy QA fix #8/#10 API. Clears the director's own hold, then resolves startup
        /// ownership exactly as OnEnable does.
        /// </summary>
        public void ReleaseOpeningSequence()
        {
            HoldOpeningSequence = false;
            ResolveStartupOwnership();
        }

        /// <summary>
        /// Full handoff: clears the local gate and tells every active hold source to relinquish
        /// its claim, then resolves startup ownership. NOTHING calls this automatically.
        /// </summary>
        public void ForceReleaseAllOpeningHolds()
        {
            HoldOpeningSequence = false;
            OpeningStoryStartPermission.ReleaseSceneHoldSources();
            ResolveStartupOwnership();
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

            // Step 2A: resolve WHO owns startup. This never starts the global opening story.
            ResolveStartupOwnership();
        }

        /// <summary>
        /// Step 2A — the ONLY thing OnEnable does about startup: decide who owns it. It never
        /// starts the global opening story itself.
        ///
        ///   A global opening cinematic is present and owns startup
        ///       -> do nothing. The cinematic pipeline will call StartOpeningStorySequence()
        ///          during the 1Z.1C handoff.
        ///
        ///   No global opening cinematic owns startup (bypass, or no cinematic in the scene)
        ///       -> enter Mission 01 gameplay directly, WITHOUT the RAVEN/Kane opening story.
        ///
        /// This is the ownership correction: the RAVEN/Kane sequence is part of the global game
        /// opening, not something Mission 01 plays merely because Mission 01 started.
        /// </summary>
        private void ResolveStartupOwnership()
        {
            if (_phase != Phase.Idle) return; // already resolved

            if (!IsMission01Context())
            {
                // Another mission: this director has no Mission 01 opening responsibility at all.
                return;
            }

            if (IsOpeningOwnedByGlobalCinematic)
            {
                Debug.Log("[STORY M01] Global opening cinematic owns startup — the director will " +
                          "NOT start the opening story. Awaiting explicit handoff.");
                return;
            }

            EnterGameplayWithoutOpening();
        }

        private bool IsMission01Context() =>
            mission01 != null && mission01.MissionId == "mission_01";

        /// <summary>
        /// Step 2A — THE single authoritative Mission 01 gameplay-start transition.
        ///
        /// Every path into gameplay funnels through here so initialization is never duplicated:
        ///   - opening story completed normally,
        ///   - opening story skipped with Space/Escape,
        ///   - development bypass (no opening story at all).
        ///
        /// Idempotent: re-entry after the phase has advanced is a no-op.
        /// </summary>
        private void EnterMission01GameplayState(bool skipped)
        {
            if (_phase == Phase.Gameplay) return;

            _phase = Phase.Gameplay;
            if (_sections != null) _sections.enabled = true;
            if (_hudCtrl != null) _hudCtrl.RestoreGameplayHud();
            if (_player != null && !_player.gameObject.activeSelf)
                _player.gameObject.SetActive(true);
            if (_interiorRig != null) _interiorRig.Teardown();
            if (_fade != null) _fade.ClearInstant();   // never leave a black screen

            // On a normal finish the helicopter_depart beat already started a graceful fly-away
            // (the placeholder hides itself on reaching its depart point). On a SKIP — and in
            // bypass mode, where the helicopter never flew — yank it so nothing lingers.
            if (skipped && _heli != null) _heli.HideNow();

            Debug.Log("[STORY M01] Mission 01 gameplay state entered (skipped=" + skipped + ").");
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
                // Space/Escape skip and normal completion both land here, and both go through the
                // SAME single gameplay transition the bypass path uses.
                Debug.Log("[STORY M01] Opening story complete -> gameplay (skip=" + skipped + ").");
                EnterMission01GameplayState(skipped);
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
