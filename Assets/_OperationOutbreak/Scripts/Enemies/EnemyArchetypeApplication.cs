using UnityEngine;

namespace OperationOutbreak.Enemies
{
    /// <summary>
    /// Milestone 1S - applies a data-driven archetype definition to a freshly
    /// spawned enemy instance of the SHARED gameplay prefab.
    ///
    /// This is the ONLY place a definition touches a live enemy, and it is pure
    /// wiring - no gameplay logic:
    ///   - ZombieController.ApplyArchetype: gameplay tuning values + spawn-time
    ///     health re-seed (the single gameplay authority keeps executing the
    ///     verified combat code).
    ///   - EnemyAnimationBridge.ApplyArchetype: locomotion presentation profile -
    ///     controller swap (when the definition declares one) + cadence
    ///     reference. No "if runner" branch exists here: the definition's data
    ///     decides what happens.
    ///
    /// Applying a NULL definition is a no-op, which keeps the verified Basic
    /// Infected defaults (the prefab's serialized values) untouched.
    /// </summary>
    public static class EnemyArchetypeApplication
    {
        /// <summary>
        /// Applies the definition to every framework component on the enemy root.
        /// Returns false (and logs) when the root carries no ZombieController -
        /// an archetype can only ever be applied to the shared gameplay enemy.
        /// </summary>
        public static bool Apply(GameObject enemyRoot, EnemyArchetypeDefinition definition)
        {
            if (enemyRoot == null)
            {
                Debug.LogError("[1S] Cannot apply an archetype to a null enemy root.");
                return false;
            }

            if (definition == null)
            {
                // Verified defaults: the prefab's serialized values remain
                // authoritative. Applying nothing is applying Basic.
                return true;
            }

            ZombieController zombie = enemyRoot.GetComponent<ZombieController>();

            if (zombie == null)
            {
                Debug.LogError(
                    "[1S] Archetype '" + definition.ArchetypeId + "' cannot be applied: '" +
                    enemyRoot.name + "' carries no ZombieController (the shared gameplay " +
                    "authority). Spawn aborted for this enemy.", enemyRoot);
                return false;
            }

            zombie.ApplyArchetype(definition);

            EnemyAnimationBridge bridge = enemyRoot.GetComponent<EnemyAnimationBridge>();
            if (bridge != null)
            {
                bridge.ApplyArchetype(definition);
            }

            return true;
        }
    }
}
