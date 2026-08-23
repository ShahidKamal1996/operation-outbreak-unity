using System;
using System.Collections.Generic;
using OperationOutbreak.Enemies;
using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1U/1X.5 - the ONE runtime objective evaluator/completion authority.
    ///
    ///   MissionSectionController / target events  publish progress
    ///   MissionObjectiveController                evaluates required objectives + decides completion
    ///   MissionCompleteController                 presents the final Mission Complete state (unchanged)
    ///
    /// There is exactly ONE authoritative completion decision (this component): when every REQUIRED
    /// objective is complete it triggers the existing single presentation path
    /// (EnemySpawner.CompleteEncounter -> EncounterCompleted -> MissionCompleteController). No second
    /// victory path exists. Completion evaluation is DEFERRED to LateUpdate (1U QA fix #2).
    ///
    /// 1X.5 adds three objective behaviours without changing that authority:
    ///   * SurviveDuration - this controller advances the timer each Update, only while the
    ///     objective is active and the player is alive, then completes it at the configured time.
    ///   * DestroyTargets / ActivateTargets - the controller routes MissionObjectiveTargetEvents
    ///     (raised by world-space barricade/activation targets) into the matching runtime.
    ///   * Sequencing - objectives with an 'activateAfterObjectiveId' stay inactive until their
    ///     prerequisite completes (Mission 5's clear -> activate -> defend chain); SurviveDuration
    ///     additionally waits for the final section to start (the player reaches the hold point).
    /// ObjectiveActivated is raised when an objective transitions to active so world-space directors
    /// can enable the matching targets at the right stage.
    ///
    /// Fallback policy (fail loud): a missing MissionDefinition, no REQUIRED objective, or a null
    /// objective is logged loudly and completion is NEVER triggered.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionObjectiveController : MonoBehaviour
    {
        [Header("Mission Data (Milestone 1U)")]
        [Tooltip("The data-driven mission whose objectives this controller evaluates.")]
        [SerializeField] private MissionDefinition missionDefinition;

        [Tooltip("The section-flow controller (SectionCleared + SectionStarted).")]
        [SerializeField] private MissionSectionController missionSections;

        [Tooltip("The shared spawner whose CompleteEncounter() is the single victory trigger.")]
        [SerializeField] private EnemySpawner enemySpawner;

        [Tooltip("1X.5 - player health, used to freeze the survival timer on death so a death " +
                 "can never produce mission success.")]
        [SerializeField] private PlayerHealth playerHealth;

        [SerializeField] private bool verboseLogging = true;

        private readonly List<MissionObjectiveRuntime> _objectives = new List<MissionObjectiveRuntime>();
        private bool _completionTriggered;
        private bool _evaluationPending;
        private bool _playerDead;
        private bool _finalSectionStarted;

        /// <summary>Read-only runtime progress of every configured objective.</summary>
        public IReadOnlyList<MissionObjectiveRuntime> Objectives => _objectives;

        public bool HasRequiredObjective { get; private set; }

        public bool AreAllRequiredObjectivesComplete =>
            MissionObjectiveRuntime.AllRequiredObjectivesComplete(_objectives);

        /// <summary>Raised the moment one objective completes. Carries the completed runtime.</summary>
        public event Action<MissionObjectiveRuntime> ObjectiveCompleted;

        /// <summary>Raised exactly once when every required objective completes.</summary>
        public event Action AllRequiredObjectivesCompleted;

        /// <summary>
        /// 1X.5 - raised when an objective transitions from inactive to active (its stage begins),
        /// so world-space directors can enable the matching targets at the right time. Carries the
        /// activated runtime.
        /// </summary>
        public event Action<MissionObjectiveRuntime> ObjectiveActivated;

        /// <summary>1X - overrides the serialized mission with the selected mission.</summary>
        public void AssignActiveMission(MissionDefinition definition)
        {
            if (definition != null)
            {
                missionDefinition = definition;
            }
        }

        private void Awake()
        {
            if (missionSections == null) missionSections = FindAnyObjectByType<MissionSectionController>();
            if (enemySpawner == null) enemySpawner = FindAnyObjectByType<EnemySpawner>();
            if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
        }

        private void OnEnable()
        {
            _completionTriggered = false;
            _evaluationPending = false;
            _playerDead = false;
            _finalSectionStarted = false;
            _objectives.Clear();
            HasRequiredObjective = false;

            if (missionDefinition == null)
            {
                Debug.LogError(
                    "[1U] No MissionDefinition is assigned to '" + name + "'. Mission completion " +
                    "will NOT be triggered.", this);
                return;
            }

            int sectionCount = missionDefinition.SectionCount;
            IReadOnlyList<MissionObjectiveDefinition> definitions = missionDefinition.Objectives;

            if (definitions == null || definitions.Count == 0)
            {
                Debug.LogError(
                    "[1U] Mission '" + missionDefinition.name + "' declares NO objectives. " +
                    "Mission completion will NOT be triggered.", this);
                return;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                MissionObjectiveDefinition definition = definitions[i];
                if (definition == null)
                {
                    Debug.LogError(
                        "[1U] Mission '" + missionDefinition.name + "' objective entry " +
                        (i + 1) + " is null - it is skipped.", this);
                    continue;
                }

                if (definition.required)
                {
                    HasRequiredObjective = true;
                }

                _objectives.Add(new MissionObjectiveRuntime(definition, sectionCount));
            }

            if (!HasRequiredObjective)
            {
                Debug.LogError(
                    "[1U] Mission '" + missionDefinition.name + "' has no REQUIRED objective. " +
                    "Mission completion will NOT be triggered.", this);
                return;
            }

            if (missionSections != null)
            {
                missionSections.SectionCleared += HandleSectionCleared;
                missionSections.SectionStarted += HandleSectionStarted;
            }

            if (playerHealth != null)
            {
                playerHealth.Died += HandlePlayerDied;
                _playerDead = playerHealth.IsDead;
            }

            MissionObjectiveTargetEvents.TargetDestroyed += HandleTargetDestroyed;
            MissionObjectiveTargetEvents.TargetActivated += HandleTargetActivated;

            // Every objective starts inactive; activate those whose conditions are already met
            // (ClearAllSections / DestroyTargets with no prerequisite). Gated ones (SurviveDuration
            // until the hold section, prerequisite-chained ones) stay inactive until their stage.
            RefreshActivations();

            if (verboseLogging)
            {
                Debug.Log(
                    "[1U] Objective runtime loaded for mission '" + missionDefinition.name +
                    "': " + _objectives.Count + " objective(s), " + CountRequired() + " required.",
                    this);
            }
        }

        private void OnDisable()
        {
            if (missionSections != null)
            {
                missionSections.SectionCleared -= HandleSectionCleared;
                missionSections.SectionStarted -= HandleSectionStarted;
            }

            if (playerHealth != null)
            {
                playerHealth.Died -= HandlePlayerDied;
            }

            MissionObjectiveTargetEvents.TargetDestroyed -= HandleTargetDestroyed;
            MissionObjectiveTargetEvents.TargetActivated -= HandleTargetActivated;
        }

        private void HandleSectionStarted(int index, MissionDefinition.MissionSection section)
        {
            if (missionDefinition != null && index == missionDefinition.SectionCount - 1)
            {
                _finalSectionStarted = true;
                RefreshActivations();
            }
        }

        private void HandleSectionCleared(int index, MissionDefinition.MissionSection section)
        {
            if (_completionTriggered)
            {
                return;
            }

            for (int i = 0; i < _objectives.Count; i++)
            {
                MissionObjectiveRuntime objective = _objectives[i];
                if (objective == null || objective.IsComplete)
                {
                    continue;
                }

                if (objective.RecordSectionCleared(index))
                {
                    OnObjectiveCompleted(objective);
                }
            }

            _evaluationPending = true;
        }

        private void HandleTargetDestroyed(string targetId)
        {
            RouteTargetEvent(targetId, MissionObjectiveType.DestroyTargets, (o, id) => o.RecordTargetDestroyed(id));
        }

        private void HandleTargetActivated(string targetId)
        {
            RouteTargetEvent(targetId, MissionObjectiveType.ActivateTargets, (o, id) => o.RecordTargetActivated(id));
        }

        private void RouteTargetEvent(string targetId, MissionObjectiveType type,
            Func<MissionObjectiveRuntime, string, bool> record)
        {
            if (_completionTriggered)
            {
                return;
            }

            for (int i = 0; i < _objectives.Count; i++)
            {
                MissionObjectiveRuntime objective = _objectives[i];
                if (objective == null || objective.IsComplete || objective.Type != type)
                {
                    continue;
                }

                if (record(objective, targetId))
                {
                    OnObjectiveCompleted(objective);
                }
            }

            _evaluationPending = true;
        }

        private void Update()
        {
            if (_completionTriggered || _playerDead)
            {
                return;
            }

            bool anySurvival = false;

            for (int i = 0; i < _objectives.Count; i++)
            {
                MissionObjectiveRuntime objective = _objectives[i];
                if (objective == null || objective.Type != MissionObjectiveType.SurviveDuration)
                {
                    continue;
                }

                anySurvival = true;

                if (objective.IsComplete)
                {
                    continue;
                }

                if (objective.RecordSurviveTick(Time.deltaTime))
                {
                    OnObjectiveCompleted(objective);
                }
            }

            if (anySurvival)
            {
                // Survival progress changes every frame; re-evaluate completion at the deferred boundary.
                _evaluationPending = true;
            }
        }

        private void HandlePlayerDied()
        {
            _playerDead = true;
        }

        private void OnObjectiveCompleted(MissionObjectiveRuntime objective)
        {
            Debug.Log(
                "[1U] Objective '" + objective.ObjectiveId + "' completed.", this);
            ObjectiveCompleted?.Invoke(objective);

            // A completed objective may unlock a dependent (prerequisite-chained) stage.
            RefreshActivations();
        }

        /// <summary>
        /// Activates every objective whose activation conditions are now met (prerequisite complete
        /// and, for SurviveDuration, the final section started). Raises ObjectiveActivated for each
        /// newly activated objective so world-space directors can enable the matching targets.
        /// </summary>
        private void RefreshActivations()
        {
            for (int i = 0; i < _objectives.Count; i++)
            {
                MissionObjectiveRuntime objective = _objectives[i];
                if (objective == null || objective.IsActive || objective.IsComplete)
                {
                    continue;
                }

                if (!CanActivate(objective))
                {
                    objective.Deactivate();
                    continue;
                }

                objective.Activate();
                ObjectiveActivated?.Invoke(objective);
            }
        }

        private bool CanActivate(MissionObjectiveRuntime objective)
        {
            string prereq = objective.PrerequisiteObjectiveId;
            if (!string.IsNullOrEmpty(prereq))
            {
                MissionObjectiveRuntime prerequisite = FindObjective(prereq);
                if (prerequisite == null || !prerequisite.IsComplete)
                {
                    return false;
                }
            }

            if (objective.Type == MissionObjectiveType.SurviveDuration && !_finalSectionStarted)
            {
                return false;
            }

            return true;
        }

        private MissionObjectiveRuntime FindObjective(string id)
        {
            for (int i = 0; i < _objectives.Count; i++)
            {
                if (_objectives[i] != null && _objectives[i].ObjectiveId == id)
                {
                    return _objectives[i];
                }
            }

            return null;
        }

        private void LateUpdate()
        {
            if (!_evaluationPending)
            {
                return;
            }

            _evaluationPending = false;
            EvaluateRequiredObjectives();
        }

        private void EvaluateRequiredObjectives()
        {
            if (_completionTriggered || !HasRequiredObjective)
            {
                return;
            }

            if (!MissionObjectiveRuntime.AllRequiredObjectivesComplete(_objectives))
            {
                return;
            }

            _completionTriggered = true;

            Debug.Log("[1U] All required objectives complete - mission completion triggered.", this);
            AllRequiredObjectivesCompleted?.Invoke();

            if (enemySpawner != null)
            {
                enemySpawner.CompleteEncounter();
            }
        }

        private int CountRequired()
        {
            int required = 0;
            for (int i = 0; i < _objectives.Count; i++)
            {
                if (_objectives[i] != null && _objectives[i].Required)
                {
                    required++;
                }
            }

            return required;
        }
    }
}
