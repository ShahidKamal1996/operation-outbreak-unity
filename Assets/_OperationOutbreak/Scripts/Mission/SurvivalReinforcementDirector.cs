using OperationOutbreak.Enemies;
using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X.5 - keeps enemy pressure during a SurviveDuration hold phase so the player is
    /// actually SURVIVING, not waiting out an empty timer. While a SurviveDuration objective is the
    /// active stage and the player is alive, it periodically spawns a Basic Infected ahead of the
    /// player through the existing 1S spawner seam (EnemySpawner.SpawnEnemyWithDefinition), which
    /// reuses all the existing chase/target/auto-aim/death plumbing - no parallel spawn system.
    ///
    /// Spawn cadence and the ahead-offset are authored fields. The director never owns completion
    /// (the survival TIMER is the gate) and never spawns when the player is dead, so a death can
    /// never produce a mission success. Enemies spawned here are tracked by the spawner exactly
    /// like section enemies; they simply do not trigger SectionCleared (only BeginSection does).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurvivalReinforcementDirector : MonoBehaviour
    {
        [Header("References (auto-resolved if empty)")]
        [SerializeField] private EnemySpawner enemySpawner;
        [SerializeField] private MissionObjectiveController objectiveController;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private Transform playerTransform;

        [Header("Reinforcement pacing")]
        [Tooltip("Seconds between reinforcement spawns while the survival phase is active.")]
        [Min(0.5f)] [SerializeField] private float spawnInterval = 2.5f;

        [Tooltip("How far ahead of the player (world +Z) reinforcements appear.")]
        [Min(2f)] [SerializeField] private float spawnAheadOffset = 11f;

        [Tooltip("Archetype id to spawn as reinforcements.")]
        [SerializeField] private string archetypeId = EnemyArchetypeRegistry.DefaultArchetypeId;

        private float _nextSpawnTime;
        private bool _survivalActive;

        private void Awake()
        {
            if (enemySpawner == null) enemySpawner = FindAnyObjectByType<EnemySpawner>();
            if (objectiveController == null) objectiveController = FindAnyObjectByType<MissionObjectiveController>();
            if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
            if (playerTransform == null)
            {
                PlayerController player = FindAnyObjectByType<PlayerController>();
                if (player != null) playerTransform = player.transform;
            }
        }

        private void OnEnable()
        {
            _nextSpawnTime = Time.time + Mathf.Max(1f, spawnInterval);
            RefreshSurvivalState();

            if (objectiveController != null)
            {
                objectiveController.ObjectiveActivated += HandleObjectiveActivated;
            }
        }

        private void OnDisable()
        {
            if (objectiveController != null)
            {
                objectiveController.ObjectiveActivated -= HandleObjectiveActivated;
            }

            _survivalActive = false;
        }

        private void HandleObjectiveActivated(MissionObjectiveRuntime runtime)
        {
            RefreshSurvivalState();
        }

        private void Update()
        {
            if (!_survivalActive || enemySpawner == null || playerHealth == null || playerHealth.IsDead)
            {
                return;
            }

            if (Time.time < _nextSpawnTime)
            {
                return;
            }

            _nextSpawnTime = Time.time + spawnInterval;
            SpawnReinforcement();
        }

        private void RefreshSurvivalState()
        {
            _survivalActive = objectiveController != null && IsSurvivalActive();
        }

        private bool IsSurvivalActive()
        {
            System.Collections.Generic.IReadOnlyList<MissionObjectiveRuntime> objectives =
                objectiveController.Objectives;

            for (int i = 0; i < objectives.Count; i++)
            {
                MissionObjectiveRuntime objective = objectives[i];
                if (objective != null
                    && objective.Type == MissionObjectiveType.SurviveDuration
                    && objective.IsActive
                    && !objective.IsComplete)
                {
                    return true;
                }
            }

            return false;
        }

        private void SpawnReinforcement()
        {
            if (playerTransform == null)
            {
                return;
            }

            EnemyArchetypeDefinition definition =
                EnemyArchetypeRegistry.ResolveRequestedArchetype(archetypeId);

            Vector3 position = new Vector3(
                0f,
                1f,
                Mathf.Min(playerTransform.position.z + spawnAheadOffset, 56f));

            enemySpawner.SpawnEnemyWithDefinition(definition, position);
        }
    }
}
