using NUnit.Framework;
using OperationOutbreak.Diagnostics;
using OperationOutbreak.Upgrades;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1O - EditMode tests for the check model and the end-of-run report.
    ///
    /// These prove the reporting layer itself is trustworthy: that a FAIL cannot be
    /// silently counted as a PASS, and that a synthetic "perfect run" produces a clean
    /// report while a deliberately broken run is caught. Everything is built in memory,
    /// so no scene, no prefabs and no gameplay are touched.
    /// </summary>
    public sealed class DiagnosticReportTests
    {
        // ------------------------------------------------------------ check model

        [Test]
        public void CheckListCountsEachStatusSeparately()
        {
            var checks = new DiagnosticCheckList();
            checks.Add(DiagnosticCheck.Pass("A-1", "passing", "d", "e", "a"));
            checks.Add(DiagnosticCheck.Warn("A-2", "warning", "d", "e", "a"));
            checks.Add(DiagnosticCheck.Fail("A-3", "failing", "d", "e", "a"));

            Assert.AreEqual(3, checks.Count);
            Assert.AreEqual(1, checks.PassedCount);
            Assert.AreEqual(1, checks.WarningCount);
            Assert.AreEqual(1, checks.FailedCount);
        }

        [Test]
        public void AllPassedIsFalseWhenAnythingFailed()
        {
            var checks = new DiagnosticCheckList();
            checks.Add(DiagnosticCheck.Pass("A-1", "passing", "d", "e", "a"));
            Assert.IsTrue(checks.AllPassed);

            checks.Add(DiagnosticCheck.Fail("A-2", "failing", "d", "e", "a"));
            Assert.IsFalse(checks.AllPassed, "A single failure must sink the whole run.");
        }

        [Test]
        public void AWarningDoesNotSinkTheRun()
        {
            // Warnings flag things worth a human look (a tight spawn, an expired pickup)
            // without declaring the run broken.
            var checks = new DiagnosticCheckList();
            checks.Add(DiagnosticCheck.Warn("A-1", "warning", "d", "e", "a"));

            Assert.IsTrue(checks.AllPassed);
            Assert.AreEqual(1, checks.WarningCount);
        }

        [Test]
        public void EvaluateMapsConditionToPassOrFail()
        {
            DiagnosticCheck passed = DiagnosticCheck.Evaluate(
                true, "E-1", "condition", "d", "e", "a");
            DiagnosticCheck failed = DiagnosticCheck.Evaluate(
                false, "E-2", "condition", "d", "e", "a");

            Assert.AreEqual(DiagnosticStatus.Passed, passed.Status);
            Assert.AreEqual(DiagnosticStatus.Failed, failed.Status);
        }

        [Test]
        public void EvaluateCanDowngradeAFailureToAWarning()
        {
            DiagnosticCheck soft = DiagnosticCheck.Evaluate(
                false, "E-3", "condition", "d", "e", "a", null, DiagnosticStatus.Warning);

            Assert.AreEqual(DiagnosticStatus.Warning, soft.Status);
        }

        // ------------------------------------------------------------ report shape

        /// <summary>Builds a synthetic run where everything went correctly.</summary>
        private static DiagnosticRunData BuildCleanRun()
        {
            var data = new DiagnosticRunData
            {
                MissionStartTime = 0f,
                MissionCompleted = true,
                MissionCompleteTime = 120f,
                MissionCompleteEventCount = 1,
                AuthoredUpgradeCount = 4,
                LaneBoundsCaptured = true,
                LaneMinX = -3.6f,
                LaneMaxX = 3.6f,
                LaneMinZ = -3f,
                LaneMaxZ = 55f,
                MinimumDistanceFromPlayer = 3f,
                MinimumDistanceFromPreviousPickup = 4f,
                SpawnClearanceRadius = 1.4f
            };

            data.UpgradeRunOrder.AddRange(new[] { 2, 0, 3, 1 });

            int[] expected = { 3, 4, 5 };
            for (int i = 0; i < expected.Length; i++)
            {
                data.Sections.Add(new SectionRecord
                {
                    SectionIndex = i,
                    Label = $"SECTION {i + 1}",
                    ActivationTime = i * 30f,
                    ClearedTime = (i * 30f) + 25f,
                    ExpectedEnemyCount = expected[i],
                    SpawnedEnemyCount = expected[i],
                    KilledEnemyCount = expected[i],
                    Cleared = true
                });
            }

            data.Player.BaseMaxHealth = 5;
            data.Player.FinalMaxHealth = 5;

            return data;
        }

        [Test]
        public void ACleanRunProducesNoFailures()
        {
            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(BuildCleanRun());

            Assert.Greater(checks.Count, 0, "The builder must actually emit checks.");
            Assert.AreEqual(0, checks.FailedCount,
                "A synthetic correct run must not report any failure.");
        }

        [Test]
        public void AShortSpawnCountIsReportedAsAFailure()
        {
            DiagnosticRunData data = BuildCleanRun();
            data.Sections[1].SpawnedEnemyCount = 3; // section 2 should spawn 4

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);

            Assert.Greater(checks.FailedCount, 0,
                "Spawning fewer enemies than the section authored must fail.");
        }

        [Test]
        public void ADuplicateMissionCompleteEventIsReportedAsAFailure()
        {
            DiagnosticRunData data = BuildCleanRun();
            data.MissionCompleteEventCount = 2;

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);

            Assert.Greater(checks.FailedCount, 0,
                "Mission Complete firing twice is exactly the bug this catches.");
        }

        [Test]
        public void AnIncompleteUpgradeOrderIsReportedAsAFailure()
        {
            DiagnosticRunData data = BuildCleanRun();
            data.UpgradeRunOrder.Clear();
            data.UpgradeRunOrder.AddRange(new[] { 0, 1, 1, 3 }); // duplicate, missing 2

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);

            Assert.Greater(checks.FailedCount, 0,
                "A run order with a duplicate upgrade must fail.");
        }

        [Test]
        public void AnUpgradeSpawningTooCloseToThePlayerIsReportedAsAFailure()
        {
            DiagnosticRunData data = BuildCleanRun();
            data.Upgrades.Add(new UpgradeRecord
            {
                OrderSlot = 1,
                OpportunityIndex = 2,
                UpgradeName = "FIRE RATE",
                UpgradeKind = UpgradeKind.FireRateMultiplier.ToString(),
                SpawnPosition = new Vector3(0f, 1.15f, 10f),
                PlayerPositionAtSpawn = new Vector3(0f, 1f, 9f), // only 1 unit away
                DistanceFromPlayerAtSpawn = 1f,
                DistanceFromPreviousPickup = -1f,
                SpawnTime = 3f,
                Collected = true,
                ResolutionTime = 5f
            });

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);

            Assert.Greater(checks.FailedCount, 0,
                "A pickup inside the 3 unit player standoff must fail.");
        }

        [Test]
        public void ReportContainsEverySectionHeadingAndAVerdict()
        {
            DiagnosticRunData data = BuildCleanRun();
            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);
            string report = DiagnosticReportBuilder.BuildReport(data, checks);

            Assert.IsNotEmpty(report);
            StringAssert.Contains("MISSION", report);
            StringAssert.Contains("ENEMY VARIETY", report);
            StringAssert.Contains("UPGRADES", report);
            StringAssert.Contains("PLAYER", report);
            StringAssert.Contains("RESULT", report);
            StringAssert.Contains("VERDICT", report);
        }

        [Test]
        public void ReportStatesTheOutcomeAndTheTotals()
        {
            DiagnosticRunData data = BuildCleanRun();
            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);
            string report = DiagnosticReportBuilder.BuildReport(data, checks);

            StringAssert.Contains("OUTCOME", report);
            StringAssert.Contains("TOTAL", report);
            StringAssert.Contains("PASSED", report);
            StringAssert.Contains("FAILED", report);
            StringAssert.Contains("WARNINGS", report);
        }

        [Test]
        public void RunDataIsInstanceBasedSoARestartStartsClean()
        {
            // Restart is a scene reload and there is no static run state anywhere, so a
            // fresh DiagnosticRunData must carry nothing over from a previous run.
            DiagnosticRunData finished = BuildCleanRun();
            Assert.IsTrue(finished.MissionCompleted);
            Assert.AreEqual(3, finished.Sections.Count);

            var fresh = new DiagnosticRunData();

            Assert.IsFalse(fresh.MissionCompleted, "A new run must not inherit completion.");
            Assert.IsFalse(fresh.GameOver);
            Assert.AreEqual(0, fresh.Sections.Count);
            Assert.AreEqual(0, fresh.Enemies.Count);
            Assert.AreEqual(0, fresh.Upgrades.Count);
            Assert.AreEqual(0, fresh.UpgradeRunOrder.Count);
            Assert.AreEqual(-1, fresh.DuplicateSectionClearIndex);
        }
        // ------------------------------------- Milestone 1O-R regression tests

        /// <summary>
        /// Reproduces the reported UPG-BOUNDS-3 / UPG-BOUNDS-4 false failures. Those pickups
        /// spawned at z=16.1 and z=46.71, which are outside the Section 1 rectangle but well
        /// inside the corridor that was actually open when they appeared.
        /// </summary>
        private static UpgradeRecord LatePickup(int slot, float z, float laneMaxZ)
        {
            return new UpgradeRecord
            {
                OrderSlot = slot,
                OpportunityIndex = slot - 1,
                UpgradeName = "MAX HEALTH",
                UpgradeKind = UpgradeKind.MaxHealthBonus.ToString(),
                SpawnPosition = new Vector3(-2.13f, 1.15f, z),
                PlayerPositionAtSpawn = new Vector3(0f, 1f, z - 8f),
                DistanceFromPlayerAtSpawn = 8f,
                DistanceFromPreviousPickup = 12f,
                SpawnTime = 40f,
                Collected = true,
                ResolutionTime = 42f,

                // The expanded corridor that was in force at THIS pickup's spawn time.
                LaneBoundsCaptured = true,
                LaneMinX = -3.6f,
                LaneMaxX = 3.6f,
                LaneMinZ = -3f,
                LaneMaxZ = laneMaxZ
            };
        }

        [Test]
        public void APickupBeyondSectionOneIsNotFailedWhenTheCorridorHadExpanded()
        {
            DiagnosticRunData data = BuildCleanRun();

            // Section 1 only reached z=15, but by the time these spawned the forward limit
            // had moved to 33 and then 51. Both must be treated as reachable.
            data.Upgrades.Add(LatePickup(3, 16.1f, 33f));
            data.Upgrades.Add(LatePickup(4, 46.71f, 51f));

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);

            Assert.AreEqual(0, checks.FailedCount,
                "Pickups inside the corridor that was open when they spawned must not fail.");
        }

        [Test]
        public void APickupOutsideTheCorridorThatWasOpenStillFails()
        {
            DiagnosticRunData data = BuildCleanRun();

            // Genuinely unreachable: spawned at z=46.7 while the corridor still ended at 15.
            data.Upgrades.Add(LatePickup(3, 46.7f, 15f));

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);

            Assert.Greater(checks.FailedCount, 0,
                "The bounds check must still catch a truly unreachable pickup.");
        }

        [Test]
        public void APickupOutsideTheLaneWidthStillFails()
        {
            DiagnosticRunData data = BuildCleanRun();

            UpgradeRecord record = LatePickup(3, 20f, 51f);
            record.SpawnPosition = new Vector3(9f, 1.15f, 20f); // far outside +/-3.6
            data.Upgrades.Add(record);

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);

            Assert.Greater(checks.FailedCount, 0,
                "A pickup outside the lateral lane limits must still fail.");
        }

        [Test]
        public void BoundsAreNotCheckedWhenNoneWereCapturedForThePickup()
        {
            DiagnosticRunData data = BuildCleanRun();

            UpgradeRecord record = LatePickup(3, 46.7f, 15f);
            record.LaneBoundsCaptured = false; // no PlayerLaneBounds wired

            data.Upgrades.Add(record);

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);

            Assert.AreEqual(0, checks.FailedCount,
                "Without captured bounds the check must be skipped rather than guessed.");
        }

        // ------------------------------------------------ runner spawn offset auditing

        private static EnemyRecord Enemy(
            int id, string archetype, float spawnZ, float playerZ, float spawnTime,
            float bandZ = float.NaN, float requestedOffset = 0f)
        {
            var record = new EnemyRecord
            {
                RuntimeId = id,
                Archetype = archetype,
                SectionIndex = 1,
                SpawnPosition = new Vector3(0f, 1f, spawnZ),
                PlayerPositionAtSpawn = new Vector3(0f, 1f, playerZ),
                InitialDistanceToPlayer = Mathf.Abs(spawnZ - playerZ),
                SpawnTime = spawnTime,
                MoveSpeed = archetype == "RUNNER" ? 3.5f : 2.5f,
                MaxHealth = archetype == "RUNNER" ? 2 : 3,
                AttackDamage = 1,
                RequestedSpawnOffset = requestedOffset,
                BandPosition = new Vector3(0f, 1f, float.IsNaN(bandZ) ? spawnZ : bandZ)
            };

            return record;
        }

        [Test]
        public void AnOffsetRemovedByTheStandoffClampIsDetected()
        {
            // Exactly the reported Runner #5: band z=40, offset 5 requested, but the player
            // was only 10.8 units back so the 12 unit standoff clamped it all away.
            EnemyRecord runner = Enemy(5, "RUNNER", 40f, 29.19f, 10f, 40f, 5f);

            Assert.IsTrue(runner.SpawnOffsetSuppressed,
                "A fully clamped offset must be reported as suppressed.");
            Assert.AreEqual(0f, runner.AppliedSpawnOffset, 0.001f);
        }

        [Test]
        public void AnOffsetThatSurvivesTheClampIsNotFlaggedAsSuppressed()
        {
            EnemyRecord runner = Enemy(5, "RUNNER", 35f, 18f, 10f, 40f, 5f);

            Assert.IsFalse(runner.SpawnOffsetSuppressed,
                "A fully applied offset must not be flagged.");
            Assert.AreEqual(5f, runner.AppliedSpawnOffset, 0.001f);
        }

        [Test]
        public void ASuppressedRunnerOffsetIsSurfacedAsAWarningNotAFailure()
        {
            DiagnosticRunData data = BuildCleanRun();

            EnemyRecord runner = Enemy(5, "RUNNER", 40f, 29.19f, 10f, 40f, 5f);
            data.Enemies.Add(runner);
            data.Sections[1].Enemies.Add(runner);

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);

            Assert.AreEqual(0, checks.FailedCount,
                "A suppressed offset is a balance observation, not a hard failure.");
            Assert.Greater(checks.WarningCount, 0,
                "It must still be visible as a warning.");
        }

        [Test]
        public void RunnerAdvantageComparesAgainstTheBasicClosestInSpawnTime()
        {
            DiagnosticRunData data = BuildCleanRun();

            // A Basic spawning much later is measured against a player who already walked
            // forward, so its raw distance is tiny. It must NOT be chosen as the reference.
            EnemyRecord early = Enemy(1, "BASIC", 37f, 26f, 9.4f);   // gap 11.0, near in time
            EnemyRecord late = Enemy(2, "BASIC", 37f, 32.8f, 20f);   // gap  4.2, far in time
            EnemyRecord runner = Enemy(5, "RUNNER", 40f, 29.19f, 10f, 40f, 5f);

            data.Enemies.Add(early);
            data.Enemies.Add(late);
            data.Enemies.Add(runner);
            data.Sections[1].Enemies.Add(early);
            data.Sections[1].Enemies.Add(late);
            data.Sections[1].Enemies.Add(runner);

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);

            DiagnosticCheck advantage = null;
            for (int i = 0; i < checks.Checks.Count; i++)
            {
                if (checks.Checks[i].Id == "RUN-ADV-5")
                {
                    advantage = checks.Checks[i];
                }
            }

            Assert.IsNotNull(advantage, "The runner advantage check must be emitted.");
            StringAssert.Contains("Basic #1", advantage.Details,
                "The time-nearest Basic must be used as the reference, not the later one.");
        }

        [Test]
        public void TheReportShowsRequestedAndAppliedOffsetForRunners()
        {
            DiagnosticRunData data = BuildCleanRun();

            EnemyRecord runner = Enemy(5, "RUNNER", 40f, 29.19f, 10f, 40f, 5f);
            data.Enemies.Add(runner);
            data.Sections[1].Enemies.Add(runner);

            DiagnosticCheckList checks = DiagnosticReportBuilder.BuildChecks(data);
            string report = DiagnosticReportBuilder.BuildReport(data, checks);

            StringAssert.Contains("requested=", report);
            StringAssert.Contains("applied=", report);
            StringAssert.Contains("SUPPRESSED", report);
        }
    }
}
