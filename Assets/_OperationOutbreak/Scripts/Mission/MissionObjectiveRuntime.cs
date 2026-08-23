using System.Collections.Generic;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1U/1X.5 - RUNTIME progress of one mission objective. Deliberately NOT a
    /// Unity asset and NOT serialized: it is plain state created fresh for each scene run, so
    /// MissionDefinition stays static configuration.
    ///
    /// The runtime is the SINGLE place objective evaluation lives. It understands every
    /// objective type and translates gameplay events (section cleared, survive tick, target
    /// destroyed, target activated) into progress, so no switch statement is ever spread
    /// across gameplay code. MissionObjectiveController (MonoBehaviour) feeds it events; it
    /// never polls the scene.
    ///
    /// Type semantics:
    ///   ClearAllSections - RequiredProgress = section count (passed in); a section counts
    ///     once; complete when all sections cleared.
    ///   SurviveDuration  - RequiredDuration = durationSeconds; the timer advances only while
    ///     the objective is active (RecordSurviveTick); complete when elapsed >= duration.
    ///   DestroyTargets   - RequiredProgress = requiredTargetCount; a destroyed target id
    ///     counts once; complete when enough distinct targets destroyed.
    ///   ActivateTargets  - RequiredProgress = requiredTargetCount; an activated target id
    ///     counts once; complete when enough distinct targets activated.
    ///
    /// Activation gating: IsActive starts true. The controller deactivates objectives that
    /// must wait (SurviveDuration until its hold section; any objective with a prerequisite)
    /// and activates them when their condition is met. Record calls are no-ops while inactive,
    /// so a survival timer cannot run early and an activation point cannot be triggered before
    /// its stage - the non-negotiable "kills alone cannot complete it" rules.
    /// </summary>
    public sealed class MissionObjectiveRuntime
    {
        private readonly bool[] _cleared;
        private readonly HashSet<string> _completedTargetIds = new HashSet<string>();
        private float _elapsedSeconds;

        /// <summary>The static definition this runtime tracks. Never null.</summary>
        public MissionObjectiveDefinition Definition { get; }

        /// <summary>
        /// Count-based required progress: section count for ClearAllSections, required target
        /// count for DestroyTargets/ActivateTargets, 0 for SurviveDuration (use RequiredDuration).
        /// </summary>
        public int RequiredProgress { get; }

        /// <summary>Time-based required progress for SurviveDuration (0 for other types).</summary>
        public float RequiredDuration { get; }

        /// <summary>Count-based progress so far (sections cleared / targets destroyed or activated).</summary>
        public int CurrentProgress { get; private set; }

        /// <summary>Elapsed survival seconds (SurviveDuration only).</summary>
        public float ElapsedSeconds => _elapsedSeconds;

        /// <summary>True once the objective's completion condition is met.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>
        /// True while this objective accepts progress. Starts true; the controller sets it false
        /// for gated objectives (SurviveDuration / prerequisite-chained) until their condition met.
        /// </summary>
        public bool IsActive { get; private set; }

        public string ObjectiveId => Definition != null ? Definition.objectiveId : string.Empty;
        public string Title => Definition != null ? Definition.title : string.Empty;
        public MissionObjectiveType Type => Definition != null ? Definition.objectiveType : MissionObjectiveType.ClearAllSections;
        public bool Required => Definition != null && Definition.required;
        public string PrerequisiteObjectiveId => Definition != null ? Definition.activateAfterObjectiveId : string.Empty;

        /// <summary>0..1 progress for display, type-aware.</summary>
        public float NormalizedProgress
        {
            get
            {
                if (IsComplete)
                {
                    return 1f;
                }

                switch (Type)
                {
                    case MissionObjectiveType.SurviveDuration:
                        return RequiredDuration > 0f ? Mathf.Clamp01(_elapsedSeconds / RequiredDuration) : 0f;
                    default:
                        return RequiredProgress > 0 ? Mathf.Clamp01((float)CurrentProgress / RequiredProgress) : 0f;
                }
            }
        }

        public MissionObjectiveRuntime(MissionObjectiveDefinition definition, int sectionCount)
        {
            Definition = definition;
            IsActive = true;

            switch (Type)
            {
                case MissionObjectiveType.SurviveDuration:
                    RequiredDuration = Mathf.Max(0f, definition != null ? definition.durationSeconds : 0f);
                    RequiredProgress = 0;
                    _cleared = null;
                    break;
                case MissionObjectiveType.DestroyTargets:
                case MissionObjectiveType.ActivateTargets:
                    RequiredProgress = Mathf.Max(0, definition != null ? definition.requiredTargetCount : 0);
                    RequiredDuration = 0f;
                    _cleared = null;
                    break;
                default: // ClearAllSections
                    RequiredProgress = Mathf.Max(0, sectionCount);
                    RequiredDuration = 0f;
                    _cleared = new bool[Mathf.Max(0, RequiredProgress)];
                    break;
            }
        }

        /// <summary>Activates the objective (accepts progress). Idempotent.</summary>
        public void Activate()
        {
            IsActive = true;
        }

        /// <summary>Deactivates the objective (progress paused) until Activate is called.</summary>
        public void Deactivate()
        {
            IsActive = false;
        }

        /// <summary>
        /// Records one cleared section (ClearAllSections only). A section counts once. Returns
        /// true if this call newly COMPLETED the objective.
        /// </summary>
        public bool RecordSectionCleared(int sectionIndex)
        {
            if (IsComplete || !IsActive || Type != MissionObjectiveType.ClearAllSections)
            {
                return false;
            }

            if (RequiredProgress <= 0 || _cleared == null || sectionIndex < 0 || sectionIndex >= _cleared.Length)
            {
                return false;
            }

            if (_cleared[sectionIndex])
            {
                return false;
            }

            _cleared[sectionIndex] = true;
            CurrentProgress++;

            return CheckComplete();
        }

        /// <summary>
        /// Advances the survival timer (SurviveDuration only, only while active). Returns true
        /// if this call newly COMPLETED the objective.
        /// </summary>
        public bool RecordSurviveTick(float deltaSeconds)
        {
            if (IsComplete || !IsActive || Type != MissionObjectiveType.SurviveDuration || RequiredDuration <= 0f)
            {
                return false;
            }

            if (deltaSeconds <= 0f)
            {
                return false;
            }

            _elapsedSeconds += deltaSeconds;

            return CheckComplete();
        }

        /// <summary>
        /// Records one destroyed target (DestroyTargets only). A target id counts once. Returns
        /// true if this call newly COMPLETED the objective.
        /// </summary>
        public bool RecordTargetDestroyed(string targetId)
        {
            return RecordTarget(targetId, MissionObjectiveType.DestroyTargets);
        }

        /// <summary>
        /// Records one activated target (ActivateTargets only). A target id counts once. Returns
        /// true if this call newly COMPLETED the objective.
        /// </summary>
        public bool RecordTargetActivated(string targetId)
        {
            return RecordTarget(targetId, MissionObjectiveType.ActivateTargets);
        }

        private bool RecordTarget(string targetId, MissionObjectiveType expectedType)
        {
            if (IsComplete || !IsActive || Type != expectedType || RequiredProgress <= 0)
            {
                return false;
            }

            if (string.IsNullOrEmpty(targetId) || !_completedTargetIds.Add(targetId))
            {
                return false;
            }

            CurrentProgress++;

            return CheckComplete();
        }

        private bool CheckComplete()
        {
            if (IsComplete)
            {
                return false;
            }

            bool done = Type == MissionObjectiveType.SurviveDuration
                ? _elapsedSeconds >= RequiredDuration
                : CurrentProgress >= RequiredProgress;

            if (done)
            {
                IsComplete = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// True only when at least ONE required objective exists and EVERY required objective
        /// is complete. Optional objectives never gate completion, and a mission with no
        /// required objective never completes - the fail-loud contract, not a silent victory.
        /// </summary>
        public static bool AllRequiredObjectivesComplete(IReadOnlyList<MissionObjectiveRuntime> objectives)
        {
            if (objectives == null)
            {
                return false;
            }

            bool anyRequired = false;

            for (int i = 0; i < objectives.Count; i++)
            {
                MissionObjectiveRuntime objective = objectives[i];

                if (objective == null || !objective.Required)
                {
                    continue;
                }

                anyRequired = true;

                if (!objective.IsComplete)
                {
                    return false;
                }
            }

            return anyRequired;
        }
    }
}
