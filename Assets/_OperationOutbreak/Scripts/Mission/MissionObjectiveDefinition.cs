using System;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1U/1X.5 - the reusable objective types a mission can declare.
    ///
    /// ClearAllSections (1U) is joined in 1X.5 by three new types so Chapter 1 missions can
    /// differ by WHAT the player does, not only by enemy count: SurviveDuration (hold a timed
    /// defense), DestroyTargets (break barricades) and ActivateTargets (reach/hold activation
    /// points). Adding a type only extends this enum and the runtime's centralized evaluation -
    /// the mission-flow architecture and the single completion authority never change.
    /// </summary>
    public enum MissionObjectiveType
    {
        /// <summary>
        /// Clear every configured mission section (equivalently: defeat all mission
        /// enemies). Required progress is DERIVED from the mission's section count,
        /// so the objective always stays in sync with the mission structure.
        /// </summary>
        ClearAllSections = 0,

        /// <summary>
        /// Milestone 1X.5 - survive a configured duration while enemies attack. Progress is
        /// TIME (durationSeconds); the objective completes only when the timer elapses, so
        /// killing enemies can never bypass it. The timer is gated: it starts only once the
        /// objective is active (typically when the player reaches the mission's hold section).
        /// </summary>
        SurviveDuration = 1,

        /// <summary>
        /// Milestone 1X.5 - destroy a configured number of mission targets (barricades).
        /// Progress is a COUNT of destroyed targets (deduped by target id); the objective
        /// completes only when enough targets are destroyed, so killing enemies can never
        /// satisfy it. Used by Mission 4 (Pushback).
        /// </summary>
        DestroyTargets = 2,

        /// <summary>
        /// Milestone 1X.5 - reach and hold a configured number of activation control points.
        /// Each target fills an activation progress while the player stands in its radius;
        /// the objective completes only when enough points are activated, so killing enemies
        /// can never satisfy it. Used by Mission 5 (Containment).
        /// </summary>
        ActivateTargets = 3
    }

    /// <summary>
    /// Milestone 1U/1X.5 - one data-driven objective of a mission. PURE DATA, like the
    /// rest of MissionDefinition: it declares WHAT must happen (identity, type, required)
    /// and never holds runtime progress.
    ///
    /// A simple serializable tagged-data model is used deliberately - no class hierarchy per
    /// objective type. Each type's parameters live as serialized fields here; the runtime
    /// evaluator (MissionObjectiveRuntime, fed by MissionObjectiveController) is the single
    /// place that interprets the type. Optional objective sequencing uses
    /// <see cref="activateAfterObjectiveId"/>: an objective with a non-empty value stays
    /// inactive until the named objective completes (Mission 5's clear -> activate -> defend
    /// chain). Empty = active from the start of the mission.
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

        [UnityEngine.Tooltip("1X.5 SurviveDuration: seconds the player must survive while the " +
                             "objective is active. Must be > 0 for SurviveDuration.")]
        [UnityEngine.Min(0f)] public float durationSeconds = 0f;

        [UnityEngine.Tooltip("1X.5 DestroyTargets / ActivateTargets: how many targets must be " +
                             "destroyed / activated. Must be > 0 for those types.")]
        [UnityEngine.Min(1)] public int requiredTargetCount = 1;

        [UnityEngine.Tooltip("1X.5 DestroyTargets: damage required to destroy each barricade " +
                             "target. Must be > 0 for DestroyTargets (barricade health).")]
        [UnityEngine.Min(1)] public int targetHealth = 6;

        [UnityEngine.Tooltip("1X.5 ActivateTargets: seconds the player must remain in a target's " +
                             "activation radius to activate it. Must be > 0 for ActivateTargets.")]
        [UnityEngine.Min(0.01f)] public float activationDuration = 1.5f;

        [UnityEngine.Tooltip("1X.5 ActivateTargets: world-space radius around a target in which " +
                             "activation progress accumulates. Must be > 0 for ActivateTargets.")]
        [UnityEngine.Min(0.1f)] public float activationRadius = 1.5f;

        [UnityEngine.Tooltip("1X.5 ActivateTargets: if true, leaving the radius RESETS that " +
                             "target's activation progress to zero; if false (default), progress " +
                             "is RETAINED where it was when the player left.")]
        public bool resetProgressOnLeave = false;

        [UnityEngine.Tooltip("1X.5 sequencing: the objective id that must be complete before " +
                             "this objective becomes active. Empty = active from mission start. " +
                             "Used for multi-stage missions (e.g. clear -> activate -> defend).")]
        public string activateAfterObjectiveId = string.Empty;
    }
}
