using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - the lightweight handoff of the SELECTED mission into a gameplay run.
    ///
    /// Mission selection (MissionSelectionService) decides WHICH MissionDefinition a run
    /// plays; the gameplay scene must then make that SAME definition authoritative for
    /// every existing mission consumer (MissionSectionController,
    /// MissionObjectiveController, MissionRewardService) without duplicating any of them.
    ///
    /// This static holder is the single, intentional bridge between the two. It is NOT
    /// run-scoped gameplay state (those systems keep their own instance state and reset on
    /// scene reload) - it is meta-progress state of the kind the codebase already keeps
    /// statically (see EnemyArchetypeRegistry): a single reference to "the mission this run
    /// is playing", set the moment a mission is started and read once when the scene boots.
    ///
    /// Lifecycle:
    ///   - MissionSelectionService.StartSelected sets Current to the chosen mission.
    ///   - MissionRuntimeAssignment (an early-Awake scene component) pushes Current into the
    ///     three mission consumers BEFORE they read their serialized default.
    ///   - When Current is null (the gameplay scene is opened directly for QA, or no mission
    ///     has been started) every consumer keeps its serialized default - Mission 01 - so
    ///     the verified direct-QA path is byte-for-byte unchanged.
    ///   - Current persists across the scene reload a Retry/Next performs, so replaying the
    ///     same mission keeps working; it is overwritten by the next StartSelected.
    ///
    /// There is intentionally no cloud, no save and no analytics here.
    /// </summary>
    public static class ActiveMissionContext
    {
        private static MissionDefinition s_current;
        private static string s_currentMissionId;

        /// <summary>
        /// The mission the current/next gameplay run must play, or null when no mission has
        /// been started through the selection system.
        /// </summary>
        public static MissionDefinition Current => s_current;

        /// <summary>
        /// Convenience: true when a mission has been selected for the run. Cheaper/safer than
        /// null-checking <see cref="Current"/> for callers that only need the boolean.
        /// </summary>
        public static bool HasCurrent => s_current != null;

        /// <summary>
        /// The stable id of the mission being played, captured at start time so it survives
        /// even if the asset reference is cleared. Empty when no mission is active.
        /// </summary>
        public static string CurrentMissionId => s_currentMissionId ?? string.Empty;

        /// <summary>
        /// Sets the mission the next/current run plays. Called by
        /// MissionSelectionService.StartSelected. A null argument clears the context.
        /// </summary>
        public static void SetForRun(MissionDefinition mission)
        {
            s_current = mission;
            s_currentMissionId = mission != null ? mission.MissionId : null;
        }

        /// <summary>Clears the active mission (returns to the serialized default in gameplay).</summary>
        public static void Clear()
        {
            s_current = null;
            s_currentMissionId = null;
        }

        /// <summary>
        /// Resolves the authoritative mission for a consumer: the active mission when one is
        /// set, otherwise the consumer's own serialized fallback. This is the one place the
        /// "selected mission becomes authoritative" rule is expressed, so consumers do not
        /// each branch on it.
        /// </summary>
        public static MissionDefinition Resolve(MissionDefinition fallback)
        {
            return s_current != null ? s_current : fallback;
        }
    }
}
