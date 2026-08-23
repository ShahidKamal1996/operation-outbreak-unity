using System;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X.5 - the loose event bus between world-space objective targets (barricades,
    /// activation points) and the single objective authority (MissionObjectiveController).
    ///
    /// Targets are scene objects that the player destroys/activates; the controller owns mission
    /// completion. To keep them decoupled (targets need no reference to the controller, the
    /// controller needs no scene searches for targets), targets RAISE events here when they are
    /// destroyed/activated, and the controller SUBSCRIBES here on enable. The bus carries the
    /// target's id so the controller can dedupe and route to the right objective type.
    ///
    /// This is a static event hub (like EnemyArchetypeRegistry's static state) for meta/objective
    /// signals only - never run-scoped gameplay state. The controller unsubscribes on disable so a
    /// scene reload never double-routes. Designed so a later milestone (1Y+) can add more target
    /// kinds (escort payloads, hack points) by raising new events without changing the controller's
    /// completion authority.
    /// </summary>
    public static class MissionObjectiveTargetEvents
    {
        /// <summary>Raised when a destroyable target (e.g. a barricade) is destroyed. Carries its id.</summary>
        public static event Action<string> TargetDestroyed;

        /// <summary>Raised when an activation target finishes activating. Carries its id.</summary>
        public static event Action<string> TargetActivated;

        /// <summary>Raises <see cref="TargetDestroyed"/>. Called by destroyable targets.</summary>
        public static void RaiseTargetDestroyed(string targetId)
        {
            if (!string.IsNullOrEmpty(targetId))
            {
                TargetDestroyed?.Invoke(targetId);
            }
        }

        /// <summary>Raises <see cref="TargetActivated"/>. Called by activation targets.</summary>
        public static void RaiseTargetActivated(string targetId)
        {
            if (!string.IsNullOrEmpty(targetId))
            {
                TargetActivated?.Invoke(targetId);
            }
        }
    }
}
