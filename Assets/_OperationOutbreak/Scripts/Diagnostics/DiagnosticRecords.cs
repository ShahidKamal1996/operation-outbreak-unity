using System.Collections.Generic;
using UnityEngine;

namespace OperationOutbreak.Diagnostics
{
    /// <summary>
    /// Milestone 1O - everything observed about one spawned enemy.
    ///
    /// Written once at spawn, then touched only by the events the enemy itself raises.
    /// Nothing here is polled per frame.
    /// </summary>
    public sealed class EnemyRecord
    {
        public int RuntimeId;
        public string Archetype;
        public int SectionIndex;

        public Vector3 SpawnPosition;
        public Vector3 PlayerPositionAtSpawn;
        public float InitialDistanceToPlayer;
        public float SpawnTime;

        public float MoveSpeed;
        public int MaxHealth;
        public int AttackDamage;

        /// <summary>Negative until the enemy dies.</summary>
        public float DeathTime = -1f;

        public bool Died;
        public int ProjectileHits;
        public bool DamagedPlayer;

        /// <summary>Set at spawn time by the overlap check. Never causes repositioning.</summary>
        public bool SpawnedOverlapping;

        /// <summary>Distance to the nearest live enemy at the moment of spawn, -1 when alone.</summary>
        public float NearestEnemyDistanceAtSpawn = -1f;

        public bool IsRunner => Archetype == "RUNNER";

        public float LifetimeSeconds => DeathTime >= 0f ? DeathTime - SpawnTime : -1f;
    }

    /// <summary>Milestone 1O - everything observed about one upgrade opportunity.</summary>
    public sealed class UpgradeRecord
    {
        /// <summary>Position in the shuffled run order: 1 = first offered this run.</summary>
        public int OrderSlot;

        /// <summary>Index into the authored opportunity list, i.e. which upgrade this is.</summary>
        public int OpportunityIndex;

        public string UpgradeName;
        public string UpgradeKind;

        public Vector3 SpawnPosition;
        public Vector3 PlayerPositionAtSpawn;
        public float SpawnTime;
        public float DistanceFromPlayerAtSpawn;

        /// <summary>Negative for the first pickup of the run.</summary>
        public float DistanceFromPreviousPickup = -1f;

        public bool Collected;
        public bool Expired;

        /// <summary>Negative until collected or expired.</summary>
        public float ResolutionTime = -1f;

        public bool IsResolved => Collected || Expired;

        public float TimeToResolve => ResolutionTime >= 0f ? ResolutionTime - SpawnTime : -1f;

        public string Outcome => Collected ? "COLLECTED" : Expired ? "EXPIRED" : "UNRESOLVED";
    }

    /// <summary>Milestone 1O - everything observed about one mission section.</summary>
    public sealed class SectionRecord
    {
        public int SectionIndex;
        public string Label;

        public float ActivationTime;
        public float ClearedTime = -1f;

        public int ExpectedEnemyCount;
        public int SpawnedEnemyCount;
        public int KilledEnemyCount;

        public bool Cleared;

        public readonly List<EnemyRecord> Enemies = new List<EnemyRecord>();

        public float DurationSeconds => ClearedTime >= 0f ? ClearedTime - ActivationTime : -1f;
    }

    /// <summary>
    /// Milestone 1O - player stat changes over the run.
    ///
    /// Recorded from the existing upgrade hooks and the existing PlayerHealth events, so
    /// no parallel player-stat tracking is introduced: these are observations of the
    /// approved systems, never a second source of truth.
    /// </summary>
    public sealed class PlayerRecord
    {
        public int BaseMaxHealth;
        public int FinalMaxHealth;

        public float DeathTime = -1f;
        public bool Died;

        public int MaxHealthUpgrades;
        public int MoveSpeedUpgrades;
        public int WeaponDamageUpgrades;
        public int FireRateUpgrades;

        public readonly List<string> StatChangeLog = new List<string>();

        public void LogChange(string line)
        {
            StatChangeLog.Add(line);
        }
    }
}
