using System.Collections.Generic;
using System.Globalization;
using OperationOutbreak.Enemies;
using OperationOutbreak.Mission;
using OperationOutbreak.Player;
using OperationOutbreak.UI;
using OperationOutbreak.Upgrades;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OperationOutbreak.Diagnostics
{
    /// <summary>
    /// Milestone 1O - dev-only observer that records one run and prints a structured
    /// PASS / FAIL / WARNING report to the Console at Mission Complete or Game Over.
    ///
    /// DESIGN RULES THIS COMPONENT OBEYS
    ///
    ///   OBSERVE ONLY. It never spawns, moves, damages, applies an upgrade, repositions a
    ///   spawn or changes any timing. Every field it reads is already public or was exposed
    ///   as a read-only property for this milestone. Deleting this component (or leaving it
    ///   disabled) changes nothing about how the game plays.
    ///
    ///   EVENT DRIVEN. There is no Update loop, no per-frame FindObjectOfType, no
    ///   reflection and no coroutine. Every measurement is taken inside a callback that the
    ///   approved systems were already raising, or inside one of the small spawn/resolution
    ///   events added for this milestone.
    ///
    ///   NO PER-FRAME COST. Records are appended to in-memory lists at discrete moments
    ///   (a spawn, a death, a pickup, a section change). The single report string is built
    ///   once, at the end of the run, and written with one Debug.Log call.
    ///
    ///   INSTANCE STATE ONLY. Nothing static, so a scene reload is a complete reset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayDiagnostics : MonoBehaviour
    {
        [Header("Gating")]
        [Tooltip("Master switch. When off, this component unsubscribes from everything and " +
                 "records nothing. Gameplay is completely unaffected either way.")]
        [SerializeField] private bool diagnosticsEnabled = true;

        [Tooltip("When enabled, diagnostics only run in the Editor or a Development Build, " +
                 "so a release player can never carry the recorder.")]
        [SerializeField] private bool developmentBuildsOnly = true;

        [Tooltip("Optional tiny 'DIAGNOSTICS ON' marker in a screen corner. Built once at " +
                 "startup; it has no per-frame cost and no gameplay effect.")]
        [SerializeField] private bool showOnScreenIndicator;

        [Header("Observed Systems")]
        [SerializeField] private MissionSectionController missionSections;
        [SerializeField] private EnemySpawner enemySpawner;
        [SerializeField] private UpgradePickupDirector upgradeDirector;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private PlayerLaneBounds laneBounds;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private MissionCompleteController missionComplete;
        [SerializeField] private GameOverController gameOver;

        private readonly DiagnosticRunData _data = new DiagnosticRunData();

        /// <summary>Live enemy spawn positions, used only for the spawn-overlap observation.</summary>
        private readonly List<Vector3> _liveEnemyPositions = new List<Vector3>();

        private readonly Dictionary<ZombieController, EnemyRecord> _enemyLookup =
            new Dictionary<ZombieController, EnemyRecord>();

        private readonly HashSet<int> _clearedSections = new HashSet<int>();

        private UpgradeRecord _activeUpgrade;
        private Vector3 _previousPickupPosition;
        private bool _hasPreviousPickup;

        private int _nextEnemyRuntimeId = 1;
        private bool _active;
        private bool _reported;

        /// <summary>True when the recorder is actually observing this run.</summary>
        public bool IsRecording => _active;

        /// <summary>Read-only access to the recorded run, for tooling and tests.</summary>
        public DiagnosticRunData RunData => _data;

        private bool ShouldRun()
        {
            if (!diagnosticsEnabled)
            {
                return false;
            }

            if (!developmentBuildsOnly)
            {
                return true;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return false;
#endif
        }

        private void Awake()
        {
            _active = ShouldRun();

            if (!_active)
            {
                // Nothing is subscribed and nothing is allocated beyond the empty run data.
                enabled = false;
                return;
            }

            if (showOnScreenIndicator)
            {
                BuildIndicator();
            }
        }

        private void OnEnable()
        {
            if (!_active)
            {
                return;
            }

            if (missionSections != null)
            {
                missionSections.SectionStarted += HandleSectionStarted;
                missionSections.SectionCleared += HandleSectionCleared;
                missionSections.MissionCompleted += HandleMissionCompleted;
            }

            if (enemySpawner != null)
            {
                enemySpawner.EnemySpawned += HandleEnemySpawned;
            }

            if (upgradeDirector != null)
            {
                upgradeDirector.RunOrderBuilt += HandleRunOrderBuilt;
                upgradeDirector.PickupSpawned += HandlePickupSpawned;
                upgradeDirector.PickupResolved += HandlePickupResolved;
            }

            if (playerHealth != null)
            {
                playerHealth.Died += HandlePlayerDied;
                playerHealth.HealthChanged += HandleHealthChanged;
            }

            if (missionComplete != null)
            {
                missionComplete.VictoryShown += HandleVictoryShown;
            }

            if (gameOver != null)
            {
                gameOver.GameOverShown += HandleGameOverShown;
            }
        }

        private void OnDisable()
        {
            if (missionSections != null)
            {
                missionSections.SectionStarted -= HandleSectionStarted;
                missionSections.SectionCleared -= HandleSectionCleared;
                missionSections.MissionCompleted -= HandleMissionCompleted;
            }

            if (enemySpawner != null)
            {
                enemySpawner.EnemySpawned -= HandleEnemySpawned;
            }

            if (upgradeDirector != null)
            {
                upgradeDirector.RunOrderBuilt -= HandleRunOrderBuilt;
                upgradeDirector.PickupSpawned -= HandlePickupSpawned;
                upgradeDirector.PickupResolved -= HandlePickupResolved;
            }

            if (playerHealth != null)
            {
                playerHealth.Died -= HandlePlayerDied;
                playerHealth.HealthChanged -= HandleHealthChanged;
            }

            if (missionComplete != null)
            {
                missionComplete.VictoryShown -= HandleVictoryShown;
            }

            if (gameOver != null)
            {
                gameOver.GameOverShown -= HandleGameOverShown;
            }

            foreach (KeyValuePair<ZombieController, EnemyRecord> pair in _enemyLookup)
            {
                if (pair.Key != null)
                {
                    pair.Key.Died -= HandleEnemyDied;
                    pair.Key.DamageTaken -= HandleEnemyDamaged;
                    pair.Key.DamagedPlayer -= HandleEnemyDamagedPlayer;
                }
            }
        }

        private void Start()
        {
            if (!_active)
            {
                return;
            }

            _data.MissionStartTime = Time.time;

            if (playerHealth != null)
            {
                _data.Player.BaseMaxHealth = playerHealth.MaxHealth;
                _data.Player.FinalMaxHealth = playerHealth.MaxHealth;
            }

            if (laneBounds != null)
            {
                _data.LaneMinX = laneBounds.MinX;
                _data.LaneMaxX = laneBounds.MaxX;
                _data.LaneMinZ = laneBounds.MinZ;
                _data.LaneMaxZ = laneBounds.MaxZ;
                _data.LaneBoundsCaptured = true;
            }

            if (enemySpawner != null)
            {
                _data.SpawnClearanceRadius = enemySpawner.SpawnClearanceRadius;
            }

            if (upgradeDirector != null)
            {
                _data.MinimumDistanceFromPlayer = upgradeDirector.MinimumDistanceFromPlayer;
                _data.MinimumDistanceFromPreviousPickup = upgradeDirector.MinimumDistanceFromPreviousPickup;
                _data.AuthoredUpgradeCount = upgradeDirector.OpportunityCount;

                // The director shuffles in its OnEnable, which can run before this
                // recorder subscribed to RunOrderBuilt. If that happened, read the same
                // permutation directly so the report never shows an empty order purely
                // because of script execution order.
                if (_data.UpgradeRunOrder.Count == 0)
                {
                    HandleRunOrderBuilt(upgradeDirector.CurrentRunOrder);
                }
            }

            Debug.Log("[DIAGNOSTICS] Recording this run. A full report prints at Mission Complete or Game Over.", this);
        }

        // ------------------------------------------------------------------ mission

        private void HandleSectionStarted(int index, MissionSectionController.SectionDefinition definition)
        {
            SectionRecord record = new SectionRecord
            {
                SectionIndex = index,
                Label = definition != null ? definition.label : $"SECTION {index + 1}",
                ActivationTime = Time.time,
                ExpectedEnemyCount = definition != null ? definition.TotalEnemyCount : 0
            };

            _data.Sections.Add(record);
        }

        private void HandleSectionCleared(int index, MissionSectionController.SectionDefinition definition)
        {
            if (!_clearedSections.Add(index) && _data.DuplicateSectionClearIndex < 0)
            {
                _data.DuplicateSectionClearIndex = index;
            }

            SectionRecord record = _data.GetSection(index);

            if (record != null)
            {
                record.Cleared = true;
                record.ClearedTime = Time.time;
            }
        }

        private void HandleMissionCompleted()
        {
            _data.MissionCompleteEventCount++;

            if (!_data.MissionCompleted)
            {
                _data.MissionCompleted = true;
                _data.MissionCompleteTime = Time.time;
            }
        }

        private void HandleVictoryShown()
        {
            // The overlay is the user-visible end of the run; report once it appears.
            if (!_data.MissionCompleted)
            {
                _data.MissionCompleted = true;
                _data.MissionCompleteTime = Time.time;
                _data.MissionCompleteEventCount++;
            }

            EmitReport();
        }

        private void HandleGameOverShown()
        {
            _data.GameOverEventCount++;

            if (!_data.GameOver)
            {
                _data.GameOver = true;
                _data.GameOverTime = Time.time;
            }

            EmitReport();
        }

        // ------------------------------------------------------------------ enemies

        private void HandleEnemySpawned(ZombieController enemy, EnemySpawnReport report)
        {
            if (enemy == null)
            {
                return;
            }

            string archetypeId = report.ArchetypeId;
            int sectionIndex = report.SectionIndex;
            Vector3 spawnPosition = enemy.transform.position;
            Vector3 playerPosition = playerTransform != null ? playerTransform.position : Vector3.zero;

            // Overlap is OBSERVED here and never corrected: the spawner's own nudge pass has
            // already run, and this simply records what it produced.
            float nearest = DiagnosticRules.NearestDistance(spawnPosition, _liveEnemyPositions);
            bool overlapping = nearest >= 0f && nearest < _data.SpawnClearanceRadius;

            EnemyRecord record = new EnemyRecord
            {
                RuntimeId = _nextEnemyRuntimeId++,
                Archetype = string.IsNullOrEmpty(archetypeId) ? EnemyArchetypeId.Basic : archetypeId,
                SectionIndex = sectionIndex,
                SpawnPosition = spawnPosition,
                PlayerPositionAtSpawn = playerPosition,
                InitialDistanceToPlayer = DiagnosticRules.PlanarDistance(spawnPosition, playerPosition),
                SpawnTime = Time.time,
                MoveSpeed = enemy.MoveSpeed,
                MaxHealth = enemy.MaxHealth,
                AttackDamage = enemy.AttackDamage,
                SpawnedOverlapping = overlapping,
                NearestEnemyDistanceAtSpawn = nearest,
                BandPosition = report.BandPosition,
                RequestedSpawnOffset = report.RequestedOffset,
                StandoffUsed = report.StandoffUsed
            };

            _data.Enemies.Add(record);
            _enemyLookup[enemy] = record;
            _liveEnemyPositions.Add(spawnPosition);

            SectionRecord section = _data.GetSection(sectionIndex);

            if (section != null)
            {
                section.SpawnedEnemyCount++;
                section.Enemies.Add(record);
            }

            enemy.Died += HandleEnemyDied;
            enemy.DamageTaken += HandleEnemyDamaged;
            enemy.DamagedPlayer += HandleEnemyDamagedPlayer;
        }

        private void HandleEnemyDamaged(ZombieController enemy, int amount)
        {
            if (enemy != null && _enemyLookup.TryGetValue(enemy, out EnemyRecord record))
            {
                record.ProjectileHits++;
            }
        }

        private void HandleEnemyDamagedPlayer(ZombieController enemy, int amount)
        {
            if (enemy != null && _enemyLookup.TryGetValue(enemy, out EnemyRecord record))
            {
                record.DamagedPlayer = true;
            }
        }

        private void HandleEnemyDied(ZombieController enemy)
        {
            if (enemy == null || !_enemyLookup.TryGetValue(enemy, out EnemyRecord record))
            {
                return;
            }

            record.Died = true;
            record.DeathTime = Time.time;

            SectionRecord section = _data.GetSection(record.SectionIndex);

            if (section != null)
            {
                section.KilledEnemyCount++;
            }

            _liveEnemyPositions.Remove(record.SpawnPosition);

            enemy.Died -= HandleEnemyDied;
            enemy.DamageTaken -= HandleEnemyDamaged;
            enemy.DamagedPlayer -= HandleEnemyDamagedPlayer;
        }

        // ------------------------------------------------------------------ upgrades

        private void HandleRunOrderBuilt(IReadOnlyList<int> runOrder)
        {
            _data.UpgradeRunOrder.Clear();

            if (runOrder == null)
            {
                return;
            }

            for (int i = 0; i < runOrder.Count; i++)
            {
                _data.UpgradeRunOrder.Add(runOrder[i]);
            }
        }

        private void HandlePickupSpawned(
            int orderSlot, int opportunityIndex, UpgradeDefinition definition, Vector3 position)
        {
            Vector3 playerPosition = playerTransform != null ? playerTransform.position : Vector3.zero;

            UpgradeRecord record = new UpgradeRecord
            {
                OrderSlot = orderSlot + 1,
                OpportunityIndex = opportunityIndex,
                UpgradeName = definition != null ? definition.displayName : "UNKNOWN",
                UpgradeKind = definition != null ? definition.kind.ToString() : "UNKNOWN",
                SpawnPosition = position,
                PlayerPositionAtSpawn = playerPosition,
                SpawnTime = Time.time,
                DistanceFromPlayerAtSpawn = DiagnosticRules.PlanarDistance(position, playerPosition),
                DistanceFromPreviousPickup = _hasPreviousPickup
                    ? DiagnosticRules.PlanarDistance(position, _previousPickupPosition)
                    : -1f
            };

            // Milestone 1O-R - snapshot the CURRENT reachable rectangle. The forward limit
            // moves as sections unlock (Milestone 1M), so the run-start bounds are only valid
            // for Section 1. Reading them here means each pickup is judged against the area
            // the player could actually reach when that pickup appeared.
            if (laneBounds != null)
            {
                record.LaneMinX = laneBounds.MinX;
                record.LaneMaxX = laneBounds.MaxX;
                record.LaneMinZ = laneBounds.MinZ;
                record.LaneMaxZ = laneBounds.MaxZ;
                record.LaneBoundsCaptured = true;
            }

            _data.Upgrades.Add(record);
            _activeUpgrade = record;

            _previousPickupPosition = position;
            _hasPreviousPickup = true;
        }

        private void HandlePickupResolved(bool collected, UpgradeDefinition definition)
        {
            if (_activeUpgrade == null)
            {
                return;
            }

            _activeUpgrade.Collected = collected;
            _activeUpgrade.Expired = !collected;
            _activeUpgrade.ResolutionTime = Time.time;

            if (collected && definition != null)
            {
                RecordUpgradeEffect(definition);
            }

            _activeUpgrade = null;
        }

        private void RecordUpgradeEffect(UpgradeDefinition definition)
        {
            switch (definition.kind)
            {
                case UpgradeKind.FireRateMultiplier:
                    _data.Player.FireRateUpgrades++;
                    _data.Player.LogChange(
                        $"t={Time.time.ToString("0.##", CultureInfo.InvariantCulture)}s FIRE RATE " +
                        $"x{definition.multiplier.ToString("0.##", CultureInfo.InvariantCulture)}");
                    break;

                case UpgradeKind.DamageBonus:
                    _data.Player.WeaponDamageUpgrades++;
                    _data.Player.LogChange(
                        $"t={Time.time.ToString("0.##", CultureInfo.InvariantCulture)}s WEAPON DAMAGE " +
                        $"+{definition.amount}");
                    break;

                case UpgradeKind.MaxHealthBonus:
                    _data.Player.MaxHealthUpgrades++;
                    _data.Player.LogChange(
                        $"t={Time.time.ToString("0.##", CultureInfo.InvariantCulture)}s MAX HEALTH " +
                        $"+{definition.amount}");
                    break;

                case UpgradeKind.MoveSpeedMultiplier:
                    _data.Player.MoveSpeedUpgrades++;
                    _data.Player.LogChange(
                        $"t={Time.time.ToString("0.##", CultureInfo.InvariantCulture)}s MOVE SPEED " +
                        $"x{definition.multiplier.ToString("0.##", CultureInfo.InvariantCulture)}");
                    break;
            }
        }

        // ------------------------------------------------------------------ player

        private void HandleHealthChanged(int current, int max)
        {
            _data.Player.FinalMaxHealth = max;
        }

        private void HandlePlayerDied()
        {
            if (_data.Player.Died)
            {
                return;
            }

            _data.Player.Died = true;
            _data.Player.DeathTime = Time.time;
        }

        // ------------------------------------------------------------------ report

        /// <summary>
        /// Builds and prints the run report exactly once. Called from the victory and
        /// defeat overlays, which are the two ways a run can end.
        /// </summary>
        private void EmitReport()
        {
            if (_reported)
            {
                return;
            }

            _reported = true;

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(_data);
            string report = DiagnosticReportBuilder.BuildReport(_data, checks);

            // ONE log call for the whole report so it can be copied in a single selection.
            if (checks.FailedCount > 0)
            {
                Debug.LogWarning(report, this);
            }
            else
            {
                Debug.Log(report, this);
            }
        }

        /// <summary>
        /// Trivial corner marker. Built once, never updated, and deliberately tiny so the
        /// portrait gameplay screen stays uncluttered. No dashboard, by requirement.
        /// </summary>
        private void BuildIndicator()
        {
            GameObject canvasObject = new GameObject(
                "DiagnosticsIndicator",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);

            GameObject labelObject = new GameObject(
                "Label", typeof(RectTransform), typeof(TextMeshProUGUI));

            labelObject.transform.SetParent(canvasObject.transform, false);

            RectTransform rect = (RectTransform)labelObject.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(320f, 40f);
            rect.anchoredPosition = new Vector2(16f, 16f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.text = "DIAGNOSTICS ON";
            label.fontSize = 22f;
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.color = new Color(0.4f, 1f, 0.5f, 0.5f);
            label.raycastTarget = false;
        }
    }
}
