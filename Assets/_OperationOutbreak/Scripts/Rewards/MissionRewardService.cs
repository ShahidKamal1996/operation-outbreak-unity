using System;
using OperationOutbreak.Enemies;
using OperationOutbreak.Mission;
using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Rewards
{
    /// <summary>
    /// Milestone 1V - the ONE reusable reward authority.
    ///
    ///   MissionDefinition   static reward configuration (Coins / Supplies)
    ///   MissionObjectiveController   objective completion authority (UNCHANGED)
    ///   MissionRewardService   calculates + grants the reward EXACTLY ONCE per run
    ///   RuntimeWallet   runtime currency balances (owned by this service)
    ///   MissionResultData   immutable summary of one outcome
    ///   MissionCompleteController / GameOverController   presentation (extended)
    ///   MissionResultNavigation   Retry / Next / Return requests
    ///
    /// This service NEVER decides whether the mission was completed - it is driven by
    /// the authoritative outcome events (EnemySpawner.EncounterCompleted for success,
    /// PlayerHealth.Died for failure) and it never declares victory, spawns enemies,
    /// owns objective progress, shows UI directly or saves permanent progression.
    ///
    /// DUPLICATE-GRANT CONTRACT (run-scoped latch): a single run grants at most once.
    /// The latch is plain instance state reset in OnEnable (a scene reload = a NEW run
    /// with a NEW grant identity). This is NOT persistent first-completion protection -
    /// save-backed duplicate protection belongs to Milestone 2C. Documented here.
    ///
    /// 2C SAVESERVICE SEAM: `RewardGranted` carries the result (and the wallet carries
    /// the balances); a future SaveService subscribes to persist that output. Nothing
    /// here writes permanent data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionRewardService : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The mission whose reward configuration this service reads.")]
        [SerializeField] private MissionDefinition missionDefinition;

        [Tooltip("The spawner whose EncounterCompleted event is the authoritative success signal.")]
        [SerializeField] private EnemySpawner enemySpawner;

        [Tooltip("The player health whose Died event is the authoritative failure signal.")]
        [SerializeField] private PlayerHealth playerHealth;

        [Tooltip("The section controller whose SectionCleared event provides the cleared count.")]
        [SerializeField] private MissionSectionController missionSections;

        [SerializeField] private bool verboseLogging = true;

        private bool _resultCreated;
        private int _sectionsCompleted;

        /// <summary>The runtime currency wallet this service grants into (session only).</summary>
        public RuntimeWallet Wallet { get; } = new RuntimeWallet();

        /// <summary>The most recent run's result, or null before the first outcome.</summary>
        public MissionResultData CurrentResult { get; private set; }

        /// <summary>True once the current run has granted its reward (success) or resolved (failure).</summary>
        public bool HasResult => _resultCreated;

        /// <summary>True when the current run already GRANTED its success reward.</summary>
        public bool RewardGrantedThisRun { get; private set; }

        /// <summary>Raised once per outcome (success or failure), carrying the immutable result.</summary>
        public event Action<MissionResultData> ResultCreated;

        /// <summary>Raised exactly once per run when a SUCCESS reward is granted (2C SaveService seam).</summary>
        public event Action<MissionResultData> RewardGranted;

        private void Awake()
        {
            if (enemySpawner == null) enemySpawner = FindAnyObjectByType<EnemySpawner>();
            if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
            if (missionSections == null) missionSections = FindAnyObjectByType<MissionSectionController>();
        }

        private void OnEnable()
        {
            // A scene reload is a NEW run: the grant identity and result are cleared so
            // the new run is eligible for its own reward exactly once.
            _resultCreated = false;
            _sectionsCompleted = 0;
            RewardGrantedThisRun = false;
            CurrentResult = null;

            if (enemySpawner != null) enemySpawner.EncounterCompleted += HandleEncounterCompleted;
            if (playerHealth != null) playerHealth.Died += HandlePlayerDied;
            if (missionSections != null) missionSections.SectionCleared += HandleSectionCleared;
        }

        private void OnDisable()
        {
            if (enemySpawner != null) enemySpawner.EncounterCompleted -= HandleEncounterCompleted;
            if (playerHealth != null) playerHealth.Died -= HandlePlayerDied;
            if (missionSections != null) missionSections.SectionCleared -= HandleSectionCleared;
        }

        private void HandleSectionCleared(int index, MissionDefinition.MissionSection section)
        {
            // Counting only - the grant is driven by the authoritative outcome event,
            // never by section progress. A section only ever counts once per run.
            if (!_resultCreated)
            {
                _sectionsCompleted++;
            }
        }

        private void HandleEncounterCompleted()
        {
            TryProcessSuccess();
        }

        private void HandlePlayerDied()
        {
            TryProcessFailure();
        }

        private void TryProcessSuccess()
        {
            if (_resultCreated)
            {
                if (verboseLogging)
                {
                    Debug.Log("[1V] Reward already granted for this run - ignored.", this);
                }

                return;
            }

            _resultCreated = true;

            MissionRewardDefinition reward = missionDefinition != null ? missionDefinition.Reward : null;
            int coins = reward != null ? Mathf.Max(0, reward.coins) : 0;
            int supplies = reward != null ? Mathf.Max(0, reward.supplies) : 0;

            Wallet.Grant(coins, supplies);
            RewardGrantedThisRun = true;

            string missionId = missionDefinition != null ? missionDefinition.MissionId : string.Empty;
            int missionNumber = missionDefinition != null ? missionDefinition.MissionNumber : 0;
            int totalSections = missionDefinition != null ? missionDefinition.SectionCount : 0;

            CurrentResult = MissionResultData.ForSuccess(
                missionId, missionNumber, coins, supplies, _sectionsCompleted, totalSections);

            if (verboseLogging)
            {
                Debug.Log(
                    "[1V] Result created - " + missionId + " SUCCESS, " +
                    _sectionsCompleted + "/" + totalSections + " sections, " +
                    "Coins=" + coins + " Supplies=" + supplies + ".", this);
                Debug.Log("[1V] Reward granted: Coins=" + coins + " Supplies=" + supplies + ".", this);
            }

            ResultCreated?.Invoke(CurrentResult);
            RewardGranted?.Invoke(CurrentResult);
        }

        private void TryProcessFailure()
        {
            // A failure grants NOTHING, and a success that already resolved can never be
            // overwritten by a late death (the two outcomes stay exclusive).
            if (_resultCreated)
            {
                return;
            }

            _resultCreated = true;

            string missionId = missionDefinition != null ? missionDefinition.MissionId : string.Empty;
            int missionNumber = missionDefinition != null ? missionDefinition.MissionNumber : 0;
            int totalSections = missionDefinition != null ? missionDefinition.SectionCount : 0;

            CurrentResult = MissionResultData.ForFailure(
                missionId, missionNumber, _sectionsCompleted, totalSections);

            if (verboseLogging)
            {
                Debug.Log(
                    "[1V] Result created - " + missionId + " FAILED, " +
                    _sectionsCompleted + "/" + totalSections + " sections, no reward granted.", this);
            }

            ResultCreated?.Invoke(CurrentResult);
        }
    }
}
