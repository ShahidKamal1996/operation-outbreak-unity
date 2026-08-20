using System.Collections.Generic;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1U - RUNTIME progress of one mission objective. Deliberately NOT a
    /// Unity asset and NOT serialized: it is plain state created fresh for each
    /// scene run, so MissionDefinition stays static configuration.
    ///
    /// The runtime is the SINGLE place objective evaluation lives. It understands
    /// the objective types (currently ClearAllSections) and translates gameplay
    /// events (section cleared) into progress, so no switch statement is ever
    /// spread across gameplay code. The MissionObjectiveController (MonoBehaviour)
    /// feeds it events; it never polls the scene.
    ///
    /// ClearAllSections semantics:
    ///   RequiredProgress = the mission's configured section count (derived, never
    ///   stored), CurrentProgress = distinct cleared sections, complete exactly
    ///   when every section has cleared - never earlier, never twice for one section.
    /// </summary>
    public sealed class MissionObjectiveRuntime
    {
        private readonly bool[] _cleared;

        /// <summary>The static definition this runtime tracks. Never null.</summary>
        public MissionObjectiveDefinition Definition { get; }

        /// <summary>The required progress for this objective (section count for ClearAllSections).</summary>
        public int RequiredProgress { get; }

        /// <summary>How much progress has been recorded so far (distinct sections cleared).</summary>
        public int CurrentProgress { get; private set; }

        /// <summary>True once CurrentProgress has reached RequiredProgress.</summary>
        public bool IsComplete { get; private set; }

        public string ObjectiveId => Definition != null ? Definition.objectiveId : string.Empty;
        public string Title => Definition != null ? Definition.title : string.Empty;
        public MissionObjectiveType Type => Definition != null ? Definition.objectiveType : MissionObjectiveType.ClearAllSections;
        public bool Required => Definition != null && Definition.required;

        /// <summary>0..1 progress; 0 until complete, 1 when complete.</summary>
        public float NormalizedProgress
        {
            get
            {
                if (RequiredProgress <= 0)
                {
                    return IsComplete ? 1f : 0f;
                }

                return Mathf.Clamp01((float)CurrentProgress / RequiredProgress);
            }
        }

        public MissionObjectiveRuntime(MissionObjectiveDefinition definition, int requiredProgress)
        {
            Definition = definition;
            RequiredProgress = Mathf.Max(0, requiredProgress);
            _cleared = new bool[Mathf.Max(0, requiredProgress)];
        }

        /// <summary>
        /// Records one cleared section. A section only ever counts once, and only
        /// ClearAllSections objectives respond to section clears - a future type
        /// adds its own event adapters here, not in gameplay code.
        /// </summary>
        public void RecordSectionCleared(int sectionIndex)
        {
            if (IsComplete)
            {
                return;
            }

            if (Type != MissionObjectiveType.ClearAllSections)
            {
                // Unsupported runtime type: validation rejects these in the editor;
                // the runtime simply refuses to progress them.
                return;
            }

            if (RequiredProgress <= 0 || sectionIndex < 0 || sectionIndex >= _cleared.Length)
            {
                return;
            }

            if (_cleared[sectionIndex])
            {
                return;
            }

            _cleared[sectionIndex] = true;
            CurrentProgress++;

            if (CurrentProgress >= RequiredProgress)
            {
                IsComplete = true;
            }
        }

        /// <summary>
        /// True only when at least ONE required objective exists and EVERY required
        /// objective is complete. Optional objectives never gate completion, and a
        /// mission with no required objective never completes - the fail-loud
        /// contract, not a silent victory.
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
