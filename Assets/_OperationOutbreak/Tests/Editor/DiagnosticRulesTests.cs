using System.Collections.Generic;
using NUnit.Framework;
using OperationOutbreak.Diagnostics;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1O - EditMode tests for the pure diagnostic rule predicates.
    ///
    /// These need no scene, no prefabs and no Play Mode: DiagnosticRules is plain C# with
    /// no Unity lifecycle, which is exactly why the rule maths was factored out of the
    /// recorder. They cover the placement and ordering guarantees the upgrade and spawn
    /// systems are supposed to honour.
    /// </summary>
    public sealed class DiagnosticRulesTests
    {
        // ------------------------------------------------------------ distance rules

        [Test]
        public void PlanarDistance_IgnoresHeightDifference()
        {
            // A pickup hovering at y = 1.15 must not read as "far from the player" just
            // because of hover height. All spacing rules are measured on the XZ plane.
            Vector3 a = new Vector3(0f, 0f, 0f);
            Vector3 b = new Vector3(3f, 25f, 4f);

            Assert.AreEqual(5f, DiagnosticRules.PlanarDistance(a, b), 0.0001f);
        }

        [Test]
        public void MeetsMinimumDistance_IsInclusiveAtTheBoundary()
        {
            Vector3 player = Vector3.zero;
            Vector3 exactlyThree = new Vector3(3f, 0f, 0f);
            Vector3 tooClose = new Vector3(2.9f, 0f, 0f);

            Assert.IsTrue(DiagnosticRules.MeetsMinimumDistance(exactlyThree, player, 3f),
                "A point exactly at the minimum distance satisfies the rule.");
            Assert.IsFalse(DiagnosticRules.MeetsMinimumDistance(tooClose, player, 3f));
        }

        [Test]
        public void IsWithinBounds_RejectsPointsOutsideTheLaneRectangle()
        {
            // Approved playable rectangle: half-width 3.6 on X.
            const float minX = -3.6f;
            const float maxX = 3.6f;
            const float minZ = -3f;
            const float maxZ = 35f;

            Assert.IsTrue(DiagnosticRules.IsWithinBounds(
                new Vector3(0f, 1.15f, 12f), minX, maxX, minZ, maxZ));
            Assert.IsFalse(DiagnosticRules.IsWithinBounds(
                new Vector3(6.3f, 1.15f, 12f), minX, maxX, minZ, maxZ),
                "A point on the boundary wall is outside the playable rectangle.");
            Assert.IsFalse(DiagnosticRules.IsWithinBounds(
                new Vector3(0f, 1.15f, 90f), minX, maxX, minZ, maxZ),
                "A point far beyond the forward limit is unreachable.");
        }

        [Test]
        public void NearestDistance_ReturnsNegativeOneForAnEmptySet()
        {
            // The first enemy of a section has nothing to overlap with, and that must not
            // be reported as an overlap at distance zero.
            Assert.AreEqual(-1f, DiagnosticRules.NearestDistance(
                Vector3.zero, new List<Vector3>()), 0.0001f);
        }

        [Test]
        public void IsOverlapping_FlagsSpawnsInsideTheClearanceRadius()
        {
            var existing = new List<Vector3> { new Vector3(0f, 1f, 35f) };

            Assert.IsTrue(DiagnosticRules.IsOverlapping(
                new Vector3(0.5f, 1f, 35f), existing, 1.4f));
            Assert.IsFalse(DiagnosticRules.IsOverlapping(
                new Vector3(2.5f, 1f, 35f), existing, 1.4f));
        }

        // ------------------------------------------------------------ upgrade order

        [Test]
        public void IsPermutation_AcceptsAShuffledFullSetAndRejectsARepeat()
        {
            // The run order must offer each authored upgrade exactly once.
            Assert.IsTrue(DiagnosticRules.IsPermutation(new List<int> { 2, 0, 3, 1 }, 4),
                "A shuffle of 0..3 is a valid run order.");
            Assert.IsFalse(DiagnosticRules.IsPermutation(new List<int> { 0, 1, 1, 3 }, 4),
                "A repeated upgrade is not a valid run order.");
            Assert.IsFalse(DiagnosticRules.IsPermutation(new List<int> { 0, 1, 2 }, 4),
                "A short order means an upgrade was dropped.");
        }

        [Test]
        public void HasDuplicates_DetectsARepeatedUpgradeIndex()
        {
            // Indices into the authored opportunity list: 0..3 = the four upgrades.
            Assert.IsFalse(DiagnosticRules.HasDuplicates(new List<int> { 2, 0, 3, 1 }));
            Assert.IsTrue(DiagnosticRules.HasDuplicates(new List<int> { 0, 1, 0 }),
                "The same upgrade offered twice in one run is a duplicate.");
        }

        [Test]
        public void AreSimultaneous_DetectsTwoPickupsResolvingAtTheSameInstant()
        {
            // One pickup at a time is a hard rule, so two resolutions must never coincide.
            Assert.IsTrue(DiagnosticRules.AreSimultaneous(10.00f, 10.02f));
            Assert.IsFalse(DiagnosticRules.AreSimultaneous(10.00f, 12.50f));
        }

        [Test]
        public void WindowsOverlap_DetectsTwoPickupsAliveAtOnce()
        {
            Assert.IsTrue(DiagnosticRules.WindowsOverlap(0f, 5f, 4f, 9f),
                "A pickup spawning before the previous one resolved is an overlap.");
            Assert.IsFalse(DiagnosticRules.WindowsOverlap(0f, 5f, 7f, 12f));
        }

        // ------------------------------------------------------------ mission shape

        [Test]
        public void IsStrictlyForwardProgressing_AcceptsTheApprovedSectionTable()
        {
            // Approved table: S1 act -100 / limit 15, S2 act 20 / limit 33,
            // S3 act 38 / limit 51. Each activation sits beyond the previous stop line,
            // so the player always has to walk forward to trigger the next section.
            var activations = new List<float> { -100f, 20f, 38f };
            var limits = new List<float> { 15f, 33f, 51f };

            Assert.IsTrue(DiagnosticRules.IsStrictlyForwardProgressing(activations, limits));
        }

        [Test]
        public void IsStrictlyForwardProgressing_RejectsAnActivationBehindThePreviousStopLine()
        {
            // This is the exact Milestone 1M bug: S2 activating at 12 while S1 stopped the
            // player at 15 fires the next section instantly, with no forward travel.
            var activations = new List<float> { -100f, 12f, 30f };
            var limits = new List<float> { 15f, 33f, 51f };

            Assert.IsFalse(DiagnosticRules.IsStrictlyForwardProgressing(activations, limits),
                "An activation behind the previous forward limit must fail.");
        }
    }
}
