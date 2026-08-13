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

        /// <summary>
        /// Milestone 1O-R - the authored band position this enemy would have used before any
        /// archetype spawn offset was considered. Recorded so the offset can be audited.
        /// </summary>
        public Vector3 BandPosition;

        /// <summary>The archetype's configured spawnDistanceOffset (what was asked for).</summary>
        public float RequestedSpawnOffset;

        /// <summary>
        /// How much forward offset actually survived the spawner's standoff/nudge clamps.
        /// A value below <see cref="RequestedSpawnOffset"/> means the clamp suppressed it.
        /// </summary>
        public float AppliedSpawnOffset => BandPosition.z - SpawnPosition.z;

        /// <summary>
        /// Milestone 1N.2 - the minimum standoff that was actually in force for this spawn,
        /// after any per-archetype override. Recorded so the report can explain a clamped
        /// offset instead of only stating that it happened.
        /// </summary>
        public float StandoffUsed;

        /// <summary>True when an offset was configured but the clamps removed some of it.</summary>
        public bool SpawnOffsetSuppressed =>
            RequestedSpawnOffset > 0.01f && AppliedSpawnOffset < RequestedSpawnOffset - 0.01f;

        /// <summary>
        /// Milestone 1N.2-R - true when a configured offset was cancelled outright: the
        /// archetype asked to spawn closer and gained nothing at all, so the enemy entered on
        /// the plain band position.
        ///
        /// This is the exact shape of the original bug (offset 5 requested, 0 applied) and it
        /// is never a legitimate outcome, no matter where the enemy happens to end up. It is
        /// therefore classified separately from a partial offset, and deliberately BEFORE the
        /// safety explanation is considered.
        /// </summary>
        public bool SpawnOffsetFullyCancelled =>
            RequestedSpawnOffset > 0.01f && AppliedSpawnOffset <= 0.01f;

        /// <summary>
        /// Milestone 1N.2 - true when the offset was cut short specifically because honouring
        /// it in full would have breached the archetype's own minimum standoff, i.e. the enemy
        /// is sitting exactly on its safety boundary. This is the legitimate reason for a
        /// partial offset and is reported as such, rather than being hidden.
        ///
        /// Milestone 1N.2-R - a FULLY CANCELLED offset is explicitly excluded. Landing on the
        /// standoff boundary only excuses a shortfall when some of the offset actually
        /// survived; if none did, the standoff did not "limit" the offset, it erased it, and
        /// the enemy sitting on a boundary is a coincidence of the band geometry rather than
        /// evidence of a safe clamp. Without this exclusion a Runner whose 5 unit offset was
        /// wiped out by the old global 12 unit standoff was quietly excused as safety-limited,
        /// hiding the very misconfiguration this milestone exists to surface.
        /// </summary>
        public bool SpawnOffsetLimitedBySafety =>
            SpawnOffsetSuppressed &&
            !SpawnOffsetFullyCancelled &&
            StandoffUsed > 0f &&
            InitialDistanceToPlayer <= StandoffUsed + 0.05f;

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

        /// <summary>
        /// Milestone 1O-R - the reachable lane rectangle AT THE MOMENT THIS PICKUP SPAWNED.
        /// PlayerLaneBounds has had a mission-driven forward limit since Milestone 1M, so the
        /// reachable area grows as sections unlock. Judging a Section 2/3 pickup against the
        /// Section 1 rectangle produced false "unreachable" failures, so each pickup now
        /// carries the bounds that were actually in force when it appeared.
        /// </summary>
        public float LaneMinX, LaneMaxX, LaneMinZ, LaneMaxZ;

        /// <summary>False when no PlayerLaneBounds was wired, in which case bounds are unknown.</summary>
        public bool LaneBoundsCaptured;

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
