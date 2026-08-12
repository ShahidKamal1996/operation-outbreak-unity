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
    /// Design constraints honoured here:
    ///   - No static or global state. A scene reload is the reset, so every field is an
    ///     instance field and OnEnable restores the mission to Section 1.
    ///   - No per-frame scene searches. References are serialized (with a one-time Awake
    ///     fallback) and the Update loop only reads a cached Transform position.
    ///   - Activation is a lightweight position poll, not a physics trigger: the Player
    ///     has no Rigidbody, so OnTriggerEnter would never fire.
    ///   - Each section latches. Walking back across a completed threshold can never
    ///     restart it, because progression only ever moves forward through the array.
    ///   - Expandable: add another entry to "sections" and the whole flow follows.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionSectionController : MonoBehaviour
    {
        /// <summary>Authoring data for one mission section. Pure data, no behaviour.</summary>
        [Serializable]
        public sealed class SectionDefinition
        {
            [Tooltip("Short HUD label, e.g. \"SECTION 1\".")]
            public string label = "SECTION 1";

            [Tooltip("HUD subtitle, e.g. \"OUTBREAK\".")]
            public string subtitle = "OUTBREAK";

            [Tooltip("The player must reach this Z before the section activates. " +
                     "Section 1 normally uses the mission start Z so it opens immediately.")]
            public float activationZ;

            [Tooltip("How far forward the player may travel while this section is the " +
                     "current one. Also caps where upgrade pickups may appear.")]
            public float forwardLimitZ = 15f;

            [Tooltip("Zombies spawned by this section. Reuses the existing EnemySpawner waves.")]
            [Min(1)] public int enemyCount = 3;

            [Tooltip("Where this section's zombies appear, relative to the section's " +
                     "forward limit. Positive values are ahead of the player's stop line.")]
            public float spawnAheadOfLimit = 4f;
        }

        [Header("References")]
        [SerializeField] private EnemySpawner enemySpawner;
        [SerializeField] private PlayerLaneBounds laneBounds;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private Transform playerTransform;

        [Tooltip("Optional. Shows the temporary \"SECTION n\" banner when a section begins.")]
        [SerializeField] private MissionSectionHud sectionHud;

        [Header("Sections (in forward order)")]
        [SerializeField]
        private List<SectionDefinition> sections = new List<SectionDefinition>();

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
        public event Action<int, SectionDefinition> SectionStarted;

        /// <summary>Raised when a section's combat is cleared. Argument is the zero-based index.</summary>
        public event Action<int, SectionDefinition> SectionCleared;

        /// <summary>
        /// Raised once when the LAST section has been cleared. This is the single mission
        /// victory signal - Mission Complete listens to the spawner's own encounter event,
        /// which is now only raised at the end of the final section.
        /// </summary>
        public event Action MissionCompleted;

        private int _currentIndex = -1;
        private bool _sectionActive;
        private bool _missionComplete;
        private bool _playerDead;
        private float _settleTimer;

        /// <summary>Zero-based index of the section in progress, or the last one cleared.</summary>
        public int CurrentSectionIndex => _currentIndex;

        /// <summary>Human-facing section number, 1-based. 0 before the mission starts.</summary>
        public int CurrentSectionNumber => _currentIndex + 1;

        /// <summary>True while a section's combat is running.</summary>
        public bool IsSectionActive => _sectionActive;

        /// <summary>True once every section has been cleared.</summary>
        public bool IsMissionComplete => _missionComplete;

        public int SectionCount => sections != null ? sections.Count : 0;

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

            if (sections == null || sections.Count == 0)
            {
                BuildDefaultSections();
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

            CurrentForwardLimitZ = sections != null && sections.Count > 0
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

            if (sections == null || next >= sections.Count || playerTransform == null)
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
            if (sections == null || index < 0 || index >= sections.Count) return;
            if (index <= _currentIndex) return;

            _currentIndex = index;
            _sectionActive = true;

            SectionDefinition section = sections[index];

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
                enemySpawner.BeginSection(
                    index,
                    section.enemyCount,
                    section.forwardLimitZ + section.spawnAheadOfLimit);
            }

            if (verboseLogging)
            {
                Debug.Log(
                    $"Section {index + 1} started - {section.enemyCount} enemies, "
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

            SectionDefinition section = sections[index];

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

            if (next < sections.Count)
            {
                CurrentForwardLimitZ = Mathf.Max(
                    CurrentForwardLimitZ,
                    sections[next].activationZ + travelAllowance);
                ApplyForwardLimit();
            }

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
        /// Default 3/4/5 layout used when the scene supplies no data. Values match the
        /// existing corridor: the approved Section 1 stop line is z=15, and each later
        /// section advances the player 18 units further along the 100-unit lane.
        /// </summary>
        private void BuildDefaultSections()
        {
            sections = new List<SectionDefinition>
            {
                new SectionDefinition
                {
                    label = "SECTION 1", subtitle = "OUTBREAK",
                    activationZ = -100f, forwardLimitZ = 15f,
                    enemyCount = 3, spawnAheadOfLimit = 1f
                },
                new SectionDefinition
                {
                    label = "SECTION 2", subtitle = "ADVANCE",
                    activationZ = 20f, forwardLimitZ = 33f,
                    enemyCount = 4, spawnAheadOfLimit = 4f
                },
                new SectionDefinition
                {
                    label = "SECTION 3", subtitle = "FINAL PUSH",
                    activationZ = 38f, forwardLimitZ = 51f,
                    enemyCount = 5, spawnAheadOfLimit = 4f
                }
            };
        }
    }
}
