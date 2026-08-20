using System;
using System.Collections.Generic;
using OperationOutbreak.Enemies;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1U - the ONE runtime objective evaluator/completion authority.
    ///
    ///   MissionSectionController  publishes section progress   (SectionCleared)
    ///   MissionObjectiveController  evaluates required objectives + decides completion
    ///   MissionCompleteController   presents the final Mission Complete state (unchanged)
    ///
    /// There is exactly ONE authoritative completion decision (this component): when
    /// every REQUIRED objective is complete it triggers the existing single
    /// presentation path (EnemySpawner.CompleteEncounter -> EncounterCompleted ->
    /// MissionCompleteController). MissionSectionController no longer declares
    /// victory itself - it only publishes progress, so the two systems can never
    /// both declare completion.
    ///
    /// Responsibilities kept narrow: it reads the MissionDefinition objectives,
    /// subscribes to gameplay events, tracks progress and decides completion. It
    /// does NOT spawn enemies, own combat, duplicate the section controller, own
    /// save/progression or manage rewards.
    ///
    /// Fallback policy (fail loud): a missing MissionDefinition, an objective list
    /// with no REQUIRED objective, or a null objective is logged as a loud error and
    /// completion is NEVER triggered - malformed objective data must not silently
    /// complete (nor silently hang): the committed Mission_01 always carries explicit
    /// objective data, so the normal path is fully defined.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionObjectiveController : MonoBehaviour
    {
        [Header("Mission Data (Milestone 1U)")]
        [Tooltip("The data-driven mission whose objectives this controller evaluates. " +
                 "Assign the same committed Mission_01 asset the section controller uses.")]
        [SerializeField] private MissionDefinition missionDefinition;

        [Tooltip("The section-flow controller this observer listens to (SectionCleared).")]
        [SerializeField] private MissionSectionController missionSections;

        [Tooltip("The shared spawner whose CompleteEncounter() is the single victory " +
                 "presentation trigger (raised once all required objectives complete).")]
        [SerializeField] private EnemySpawner enemySpawner;

        [SerializeField] private bool verboseLogging = true;

        private readonly List<MissionObjectiveRuntime> _objectives = new List<MissionObjectiveRuntime>();
        private bool _completionTriggered;

        /// <summary>Read-only runtime progress of every configured objective.</summary>
        public IReadOnlyList<MissionObjectiveRuntime> Objectives => _objectives;

        /// <summary>True when the mission data carries at least one REQUIRED objective.</summary>
        public bool HasRequiredObjective { get; private set; }

        /// <summary>True once every required objective is complete (the completion gate).</summary>
        public bool AreAllRequiredObjectivesComplete =>
            MissionObjectiveRuntime.AllRequiredObjectivesComplete(_objectives);

        /// <summary>Raised the moment one objective completes. Carries the completed runtime.</summary>
        public event Action<MissionObjectiveRuntime> ObjectiveCompleted;

        /// <summary>Raised exactly once when every required objective completes.</summary>
        public event Action AllRequiredObjectivesCompleted;

        private void Awake()
        {
            if (missionSections == null) missionSections = FindAnyObjectByType<MissionSectionController>();
            if (enemySpawner == null) enemySpawner = FindAnyObjectByType<EnemySpawner>();
        }

        private void OnEnable()
        {
            // Instance state only: a scene reload rebuilds every objective fresh.
            _completionTriggered = false;
            _objectives.Clear();
            HasRequiredObjective = false;

            if (missionDefinition == null)
            {
                Debug.LogError(
                    "[1U] No MissionDefinition is assigned to '" + name + "'. Assign the " +
                    "committed Mission_01 asset. No objective can be evaluated and mission " +
                    "completion will NOT be triggered.", this);
                return;
            }

            int sectionCount = missionDefinition.SectionCount;
            IReadOnlyList<MissionObjectiveDefinition> definitions = missionDefinition.Objectives;

            if (definitions == null || definitions.Count == 0)
            {
                Debug.LogError(
                    "[1U] Mission '" + missionDefinition.name + "' declares NO objectives. " +
                    "Mission completion will NOT be triggered - author a required objective " +
                    "in the MissionDefinition asset.", this);
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
                    "Mission completion will NOT be triggered - mark at least one objective " +
                    "required in the MissionDefinition asset.", this);
                return;
            }

            if (missionSections != null)
            {
                missionSections.SectionCleared += HandleSectionCleared;
            }

            if (verboseLogging)
            {
                Debug.Log(
                    "[1U] Objective runtime loaded for mission '" + missionDefinition.name +
                    "': " + _objectives.Count + " objective(s), " +
                    CountRequired() + " required.", this);
            }
        }

        private void OnDisable()
        {
            if (missionSections != null)
            {
                missionSections.SectionCleared -= HandleSectionCleared;
            }
        }

        private void HandleSectionCleared(int index, MissionDefinition.MissionSection section)
        {
            // The 'section' argument is the MissionSectionController.SectionCleared
            // payload (Action<int, MissionDefinition.MissionSection>); only the index
            // is needed to record progress against the section-indexed ClearAllSections
            // objective. The payload is accepted verbatim so the handler matches the
            // event delegate exactly - no adapter, no signature change.
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

                objective.RecordSectionCleared(index);

                if (objective.IsComplete)
                {
                    Debug.Log(
                        "[1U] Objective '" + objective.ObjectiveId + "' completed (" +
                        objective.CurrentProgress + "/" + objective.RequiredProgress + ").",
                        this);
                    ObjectiveCompleted?.Invoke(objective);
                }
            }

            EvaluateRequiredObjectives();
        }

        /// <summary>
        /// The single completion gate: when every required objective is complete,
        /// triggers the existing victory presentation path exactly once.
        /// </summary>
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

            // Single victory path: Mission Complete UI already listens to
            // EnemySpawner.EncounterCompleted, raised by CompleteEncounter().
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
