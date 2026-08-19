using System;
using System.Collections.Generic;
using OperationOutbreak.Enemies;
using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1M - drives the mission as a sequence of forward sections instead of
    /// one static arena.
    ///
    ///   MISSION START -> SECTION 1 -> advance -> SECTION 2 -> advance -> SECTION 3 -> COMPLETE
    ///
    /// Responsibilities are deliberately narrow: this component owns *progression state
    /// only*. It decides which section is current, when a section may activate, and when
    /// the mission is finished. It does not spawn anything itself and it does not own any
    /// UI - it drives the existing EnemySpawner and raises events that presentation
    /// components listen to.
    ///
    /// Milestone 1T - the section/composition data now comes from a MissionDefinition
    /// asset (serialized reference), so this component consumes mission DATA instead of
    /// reconstructing the mission from hard-coded tables. The runtime flow is identical:
    /// load the definition, activate section 1, request its configured enemies, wait for
    /// the clear, advance through the definition's ordered sections, and fire the single
    /// Mission Complete path after the final section. There is still exactly ONE
    /// mission-flow system and ONE victory path.
    ///
    /// Fallback policy (documented): a missing MissionDefinition reference is a setup
    /// error - it logs a loud, actionable diagnostic AND falls back to the verified
    /// prototype mission (3 sections / 9 Basic + 3 Runner) so gameplay can never become
    /// unpredictable or partially unplayable. The committed Mission_01 asset is the
    /// production source of truth.
    ///
    /// Design constraints honoured here:
    ///   - No static or global state. A scene reload is the reset, so every field is an
    ///     instance field and OnEnable restores the mission to Section 1.
    ///   - No per-frame scene searches. References are serialized (with a one-time Awake
    ///     fallback) and the Update loop only reads a cached Transform position.
    ///   - Activation is a lightweight position poll, not a physics trigger: the Player
    ///     has no Rigidbody, so OnTriggerEnter would never fire.
    ///   - Each section latches. Walking back across a completed threshold can never
    ///     restart it, because progression only ever moves forward through the array.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionSectionController : MonoBehaviour
    {
        [Header("Mission Data (Milestone 1T)")]
        [Tooltip("The data-driven mission this controller executes. Assign the committed " +
                 "Mission_01 asset. A missing reference logs an error and falls back to the " +
                 "verified prototype mission so gameplay stays well-defined.")]
        [SerializeField] private MissionDefinition missionDefinition;

        [Header("References")]
        [SerializeField] private EnemySpawner enemySpawner;
        [SerializeField] private PlayerLaneBounds laneBounds;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private Transform playerTransform;

        [Tooltip("Optional. Shows the temporary \"SECTION n\" banner when a section begins.")]
        [SerializeField] private MissionSectionHud sectionHud;

        [Header("Activation")]
        [Tooltip("Seconds to wait after a section is cleared before the next one may be " +
                 "activated. Gives the player a beat to breathe before advancing.")]
        [Min(0f)] [SerializeField] private float postSectionSettle = 0.35f;

        [Tooltip("Extra forward travel opened up once a section is cleared, measured past " +
                 "the NEXT section's activation line. This is what lets the player walk " +
                 "forward into the next section - the corridor is never opened all the " +
                 "way to the next combat area before it activates.")]
        [Min(0.5f)] [SerializeField] private float travelAllowance = 1.5f;

        [SerializeField] private bool verboseLogging = true;

        /// <summary>Raised when a section becomes active. Argument is the zero-based index.</summary>
        public event Action<int, MissionDefinition.MissionSection> SectionStarted;

        /// <summary>Raised when a section's combat is cleared. Argument is the zero-based index.</summary>
        public event Action<int, MissionDefinition.MissionSection> SectionCleared;

        /// <summary>
        /// Raised once when the LAST section has been cleared. This is the single mission
        /// victory signal - Mission Complete listens to the spawner's own encounter event,
        /// which is only raised at the end of the final section.
        /// </summary>
        public event Action MissionCompleted;

        private int _currentIndex = -1;
        private bool _sectionActive;
        private bool _missionComplete;
        private bool _playerDead;
        private float _settleTimer;
        private bool _fallbackWarned;
        private MissionDefinition _fallbackDefinition;

        /// <summary>Zero-based index of the section in progress, or the last one cleared.</summary>
        public int CurrentSectionIndex => _currentIndex;

        /// <summary>Human-facing section number, 1-based. 0 before the mission starts.</summary>
        public int CurrentSectionNumber => _currentIndex + 1;

        /// <summary>True while a section's combat is running.</summary>
        public bool IsSectionActive => _sectionActive;

        /// <summary>True once every section has been cleared.</summary>
        public bool IsMissionComplete => _missionComplete;

        /// <summary>Number of sections in the mission being executed.</summary>
        public int SectionCount => ResolvedSections.Count;

        /// <summary>
        /// How far forward the player may currently travel. Used by PlayerLaneBounds so the
        /// corridor grows section by section instead of exposing the whole lane at once.
        /// </summary>
        public float CurrentForwardLimitZ { get; private set; }

        /// <summary>
        /// Rear edge of the CURRENT section's combat band - the previous section's forward
        /// limit, or the rear of the lane while Section 1 is running. Upgrade placement
        /// uses this together with CurrentForwardLimitZ so a pickup lands in the space the
        /// player is actually fighting in, never stranded far behind in a cleared section
        /// and never ahead in a locked one.
        /// </summary>
        public float CurrentSectionMinZ { get; private set; }

        /// <summary>
        /// The mission sections this controller executes. Primary source is the assigned
        /// MissionDefinition asset; when none is assigned the verified prototype mission is
        /// built in memory (with a loud diagnostic) so gameplay stays well-defined.
        /// </summary>
        private IReadOnlyList<MissionDefinition.MissionSection> ResolvedSections
        {
            get
            {
                if (missionDefinition != null && missionDefinition.SectionCount > 0)
                {
                    return missionDefinition.Sections;
                }

                if (!_fallbackWarned)
                {
                    _fallbackWarned = true;
                    Debug.LogError(
                        "[1T] No MissionDefinition is assigned to '" + name + "'. Assign the " +
                        "committed Mission_01 asset (Assets/_OperationOutbreak/Resources/" +
                        "MissionDefinitions/Mission_01.asset) or create one via Assets > Create > " +
                        "Operation Outbreak > Mission Definition. Using the verified prototype " +
                        "mission (3 sections / 9 Basic + 3 Runner) as a development fallback until " +
                        "then.", this);
                }

                if (_fallbackDefinition == null)
                {
                    _fallbackDefinition = MissionDefinition.CreateVerifiedPrototypeMission();
                }

                return _fallbackDefinition.Sections;
            }
        }

        private void Awake()
        {
            if (enemySpawner == null) enemySpawner = FindAnyObjectByType<EnemySpawner>();
            if (laneBounds == null) laneBounds = FindAnyObjectByType<PlayerLaneBounds>();
            if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
            if (sectionHud == null) sectionHud = FindAnyObjectByType<MissionSectionHud>();

            if (playerTransform == null)
            {
                PlayerController controller = FindAnyObjectByType<PlayerController>();
                if (controller != null)
                {
                    playerTransform = controller.transform;
                }
            }
        }

        private void OnEnable()
        {
            // Instance state only: a scene reload is the full mission reset.
            _currentIndex = -1;
            _sectionActive = false;
            _missionComplete = false;
            _playerDead = false;
            _settleTimer = 0f;

            IReadOnlyList<MissionDefinition.MissionSection> sections = ResolvedSections;

            CurrentForwardLimitZ = sections.Count > 0
                ? sections[0].forwardLimitZ
                : 15f;

            CurrentSectionMinZ = float.NegativeInfinity;

            ApplyForwardLimit();

            if (playerHealth != null)
            {
                playerHealth.Died += HandlePlayerDied;
            }

            if (enemySpawner != null)
            {
                enemySpawner.SectionCleared += HandleSectionCleared;
            }
        }

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.Died -= HandlePlayerDied;
            }

            if (enemySpawner != null)
            {
                enemySpawner.SectionCleared -= HandleSectionCleared;
            }
        }

        private void Start()
        {
            // Section 1 opens as soon as the mission begins.
            TryActivate(0);
        }

        private void Update()
        {
            if (_missionComplete || _playerDead || _sectionActive)
            {
                return;
            }

            int next = _currentIndex + 1;
            IReadOnlyList<MissionDefinition.MissionSection> sections = ResolvedSections;

            if (next >= sections.Count || playerTransform == null)
            {
                return;
            }

            if (_settleTimer > 0f)
            {
                _settleTimer -= Time.deltaTime;
                return;
            }

            // Lightweight forward-progress check. No physics, no scene searches: just the
            // cached player transform against the next section's threshold.
            if (playerTransform.position.z >= sections[next].activationZ)
            {
                TryActivate(next);
            }
        }

        /// <summary>
        /// Activates a section exactly once. Because progression only ever advances the
        /// index, a completed threshold can never re-trigger when the player walks back.
        /// </summary>
        private void TryActivate(int index)
        {
            if (_missionComplete || _playerDead || _sectionActive) return;

            IReadOnlyList<MissionDefinition.MissionSection> sections = ResolvedSections;
            if (index < 0 || index >= sections.Count) return;
            if (index <= _currentIndex) return;

            _currentIndex = index;
            _sectionActive = true;

            MissionDefinition.MissionSection section = sections[index];

            // The band the player fights in during this section: from the previous
            // section's stop line up to this one's. Section 1 keeps the lane's own rear.
            CurrentSectionMinZ = index > 0
                ? sections[index - 1].forwardLimitZ
                : float.NegativeInfinity;

            // Open the corridor up to this section's forward limit.
            CurrentForwardLimitZ = Mathf.Max(CurrentForwardLimitZ, section.forwardLimitZ);
            ApplyForwardLimit();

            if (sectionHud != null)
            {
                sectionHud.Show(section.label, section.subtitle);
            }

            if (enemySpawner != null)
            {
                // Milestone 1T - the composition is supplied by the MissionDefinition and
                // passed straight through (by 1S stable archetype id). Mission progression
                // stays ignorant of enemy types: it only cares that the section reports
                // itself cleared.
                enemySpawner.BeginSection(
                    index,
                    section.TotalEnemyCount,
                    section.forwardLimitZ + section.spawnAheadOfLimit,
                    BuildSpawnComposition(section));
            }

            if (verboseLogging)
            {
                Debug.Log(
                    $"Section {index + 1} started - {section.TotalEnemyCount} enemies, "
                    + $"forward limit z={section.forwardLimitZ}.",
                    this);
            }

            SectionStarted?.Invoke(index, section);
        }

        private void HandleSectionCleared(int index)
        {
            // Ignore stale callbacks for a section we are no longer running.
            if (!_sectionActive || index != _currentIndex || _playerDead || _missionComplete)
            {
                return;
            }

            _sectionActive = false;
            _settleTimer = postSectionSettle;

            IReadOnlyList<MissionDefinition.MissionSection> sections = ResolvedSections;
            MissionDefinition.MissionSection section = sections[index];

            if (verboseLogging)
            {
                Debug.Log($"Section {index + 1} cleared.", this);
            }

            SectionCleared?.Invoke(index, section);

            bool wasFinal = index >= sections.Count - 1;

            if (wasFinal)
            {
                _missionComplete = true;

                if (verboseLogging)
                {
                    Debug.Log("All mission sections cleared.", this);
                }

                MissionCompleted?.Invoke();

                // The spawner raises its own EncounterCompleted here, which is what the
                // existing Mission Complete UI already listens to. One victory path only.
                if (enemySpawner != null)
                {
                    enemySpawner.CompleteEncounter();
                }

                return;
            }

            // Open just enough corridor for the player to WALK to the next activation
            // line. The next section's own forward limit stays locked until it activates,
            // so the player can never wander into an unstarted combat area.
            int next = index + 1;

            CurrentForwardLimitZ = Mathf.Max(
                CurrentForwardLimitZ,
                sections[next].activationZ + travelAllowance);
            ApplyForwardLimit();

            if (sectionHud != null)
            {
                sectionHud.ShowAdvancePrompt();
            }
        }

        private void HandlePlayerDied()
        {
            // Game Over always wins: progression stops dead and no further section can open.
            _playerDead = true;
            _sectionActive = false;
        }

        /// <summary>Pushes the current forward limit into the shared lane bounds.</summary>
        private void ApplyForwardLimit()
        {
            if (laneBounds != null)
            {
                laneBounds.SetForwardLimit(CurrentForwardLimitZ);
            }
        }

        /// <summary>
        /// Milestone 1T - flattens a MissionDefinition section's composition (1S stable
        /// ids) into the spawner's spawn entries. No archetype branching here: the ids
        /// are passed through untouched and the shared spawner resolves them.
        /// </summary>
        private static List<EnemySpawnEntry> BuildSpawnComposition(
            MissionDefinition.MissionSection section)
        {
            List<EnemySpawnEntry> entries = new List<EnemySpawnEntry>();

            if (section.composition == null)
            {
                return entries;
            }

            for (int i = 0; i < section.composition.Count; i++)
            {
                MissionDefinition.EnemyCompositionEntry entry = section.composition[i];
                if (entry != null)
                {
                    entries.Add(new EnemySpawnEntry(entry.archetypeId, entry.count));
                }
            }

            return entries;
        }
    }
}
