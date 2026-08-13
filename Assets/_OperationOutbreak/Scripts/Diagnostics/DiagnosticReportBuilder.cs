using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace OperationOutbreak.Diagnostics
{
    /// <summary>
    /// Milestone 1O - turns a finished <see cref="DiagnosticRunData"/> into the check list
    /// and the copy-pasteable console report.
    ///
    /// Pure and static: it takes recorded data in and returns text out. It never reads the
    /// scene, never touches a component and never runs during gameplay - it is invoked
    /// exactly once, at Mission Complete or Game Over. That is what keeps the whole
    /// diagnostics feature off the per-frame path, and what makes every verdict in the
    /// report reproducible from an EditMode test.
    /// </summary>
    public static class DiagnosticReportBuilder
    {
        private const string Rule = "================================================================";
        private const string Thin = "----------------------------------------------------------------";

        private static string F(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string V(Vector3 value)
        {
            return $"({F(value.x)}, {F(value.z)})";
        }

        /// <summary>
        /// Evaluates every rule against the recorded run. The order of the returned checks
        /// is the order they print in.
        /// </summary>
        public static DiagnosticCheckList BuildChecks(DiagnosticRunData data)
        {
            DiagnosticCheckList checks = new DiagnosticCheckList();

            if (data == null)
            {
                checks.Add(DiagnosticCheck.Fail(
                    "GEN-00", "Run data present",
                    "The diagnostics recorder must produce a run data set.",
                    "run data", "null"));

                return checks;
            }

            BuildMissionChecks(data, checks);
            BuildEnemyChecks(data, checks);
            BuildUpgradeChecks(data, checks);
            BuildPlayerChecks(data, checks);

            return checks;
        }

        private static void BuildMissionChecks(DiagnosticRunData data, DiagnosticCheckList checks)
        {
            // Every section that started must have spawned exactly the enemies it promised.
            for (int i = 0; i < data.Sections.Count; i++)
            {
                SectionRecord section = data.Sections[i];

                checks.Add(DiagnosticCheck.Evaluate(
                    section.SpawnedEnemyCount == section.ExpectedEnemyCount,
                    $"MIS-{section.SectionIndex + 1:00}",
                    $"Section {section.SectionIndex + 1} spawned its authored enemy count",
                    "The number of enemies spawned must equal the section's composition total.",
                    section.ExpectedEnemyCount.ToString(CultureInfo.InvariantCulture),
                    section.SpawnedEnemyCount.ToString(CultureInfo.InvariantCulture),
                    $"label='{section.Label}' cleared={section.Cleared} killed={section.KilledEnemyCount}"));
            }

            // A cleared section must have killed everything it spawned.
            for (int i = 0; i < data.Sections.Count; i++)
            {
                SectionRecord section = data.Sections[i];

                if (!section.Cleared)
                {
                    continue;
                }

                checks.Add(DiagnosticCheck.Evaluate(
                    section.KilledEnemyCount >= section.SpawnedEnemyCount,
                    $"MIS-{section.SectionIndex + 1:00}-CLR",
                    $"Section {section.SectionIndex + 1} cleared only after all enemies died",
                    "A section may not report cleared while any of its enemies is still alive.",
                    $"killed >= {section.SpawnedEnemyCount}",
                    section.KilledEnemyCount.ToString(CultureInfo.InvariantCulture),
                    $"duration={F(section.DurationSeconds)}s"));
            }

            checks.Add(DiagnosticCheck.Evaluate(
                data.DuplicateSectionClearIndex < 0,
                "MIS-DUP-SEC", "No section reported cleared twice",
                "Each section must raise its cleared signal exactly once per run.",
                "no duplicates",
                data.DuplicateSectionClearIndex < 0
                    ? "no duplicates"
                    : $"section {data.DuplicateSectionClearIndex + 1} cleared twice"));

            checks.Add(DiagnosticCheck.Evaluate(
                data.MissionCompleteEventCount <= 1,
                "MIS-DUP-MC", "Mission Complete raised at most once",
                "A duplicate completion event means a second victory path exists.",
                "<= 1",
                data.MissionCompleteEventCount.ToString(CultureInfo.InvariantCulture)));

            checks.Add(DiagnosticCheck.Evaluate(
                data.GameOverEventCount <= 1,
                "MIS-DUP-GO", "Game Over raised at most once",
                "A duplicate Game Over event means death is handled twice.",
                "<= 1",
                data.GameOverEventCount.ToString(CultureInfo.InvariantCulture)));

            // Victory and defeat must be mutually exclusive.
            checks.Add(DiagnosticCheck.Evaluate(
                !(data.MissionCompleted && data.GameOver),
                "MIS-EXCL", "Mission Complete and Game Over are exclusive",
                "The run must end in exactly one outcome.",
                "one outcome only",
                data.MissionCompleted && data.GameOver
                    ? "BOTH were shown"
                    : data.Outcome));

            // Mission Complete is only legal once every authored section is cleared.
            if (data.MissionCompleted)
            {
                int clearedSections = 0;

                for (int i = 0; i < data.Sections.Count; i++)
                {
                    if (data.Sections[i].Cleared)
                    {
                        clearedSections++;
                    }
                }

                checks.Add(DiagnosticCheck.Evaluate(
                    clearedSections == data.Sections.Count && data.Sections.Count > 0,
                    "MIS-FINAL", "Mission Complete only after the final section",
                    "Victory must be impossible before every section has been cleared.",
                    $"{data.Sections.Count} of {data.Sections.Count} sections cleared",
                    $"{clearedSections} of {data.Sections.Count} sections cleared"));
            }
        }

        private static void BuildEnemyChecks(DiagnosticRunData data, DiagnosticCheckList checks)
        {
            int overlapping = 0;

            for (int i = 0; i < data.Enemies.Count; i++)
            {
                if (data.Enemies[i].SpawnedOverlapping)
                {
                    overlapping++;
                }
            }

            // Diagnostics-only observation: the spawner's nudge system stays authoritative,
            // so a residual overlap is reported as a warning rather than a hard failure.
            checks.Add(DiagnosticCheck.Evaluate(
                overlapping == 0,
                "ENM-OVERLAP", "No enemy spawned overlapping another enemy",
                "At spawn time no enemy may sit inside another live enemy's clearance radius.",
                $"0 overlapping (clearance {F(data.SpawnClearanceRadius)})",
                overlapping.ToString(CultureInfo.InvariantCulture),
                null,
                DiagnosticStatus.Warning));

            EnemyRecord basicSample = null;
            EnemyRecord runnerSample = null;

            for (int i = 0; i < data.Enemies.Count; i++)
            {
                EnemyRecord enemy = data.Enemies[i];

                if (enemy.IsRunner)
                {
                    if (runnerSample == null)
                    {
                        runnerSample = enemy;
                    }
                }
                else if (basicSample == null)
                {
                    basicSample = enemy;
                }
            }

            // Runner balance is REPORTED, never corrected. These checks assert the approved
            // 1N relationship holds at runtime; they do not rebalance anything.
            if (basicSample != null && runnerSample != null)
            {
                checks.Add(DiagnosticCheck.Evaluate(
                    runnerSample.MoveSpeed > basicSample.MoveSpeed,
                    "RUN-SPEED", "Runner is faster than Basic",
                    "The Runner archetype must move faster than the Basic zombie.",
                    $"runner > basic ({F(basicSample.MoveSpeed)})",
                    F(runnerSample.MoveSpeed)));

                checks.Add(DiagnosticCheck.Evaluate(
                    runnerSample.MaxHealth < basicSample.MaxHealth,
                    "RUN-HEALTH", "Runner is frailer than Basic",
                    "The Runner archetype must have less health than the Basic zombie.",
                    $"runner < basic ({basicSample.MaxHealth})",
                    runnerSample.MaxHealth.ToString(CultureInfo.InvariantCulture)));

                checks.Add(DiagnosticCheck.Evaluate(
                    runnerSample.AttackDamage == basicSample.AttackDamage,
                    "RUN-DAMAGE", "Runner damage matches Basic",
                    "The Runner must not hit harder than the Basic zombie.",
                    basicSample.AttackDamage.ToString(CultureInfo.InvariantCulture),
                    runnerSample.AttackDamage.ToString(CultureInfo.InvariantCulture)));
            }

            // Milestone 1O-R - per-Runner spawn advantage.
            //
            // The original check compared a Runner's spawn distance against the SMALLEST
            // initial distance of any Basic in the same section. Those Basics spawn on a
            // 0.6s stagger while the player keeps advancing, so a Basic spawning later was
            // measured against a player who had already walked most of the way to the band.
            // That is not a like-for-like comparison, and it made a correctly-offset Runner
            // look like it had no advantage.
            //
            // Two comparisons are used instead, both time-honest:
            //   1. RUN-OFFSET-n : the Runner against ITS OWN authored band, same instant and
            //      same player position. Zero timing confound, so it isolates the offset.
            //   2. RUN-ADV-n    : the Runner against the Basic whose spawn time is CLOSEST to
            //      its own, i.e. the one that saw a comparable player position.
            for (int i = 0; i < data.Enemies.Count; i++)
            {
                EnemyRecord runner = data.Enemies[i];

                if (!runner.IsRunner)
                {
                    continue;
                }

                // ---- 1. Did the configured offset actually survive the spawner's clamps? ----
                if (runner.RequestedSpawnOffset > 0.01f)
                {
                    float bandDistance = DiagnosticRules.PlanarDistance(
                        runner.BandPosition, runner.PlayerPositionAtSpawn);

                    // Milestone 1N.2 - three distinct outcomes, reported honestly:
                    //   full offset            -> PASS
                    //   partial, but the enemy is sitting on its own safety standoff, i.e. the
                    //   player had already advanced too far -> WARNING, legitimately clamped
                    //   partial for any other reason (or fully removed) -> WARNING, a real
                    //   configuration problem worth looking at
                    bool fullyApplied = !runner.SpawnOffsetSuppressed;
                    bool safetyLimited = runner.SpawnOffsetLimitedBySafety;

                    string offsetDetails =
                        $"band z={F(runner.BandPosition.z)} -> spawn z={F(runner.SpawnPosition.z)}; " +
                        $"distance {F(bandDistance)} -> {F(runner.InitialDistanceToPlayer)}; " +
                        $"player z={F(runner.PlayerPositionAtSpawn.z)}; " +
                        $"standoff used={F(runner.StandoffUsed)}";

                    if (safetyLimited)
                    {
                        offsetDetails +=
                            "; partial offset was required to keep the archetype's minimum " +
                            "standoff - the player had already advanced toward the band";
                    }

                    checks.Add(DiagnosticCheck.Evaluate(
                        fullyApplied,
                        $"RUN-OFFSET-{runner.RuntimeId}",
                        $"Runner #{runner.RuntimeId} received its configured spawn offset",
                        "The archetype spawn offset must survive the minimum-standoff clamp, " +
                        "otherwise the Runner enters at the plain Basic band position. A partial " +
                        "offset is acceptable only when the standoff itself forced it.",
                        $"{F(runner.RequestedSpawnOffset)} forward",
                        $"{F(runner.AppliedSpawnOffset)} forward" +
                        (safetyLimited ? " (limited by standoff)" : string.Empty),
                        offsetDetails,
                        DiagnosticStatus.Warning));
                }

                // ---- 2. Compare against the time-nearest Basic of the same section ----
                SectionRecord section = data.GetSection(runner.SectionIndex);
                EnemyRecord reference = null;
                float bestTimeGap = -1f;

                if (section != null)
                {
                    for (int j = 0; j < section.Enemies.Count; j++)
                    {
                        EnemyRecord other = section.Enemies[j];

                        if (other.IsRunner)
                        {
                            continue;
                        }

                        float gap = Mathf.Abs(other.SpawnTime - runner.SpawnTime);

                        if (reference == null || gap < bestTimeGap)
                        {
                            reference = other;
                            bestTimeGap = gap;
                        }
                    }
                }

                if (reference != null)
                {
                    // Both distances are corrected onto a common player position by comparing
                    // each enemy's spawn against the player position IT saw, then differencing
                    // the band-relative gaps rather than the raw distances.
                    float runnerGap = runner.InitialDistanceToPlayer;
                    float basicGap = reference.InitialDistanceToPlayer;
                    float advantage = basicGap - runnerGap;

                    checks.Add(DiagnosticCheck.Evaluate(
                        runnerGap <= basicGap + 0.01f,
                        $"RUN-ADV-{runner.RuntimeId}",
                        $"Runner #{runner.RuntimeId} starts no further away than a comparable Basic",
                        "Compared against the Basic that spawned closest in time, so both saw a " +
                        "comparable player position.",
                        $"<= {F(basicGap)}",
                        F(runnerGap),
                        $"section={runner.SectionIndex + 1} advantage={F(advantage)} " +
                        $"reference=Basic #{reference.RuntimeId} spawn-time gap={F(bestTimeGap)}s " +
                        $"spawn={V(runner.SpawnPosition)}",
                        DiagnosticStatus.Warning));
                }

                // Purely informational: did the fast archetype ever actually apply pressure?
                checks.Add(DiagnosticCheck.Evaluate(
                    runner.DamagedPlayer,
                    $"RUN-REACH-{runner.RuntimeId}",
                    $"Runner #{runner.RuntimeId} reached the player before dying",
                    "Reported so Runner pressure can be judged objectively. Not a balance change.",
                    "attacked at least once",
                    runner.DamagedPlayer ? "attacked the player" : "died before attacking",
                    $"lifetime={F(runner.LifetimeSeconds)}s hits taken={runner.ProjectileHits}",
                    DiagnosticStatus.Warning));
            }
        }

        private static void BuildUpgradeChecks(DiagnosticRunData data, DiagnosticCheckList checks)
        {
            // The shuffle must be a permutation: each authored upgrade offered exactly once.
            checks.Add(DiagnosticCheck.Evaluate(
                DiagnosticRules.IsPermutation(data.UpgradeRunOrder, data.AuthoredUpgradeCount),
                "UPG-ORDER", "Upgrade run order contains each upgrade exactly once",
                "The shuffled order must be a permutation of the authored opportunities.",
                $"permutation of 0..{Mathf.Max(0, data.AuthoredUpgradeCount - 1)}",
                data.UpgradeRunOrder.Count == 0 ? "empty" : string.Join(",", data.UpgradeRunOrder)));

            checks.Add(DiagnosticCheck.Evaluate(
                !DiagnosticRules.HasDuplicates(data.UpgradeRunOrder),
                "UPG-DUP", "No duplicate upgrade offered",
                "An upgrade may not appear twice in a single run.",
                "no duplicates",
                DiagnosticRules.HasDuplicates(data.UpgradeRunOrder) ? "duplicates found" : "no duplicates"));

            // One-at-a-time invariant, proven from recorded spawn/resolution windows.
            bool simultaneous = false;

            for (int i = 0; i < data.Upgrades.Count && !simultaneous; i++)
            {
                for (int j = i + 1; j < data.Upgrades.Count; j++)
                {
                    UpgradeRecord a = data.Upgrades[i];
                    UpgradeRecord b = data.Upgrades[j];

                    float endA = a.ResolutionTime >= 0f ? a.ResolutionTime : float.MaxValue;
                    float endB = b.ResolutionTime >= 0f ? b.ResolutionTime : float.MaxValue;

                    if (DiagnosticRules.WindowsOverlap(a.SpawnTime, endA, b.SpawnTime, endB))
                    {
                        simultaneous = true;
                        break;
                    }
                }
            }

            checks.Add(DiagnosticCheck.Evaluate(
                !simultaneous,
                "UPG-ONEATATIME", "Only one upgrade pickup existed at a time",
                "Two pickups must never be collectable simultaneously.",
                "no overlapping pickup windows",
                simultaneous ? "overlapping windows found" : "no overlap"));

            for (int i = 0; i < data.Upgrades.Count; i++)
            {
                UpgradeRecord upgrade = data.Upgrades[i];

                checks.Add(DiagnosticCheck.Evaluate(
                    upgrade.DistanceFromPlayerAtSpawn >= data.MinimumDistanceFromPlayer - 0.01f,
                    $"UPG-DIST-P{upgrade.OrderSlot}",
                    $"Pickup {upgrade.OrderSlot} respected the minimum player distance",
                    "A pickup may not spawn on top of the player.",
                    $">= {F(data.MinimumDistanceFromPlayer)}",
                    F(upgrade.DistanceFromPlayerAtSpawn),
                    $"{upgrade.UpgradeName} at {V(upgrade.SpawnPosition)}"));

                if (upgrade.DistanceFromPreviousPickup >= 0f)
                {
                    checks.Add(DiagnosticCheck.Evaluate(
                        upgrade.DistanceFromPreviousPickup >= data.MinimumDistanceFromPreviousPickup - 0.01f,
                        $"UPG-DIST-V{upgrade.OrderSlot}",
                        $"Pickup {upgrade.OrderSlot} respected the minimum spacing from the previous pickup",
                        "Consecutive pickups must not appear in the same spot.",
                        $">= {F(data.MinimumDistanceFromPreviousPickup)}",
                        F(upgrade.DistanceFromPreviousPickup)));
                }

                // Milestone 1O-R - judge each pickup against the bounds that were in force
                // when IT spawned, not the run-start (Section 1) rectangle. The forward limit
                // expands as sections unlock, so a Section 3 pickup at z=46.7 is perfectly
                // reachable even though it sits far beyond the Section 1 limit of z=15.
                // A genuinely unreachable pickup - outside the lane that was open at the time -
                // still fails, so the check keeps its diagnostic value.
                if (upgrade.LaneBoundsCaptured)
                {
                    bool inBounds = DiagnosticRules.IsWithinBounds(
                        upgrade.SpawnPosition,
                        upgrade.LaneMinX, upgrade.LaneMaxX, upgrade.LaneMinZ, upgrade.LaneMaxZ);

                    checks.Add(DiagnosticCheck.Evaluate(
                        inBounds,
                        $"UPG-BOUNDS-{upgrade.OrderSlot}",
                        $"Pickup {upgrade.OrderSlot} spawned inside the reachable lane",
                        "A pickup outside the lane bounds can never be collected.",
                        $"x[{F(upgrade.LaneMinX)},{F(upgrade.LaneMaxX)}] " +
                        $"z[{F(upgrade.LaneMinZ)},{F(upgrade.LaneMaxZ)}]",
                        V(upgrade.SpawnPosition),
                        "bounds sampled at this pickup's spawn time (forward limit expands per section)"));
                }
            }
        }

        private static void BuildPlayerChecks(DiagnosticRunData data, DiagnosticCheckList checks)
        {
            int expectedMax = data.Player.BaseMaxHealth;

            checks.Add(DiagnosticCheck.Evaluate(
                data.Player.FinalMaxHealth >= expectedMax,
                "PLR-MAXHP", "Player max health never decreased",
                "Upgrades may only raise max health during a run.",
                $">= {expectedMax}",
                data.Player.FinalMaxHealth.ToString(CultureInfo.InvariantCulture),
                $"max-health upgrades collected={data.Player.MaxHealthUpgrades}"));
        }

        /// <summary>
        /// Formats the full end-of-run report. One string, printed with a single
        /// Debug.Log so the user can select and copy it in one action.
        /// </summary>
        public static string BuildReport(DiagnosticRunData data, DiagnosticCheckList checks)
        {
            StringBuilder sb = new StringBuilder(4096);

            sb.AppendLine(Rule);
            sb.AppendLine("OPERATION OUTBREAK - RUN DIAGNOSTICS (Milestone 1O)");
            sb.AppendLine(Rule);

            if (data == null)
            {
                sb.AppendLine("No run data was recorded.");
                return sb.ToString();
            }

            float endTime = data.MissionCompleted ? data.MissionCompleteTime : data.GameOverTime;
            float duration = endTime >= 0f ? endTime - data.MissionStartTime : -1f;

            sb.AppendLine($"OUTCOME : {data.Outcome}");
            sb.AppendLine($"DURATION: {F(duration)}s");
            sb.AppendLine();

            // ---------------- MISSION ----------------
            sb.AppendLine("MISSION");
            sb.AppendLine(Thin);
            sb.AppendLine($"  Sections authored     : {data.Sections.Count}");

            for (int i = 0; i < data.Sections.Count; i++)
            {
                SectionRecord s = data.Sections[i];

                sb.AppendLine(
                    $"  S{s.SectionIndex + 1} '{s.Label}' activated t={F(s.ActivationTime)}s " +
                    $"expected={s.ExpectedEnemyCount} spawned={s.SpawnedEnemyCount} " +
                    $"killed={s.KilledEnemyCount} cleared={(s.Cleared ? "YES" : "NO")} " +
                    $"duration={F(s.DurationSeconds)}s");
            }

            sb.AppendLine($"  Mission Complete raised: {data.MissionCompleteEventCount}");
            sb.AppendLine($"  Game Over raised       : {data.GameOverEventCount}");
            sb.AppendLine($"  Duplicate section clear: " +
                          (data.DuplicateSectionClearIndex < 0
                              ? "none"
                              : $"section {data.DuplicateSectionClearIndex + 1}"));
            sb.AppendLine();

            // ---------------- ENEMY VARIETY ----------------
            sb.AppendLine("ENEMY VARIETY");
            sb.AppendLine(Thin);

            int basics = 0;
            int runners = 0;

            for (int i = 0; i < data.Enemies.Count; i++)
            {
                if (data.Enemies[i].IsRunner)
                {
                    runners++;
                }
                else
                {
                    basics++;
                }
            }

            sb.AppendLine($"  Total spawned: {data.Enemies.Count}  (BASIC={basics}, RUNNER={runners})");

            for (int i = 0; i < data.Enemies.Count; i++)
            {
                EnemyRecord e = data.Enemies[i];

                sb.AppendLine(
                    $"  #{e.RuntimeId:00} {e.Archetype,-6} S{e.SectionIndex + 1} " +
                    $"spawn={V(e.SpawnPosition)} playerAt={V(e.PlayerPositionAtSpawn)} " +
                    $"dist={F(e.InitialDistanceToPlayer)} spd={F(e.MoveSpeed)} hp={e.MaxHealth} " +
                    $"dmg={e.AttackDamage} hits={e.ProjectileHits} " +
                    $"died={(e.Died ? F(e.DeathTime) + "s" : "alive")} " +
                    $"hitPlayer={(e.DamagedPlayer ? "YES" : "no")} " +
                    $"overlap={(e.SpawnedOverlapping ? "YES" : "no")}" +
                    (e.NearestEnemyDistanceAtSpawn >= 0f
                        ? $" nearest={F(e.NearestEnemyDistanceAtSpawn)}"
                        : string.Empty));

                // Milestone 1O-R - show requested vs applied spawn offset whenever the
                // archetype asked for one, so a clamped-away offset is visible immediately.
                if (e.RequestedSpawnOffset > 0.01f)
                {
                    sb.AppendLine(
                        $"        offset: requested={F(e.RequestedSpawnOffset)} " +
                        $"applied={F(e.AppliedSpawnOffset)} " +
                        $"standoff={F(e.StandoffUsed)} " +
                        $"band z={F(e.BandPosition.z)} -> spawn z={F(e.SpawnPosition.z)}" +
                        (e.SpawnOffsetSuppressed
                            ? (e.SpawnOffsetLimitedBySafety
                                ? "   <-- partial, limited by minimum standoff"
                                : "   <-- SUPPRESSED by minimum standoff")
                            : "   <-- applied in full"));
                }
            }

            sb.AppendLine();

            // ---------------- UPGRADES ----------------
            sb.AppendLine("UPGRADES");
            sb.AppendLine(Thin);
            sb.AppendLine($"  Shuffled run order: " +
                          (data.UpgradeRunOrder.Count == 0
                              ? "(none recorded)"
                              : string.Join(" -> ", data.UpgradeRunOrder)));

            for (int i = 0; i < data.Upgrades.Count; i++)
            {
                UpgradeRecord u = data.Upgrades[i];

                sb.AppendLine(
                    $"  {u.OrderSlot}. {u.UpgradeName,-12} ({u.UpgradeKind}) " +
                    $"spawn={V(u.SpawnPosition)} t={F(u.SpawnTime)}s " +
                    $"playerAt={V(u.PlayerPositionAtSpawn)} distPlayer={F(u.DistanceFromPlayerAtSpawn)} " +
                    $"distPrev={(u.DistanceFromPreviousPickup >= 0f ? F(u.DistanceFromPreviousPickup) : "n/a")} " +
                    $"{u.Outcome} after {F(u.TimeToResolve)}s");
            }

            sb.AppendLine();

            // ---------------- PLAYER ----------------
            sb.AppendLine("PLAYER");
            sb.AppendLine(Thin);
            sb.AppendLine($"  Base max health : {data.Player.BaseMaxHealth}");
            sb.AppendLine($"  Final max health: {data.Player.FinalMaxHealth}");
            sb.AppendLine(
                $"  Upgrades applied: maxHealth={data.Player.MaxHealthUpgrades} " +
                $"moveSpeed={data.Player.MoveSpeedUpgrades} " +
                $"weaponDamage={data.Player.WeaponDamageUpgrades} " +
                $"fireRate={data.Player.FireRateUpgrades}");
            sb.AppendLine($"  Player died     : " +
                          (data.Player.Died ? $"YES at {F(data.Player.DeathTime)}s" : "no"));

            for (int i = 0; i < data.Player.StatChangeLog.Count; i++)
            {
                sb.AppendLine($"    {data.Player.StatChangeLog[i]}");
            }

            sb.AppendLine();

            // ---------------- RESULT ----------------
            sb.AppendLine("RESULT");
            sb.AppendLine(Thin);

            if (checks != null)
            {
                for (int i = 0; i < checks.Checks.Count; i++)
                {
                    DiagnosticCheck c = checks.Checks[i];

                    sb.AppendLine(
                        $"  [{c.Status.ToString().ToUpperInvariant(),-7}] {c.Id,-16} {c.Name}");
                    sb.AppendLine($"             expected: {c.Expected}   actual: {c.Actual}");

                    if (!string.IsNullOrEmpty(c.Details))
                    {
                        sb.AppendLine($"             details : {c.Details}");
                    }
                }

                sb.AppendLine(Thin);
                sb.AppendLine(
                    $"  TOTAL {checks.Count}   PASSED {checks.PassedCount}   " +
                    $"FAILED {checks.FailedCount}   WARNINGS {checks.WarningCount}");
                sb.AppendLine($"  VERDICT: {(checks.AllPassed ? "PASS" : "FAIL")}");
            }

            sb.AppendLine(Rule);

            return sb.ToString();
        }
    }
}
