using System;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1U - the reusable objective types a mission can declare.
    ///
    /// Only the FIRST production type is implemented for 1U; later milestones add
    /// more entries (defeat N enemies, survive N seconds, reach destination,
    /// escort/protect/destroy, boss, collect, multi-stage) WITHOUT changing the
    /// mission-flow or evaluation architecture - they only extend this enum and
    /// the runtime's centralized evaluation.
    /// </summary>
    public enum MissionObjectiveType
    {
        /// <summary>
        /// Clear every configured mission section (equivalently: defeat all mission
        /// enemies). Required progress is DERIVED from the mission's section count,
        /// so the objective always stays in sync with the mission structure.
        /// </summary>
        ClearAllSections = 0
    }

    /// <summary>
    /// Milestone 1U - one data-driven objective of a mission. PURE DATA, like the
    /// rest of MissionDefinition: it declares WHAT must happen (identity, type,
    /// required/optional) and never holds runtime progress.
    ///
    /// A simple serializable tagged-data model is used deliberately - no class
    /// hierarchy per objective type. Future types add their own serialized
    /// parameter fields to this class (or extend the validation contract); the
    /// runtime evaluator is the single place that interprets the type.
    /// </summary>
    [Serializable]
    public sealed class MissionObjectiveDefinition
    {
        [UnityEngine.Tooltip("STABLE objective id, e.g. 'clear_all_sections'. Unique within the mission.")]
        public string objectiveId = string.Empty;

        [UnityEngine.Tooltip("Human-readable debug/display title, e.g. 'Clear All Sections'.")]
        public string title = string.Empty;

        [UnityEngine.Tooltip("Which objective behaviour this entry configures.")]
        public MissionObjectiveType objectiveType = MissionObjectiveType.ClearAllSections;

        [UnityEngine.Tooltip("True when this objective GATES mission completion. Optional " +
                             "objectives are tracked but never block victory.")]
        public bool required = true;
    }
}
