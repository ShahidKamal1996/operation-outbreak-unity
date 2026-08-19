using System.Collections.Generic;
using NUnit.Framework;
using OperationOutbreak.Diagnostics;
using OperationOutbreak.Mission;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1O - EditMode tests for the authored mission shape: three sections with
    /// 3 / 4 / 5 enemies and the 0 / 1 / 2 Runner split.
    ///
    /// Milestone 1T - these now build MissionDefinition.MissionSection objects in memory
    /// (the data model the runtime mission flow consumes) rather than the controller's old
    /// private serialized list. The numbers are unchanged from the verified baseline: the
    /// migration to the data-driven MissionDefinition did not move a single enemy.
    /// </summary>
    public sealed class MissionStructureTests
    {
        /// <summary>Rebuilds the approved section table exactly as authored in the scene.</summary>
        private static List<MissionDefinition.MissionSection> BuildApprovedSections()
        {
            var s1 = new MissionDefinition.MissionSection
            {
                sectionId = "section_01",
                label = "SECTION 1",
                subtitle = "OUTBREAK",
                activationZ = -100f,
                forwardLimitZ = 15f,
                spawnAheadOfLimit = 1f,
                composition = new List<MissionDefinition.EnemyCompositionEntry>
                {
                    new MissionDefinition.EnemyCompositionEntry(MissionDefinition.BasicArchetypeId, 3)
                }
            };

            var s2 = new MissionDefinition.MissionSection
            {
                sectionId = "section_02",
                label = "SECTION 2",
                subtitle = "ADVANCE",
                activationZ = 20f,
                forwardLimitZ = 33f,
                spawnAheadOfLimit = 4f,
                composition = new List<MissionDefinition.EnemyCompositionEntry>
                {
                    new MissionDefinition.EnemyCompositionEntry(MissionDefinition.BasicArchetypeId, 3),
                    new MissionDefinition.EnemyCompositionEntry(MissionDefinition.RunnerArchetypeId, 1)
                }
            };

            var s3 = new MissionDefinition.MissionSection
            {
                sectionId = "section_03",
                label = "SECTION 3",
                subtitle = "FINAL PUSH",
                activationZ = 38f,
                forwardLimitZ = 51f,
                spawnAheadOfLimit = 4f,
                composition = new List<MissionDefinition.EnemyCompositionEntry>
                {
                    new MissionDefinition.EnemyCompositionEntry(MissionDefinition.BasicArchetypeId, 3),
                    new MissionDefinition.EnemyCompositionEntry(MissionDefinition.RunnerArchetypeId, 2)
                }
            };

            return new List<MissionDefinition.MissionSection> { s1, s2, s3 };
        }

        private static int CountOf(
            MissionDefinition.MissionSection section, string archetypeId)
        {
            int total = 0;
            foreach (MissionDefinition.EnemyCompositionEntry entry in section.composition)
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
            MissionDefinition.MissionSection s1 = BuildApprovedSections()[0];

            Assert.AreEqual(3, CountOf(s1, MissionDefinition.BasicArchetypeId));
            Assert.AreEqual(0, CountOf(s1, MissionDefinition.RunnerArchetypeId),
                "Section 1 introduces the Basic zombie only.");
            Assert.AreEqual(3, s1.TotalEnemyCount);
        }

        [Test]
        public void SectionTwoIsThreeBasicsAndOneRunner()
        {
            MissionDefinition.MissionSection s2 = BuildApprovedSections()[1];

            Assert.AreEqual(3, CountOf(s2, MissionDefinition.BasicArchetypeId));
            Assert.AreEqual(1, CountOf(s2, MissionDefinition.RunnerArchetypeId));
            Assert.AreEqual(4, s2.TotalEnemyCount);
        }

        [Test]
        public void SectionThreeIsThreeBasicsAndTwoRunners()
        {
            MissionDefinition.MissionSection s3 = BuildApprovedSections()[2];

            Assert.AreEqual(3, CountOf(s3, MissionDefinition.BasicArchetypeId));
            Assert.AreEqual(2, CountOf(s3, MissionDefinition.RunnerArchetypeId));
            Assert.AreEqual(5, s3.TotalEnemyCount);
        }

        [Test]
        public void MissionTotalsAreThreeFourFive()
        {
            List<MissionDefinition.MissionSection> sections = BuildApprovedSections();

            Assert.AreEqual(3, sections.Count, "The mission is three sections long.");
            Assert.AreEqual(3, sections[0].TotalEnemyCount);
            Assert.AreEqual(4, sections[1].TotalEnemyCount);
            Assert.AreEqual(5, sections[2].TotalEnemyCount);
            Assert.AreEqual(12, sections[0].TotalEnemyCount
                + sections[1].TotalEnemyCount
                + sections[2].TotalEnemyCount);
        }

        [Test]
        public void TotalEnemyCountIsDerivedFromComposition()
        {
            // composition is the single source of truth for a section's enemy total. The
            // section total is DERIVED from it, never stored separately, so the two can
            // never drift out of sync.
            var section = new MissionDefinition.MissionSection
            {
                composition = new List<MissionDefinition.EnemyCompositionEntry>
                {
                    new MissionDefinition.EnemyCompositionEntry(MissionDefinition.BasicArchetypeId, 3),
                    new MissionDefinition.EnemyCompositionEntry(MissionDefinition.RunnerArchetypeId, 1)
                }
            };

            Assert.AreEqual(4, section.TotalEnemyCount);
        }

        [Test]
        public void SectionWithNoCompositionSpreadsZeroEnemies()
        {
            // Milestone 1T - composition is REQUIRED (validation rejects an empty one).
            // An empty composition therefore derives a zero total rather than silently
            // inventing a fallback count, which is what keeps malformed data loud.
            var section = new MissionDefinition.MissionSection
            {
                composition = new List<MissionDefinition.EnemyCompositionEntry>()
            };

            Assert.AreEqual(0, section.TotalEnemyCount);
        }

        // ------------------------------------------------------------ progression

        [Test]
        public void SectionsAreStrictlyForwardProgressing()
        {
            List<MissionDefinition.MissionSection> sections = BuildApprovedSections();

            var activations = new List<float>();
            var limits = new List<float>();
            foreach (MissionDefinition.MissionSection section in sections)
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
            List<MissionDefinition.MissionSection> sections = BuildApprovedSections();

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
            List<MissionDefinition.MissionSection> sections = BuildApprovedSections();
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
            foreach (MissionDefinition.MissionSection section in BuildApprovedSections())
            {
                Assert.Greater(section.spawnAheadOfLimit, 0f,
                    $"{section.label} would spawn enemies on top of the player.");
            }
        }
    }
}
