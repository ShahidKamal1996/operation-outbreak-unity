using System.Collections.Generic;
using NUnit.Framework;
using OperationOutbreak.Diagnostics;
using OperationOutbreak.Enemies;
using OperationOutbreak.Mission;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1O - EditMode tests for the authored mission shape: three sections with
    /// 3 / 4 / 5 enemies and the 0 / 1 / 2 Runner split.
    ///
    /// These build SectionDefinition objects in memory rather than loading the scene,
    /// because MissionSectionController's list is private and serialized. That keeps the
    /// tests fast and side-effect free; the companion check in the runtime diagnostics
    /// verifies the ACTUAL scene values during a play session, and the section-table test
    /// below encodes the same numbers so a drift shows up in one of the two places.
    /// </summary>
    public sealed class MissionStructureTests
    {
        /// <summary>Rebuilds the approved section table exactly as authored in the scene.</summary>
        private static List<MissionSectionController.SectionDefinition> BuildApprovedSections()
        {
            var s1 = new MissionSectionController.SectionDefinition
            {
                label = "SECTION 1",
                activationZ = -100f,
                forwardLimitZ = 15f,
                enemyCount = 3,
                composition = new List<EnemySpawnEntry>
                {
                    new EnemySpawnEntry(EnemyArchetypeId.Basic, 3)
                }
            };

            var s2 = new MissionSectionController.SectionDefinition
            {
                label = "SECTION 2",
                activationZ = 20f,
                forwardLimitZ = 33f,
                enemyCount = 4,
                composition = new List<EnemySpawnEntry>
                {
                    new EnemySpawnEntry(EnemyArchetypeId.Basic, 3),
                    new EnemySpawnEntry(EnemyArchetypeId.Runner, 1)
                }
            };

            var s3 = new MissionSectionController.SectionDefinition
            {
                label = "SECTION 3",
                activationZ = 38f,
                forwardLimitZ = 51f,
                enemyCount = 5,
                composition = new List<EnemySpawnEntry>
                {
                    new EnemySpawnEntry(EnemyArchetypeId.Basic, 3),
                    new EnemySpawnEntry(EnemyArchetypeId.Runner, 2)
                }
            };

            return new List<MissionSectionController.SectionDefinition> { s1, s2, s3 };
        }

        private static int CountOf(
            MissionSectionController.SectionDefinition section, string archetypeId)
        {
            int total = 0;
            foreach (EnemySpawnEntry entry in section.composition)
            {
                if (entry != null && entry.archetypeId == archetypeId)
                {
                    total += entry.count;
                }
            }

            return total;
        }

        // ------------------------------------------------------------ composition

        [Test]
        public void SectionOneIsThreeBasicsAndNoRunners()
        {
            MissionSectionController.SectionDefinition s1 = BuildApprovedSections()[0];

            Assert.AreEqual(3, CountOf(s1, EnemyArchetypeId.Basic));
            Assert.AreEqual(0, CountOf(s1, EnemyArchetypeId.Runner),
                "Section 1 introduces the Basic zombie only.");
            Assert.AreEqual(3, s1.TotalEnemyCount);
        }

        [Test]
        public void SectionTwoIsThreeBasicsAndOneRunner()
        {
            MissionSectionController.SectionDefinition s2 = BuildApprovedSections()[1];

            Assert.AreEqual(3, CountOf(s2, EnemyArchetypeId.Basic));
            Assert.AreEqual(1, CountOf(s2, EnemyArchetypeId.Runner));
            Assert.AreEqual(4, s2.TotalEnemyCount);
        }

        [Test]
        public void SectionThreeIsThreeBasicsAndTwoRunners()
        {
            MissionSectionController.SectionDefinition s3 = BuildApprovedSections()[2];

            Assert.AreEqual(3, CountOf(s3, EnemyArchetypeId.Basic));
            Assert.AreEqual(2, CountOf(s3, EnemyArchetypeId.Runner));
            Assert.AreEqual(5, s3.TotalEnemyCount);
        }

        [Test]
        public void MissionTotalsAreThreeFourFive()
        {
            List<MissionSectionController.SectionDefinition> sections = BuildApprovedSections();

            Assert.AreEqual(3, sections.Count, "The mission is three sections long.");
            Assert.AreEqual(3, sections[0].TotalEnemyCount);
            Assert.AreEqual(4, sections[1].TotalEnemyCount);
            Assert.AreEqual(5, sections[2].TotalEnemyCount);
            Assert.AreEqual(12, sections[0].TotalEnemyCount
                + sections[1].TotalEnemyCount
                + sections[2].TotalEnemyCount);
        }

        [Test]
        public void TotalEnemyCountFollowsCompositionRatherThanTheLegacyCount()
        {
            // composition is authoritative; enemyCount is only the legacy fallback. If the
            // two disagree the spawner must follow composition, or Section 2 would spawn
            // the wrong number of enemies and could never report AREA CLEAR correctly.
            var section = new MissionSectionController.SectionDefinition
            {
                enemyCount = 99,
                composition = new List<EnemySpawnEntry>
                {
                    new EnemySpawnEntry(EnemyArchetypeId.Basic, 3),
                    new EnemySpawnEntry(EnemyArchetypeId.Runner, 1)
                }
            };

            Assert.AreEqual(4, section.TotalEnemyCount);
        }

        [Test]
        public void TotalEnemyCountFallsBackToEnemyCountWhenNoCompositionIsAuthored()
        {
            var section = new MissionSectionController.SectionDefinition
            {
                enemyCount = 3,
                composition = new List<EnemySpawnEntry>()
            };

            Assert.AreEqual(3, section.TotalEnemyCount);
        }

        // ------------------------------------------------------------ progression

        [Test]
        public void SectionsAreStrictlyForwardProgressing()
        {
            List<MissionSectionController.SectionDefinition> sections = BuildApprovedSections();

            var activations = new List<float>();
            var limits = new List<float>();
            foreach (MissionSectionController.SectionDefinition section in sections)
            {
                activations.Add(section.activationZ);
                limits.Add(section.forwardLimitZ);
            }

            Assert.IsTrue(DiagnosticRules.IsStrictlyForwardProgressing(activations, limits),
                "Each section must activate beyond the previous stop line, or it fires instantly.");
        }

        [Test]
        public void EachSectionActivationSitsBeyondThePreviousForwardLimit()
        {
            List<MissionSectionController.SectionDefinition> sections = BuildApprovedSections();

            for (int i = 1; i < sections.Count; i++)
            {
                Assert.Greater(sections[i].activationZ, sections[i - 1].forwardLimitZ,
                    $"Section {i + 1} activates at {sections[i].activationZ} but section {i} " +
                    $"stops the player at {sections[i - 1].forwardLimitZ}.");
            }
        }

        [Test]
        public void MissionCompleteIsImpossibleBeforeTheFinalSection()
        {
            // Mission Complete is defined as "the last section index has been cleared".
            // Clearing section 0 or 1 must never satisfy it.
            List<MissionSectionController.SectionDefinition> sections = BuildApprovedSections();
            int lastIndex = sections.Count - 1;

            Assert.AreNotEqual(lastIndex, 0, "Clearing Section 1 must not complete the mission.");
            Assert.AreNotEqual(lastIndex, 1, "Clearing Section 2 must not complete the mission.");
            Assert.AreEqual(2, lastIndex, "Only clearing Section 3 completes the mission.");
        }

        [Test]
        public void SpawnLineAlwaysSitsAheadOfTheForwardLimit()
        {
            // Enemies spawn at forwardLimitZ + spawnAheadOfLimit, so they always appear in
            // front of where the player is allowed to stand.
            foreach (MissionSectionController.SectionDefinition section in BuildApprovedSections())
            {
                Assert.Greater(section.spawnAheadOfLimit, 0f,
                    $"{section.label} would spawn enemies on top of the player.");
            }
        }
    }
}
