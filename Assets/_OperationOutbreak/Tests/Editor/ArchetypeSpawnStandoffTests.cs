using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OperationOutbreak.Enemies;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1N.2 - EditMode tests for the per-archetype minimum spawn standoff.
    ///
    /// Two layers are covered:
    ///  * the pure clamp geometry in <see cref="EnemySpawnMath"/>, which is the single
    ///    definition of the spawn safety rules the spawner applies;
    ///  * the archetype configuration actually authored in the scene, read straight off disk,
    ///    so an accidental Inspector edit is caught rather than silently accepted.
    ///
    /// Nothing here instantiates or writes anything, so these tests cannot alter balance.
    /// </summary>
    public sealed class ArchetypeSpawnStandoffTests
    {
        private const string ScenePath =
            "Assets/_OperationOutbreak/Scenes/Gameplay_Prototype.unity";

        // The approved prototype values this milestone locks in.
        private const float BasicOffset = 0f;
        private const float BasicStandoff = 12f;
        private const float RunnerOffset = 5f;
        private const float RunnerStandoff = 6f;

        // ------------------------------------------------ archetype configuration model

        [Test]
        public void AnArchetypeWithoutAnOverrideInheritsTheGlobalStandoff()
        {
            var basic = new EnemyArchetype { id = EnemyArchetypeId.Basic };

            Assert.IsFalse(basic.HasStandoffOverride,
                "A default archetype must not claim an override.");
            Assert.AreEqual(12f, basic.ResolveMinimumStandoff(12f), 0.0001f,
                "Without an override the spawner's global default must be used unchanged.");
        }

        [Test]
        public void AnArchetypeWithAnOverrideUsesItInsteadOfTheGlobalStandoff()
        {
            var runner = new EnemyArchetype
            {
                id = EnemyArchetypeId.Runner,
                minimumSpawnStandoffOverride = RunnerStandoff
            };

            Assert.IsTrue(runner.HasStandoffOverride);
            Assert.AreEqual(RunnerStandoff, runner.ResolveMinimumStandoff(12f), 0.0001f,
                "The archetype's own standoff must win over the global default.");
        }

        [Test]
        public void AZeroOverrideIsTreatedAsAnOverrideNotAsUnset()
        {
            // Guards the sentinel choice: negative means inherit, so 0 must still be honoured.
            var archetype = new EnemyArchetype { minimumSpawnStandoffOverride = 0f };

            Assert.IsTrue(archetype.HasStandoffOverride);
            Assert.AreEqual(0f, archetype.ResolveMinimumStandoff(12f), 0.0001f);
        }

        // ------------------------------------------------------------ clamp geometry

        [Test]
        public void RunnerOffsetIsAppliedInFullWhenSafetyAllows()
        {
            // Section 3 centre band with the player still well back: the full 5 units fit
            // inside the Runner's 6 unit standoff, so nothing should be clamped away.
            float z = EnemySpawnMath.ClampForwardOffset(58f, 46.98f, RunnerOffset, RunnerStandoff);

            Assert.AreEqual(53f, z, 0.001f, "The full offset must survive.");
            Assert.AreEqual(RunnerOffset, 58f - z, 0.001f);
            Assert.GreaterOrEqual(z - 46.98f, RunnerStandoff - 0.001f,
                "The result must still respect the Runner standoff.");
        }

        [Test]
        public void RunnerOffsetIsAppliedPartiallyWhenThePlayerHasAdvancedTooFar()
        {
            // Section 2 centre band, the reported real case: only 4.81 of the 5 units fit.
            float z = EnemySpawnMath.ClampForwardOffset(40f, 29.19f, RunnerOffset, RunnerStandoff);

            float applied = 40f - z;

            Assert.Greater(applied, 0f, "A meaningful part of the offset must still apply.");
            Assert.Less(applied, RunnerOffset + 0.001f, "It must never exceed the request.");
            Assert.AreEqual(RunnerStandoff, z - 29.19f, 0.001f,
                "A partial offset must land exactly on the safety boundary, not past it.");
        }

        [Test]
        public void RunnerOffsetIsFullySuppressedWhenThePlayerIsAlreadyInsideTheStandoff()
        {
            // Player nearly on top of the band: the offset must be refused entirely rather
            // than forced, and the enemy stays on the authored band.
            float z = EnemySpawnMath.ClampForwardOffset(55f, 52f, RunnerOffset, RunnerStandoff);

            Assert.AreEqual(55f, z, 0.001f,
                "When no offset is safe the enemy must remain on its band.");
        }

        [Test]
        public void TheOffsetNeverPushesAnEnemyPastItsAuthoredBand()
        {
            // A player far behind must not cause the enemy to drift outward.
            float z = EnemySpawnMath.ClampForwardOffset(40f, -50f, RunnerOffset, RunnerStandoff);

            Assert.LessOrEqual(z, 40f + 0.001f, "The offset only ever pulls inward.");
            Assert.AreEqual(35f, z, 0.001f);
        }

        [Test]
        public void AnEnemyIsNeverPlacedOnTopOfOrBehindThePlayer()
        {
            // Sweep every band the mission authors against a wide range of player positions.
            float[] bands = { 16f, 19f, 37f, 40f, 55f, 58f };

            for (int b = 0; b < bands.Length; b++)
            {
                for (float playerZ = -10f; playerZ <= bands[b] + 10f; playerZ += 0.25f)
                {
                    float z = EnemySpawnMath.ClampForwardOffset(
                        bands[b], playerZ, RunnerOffset, RunnerStandoff);

                    Assert.LessOrEqual(z, bands[b] + 0.001f,
                        $"Band {bands[b]}, player {playerZ}: drifted past the band.");

                    // Either the standoff is respected, or the band itself was already closer
                    // than the standoff - in which case the enemy stays on the band and the
                    // offset contributed nothing.
                    bool respectsStandoff = z - playerZ >= RunnerStandoff - 0.001f;
                    bool stayedOnBand = Mathf.Abs(z - bands[b]) < 0.001f;

                    Assert.IsTrue(respectsStandoff || stayedOnBand,
                        $"Band {bands[b]}, player {playerZ}: unsafe spawn at z={z}.");
                }
            }
        }

        [Test]
        public void AZeroOffsetLeavesTheBandPositionExactlyUnchanged()
        {
            // This is the property that keeps the Basic zombie byte-identical: with offset 0
            // the clamp is an identity function for every player position.
            for (float playerZ = -10f; playerZ <= 30f; playerZ += 0.5f)
            {
                Assert.AreEqual(16f,
                    EnemySpawnMath.ClampForwardOffset(16f, playerZ, BasicOffset, BasicStandoff),
                    0.0001f,
                    $"Basic spawn moved for player z={playerZ}.");
            }
        }

        [Test]
        public void BasicStandoffWouldStillSuppressAFiveUnitOffset()
        {
            // Documents WHY the override was needed: at the global 12 unit standoff the very
            // same request the Runner makes is cancelled completely.
            float z = EnemySpawnMath.ClampForwardOffset(40f, 29.19f, RunnerOffset, BasicStandoff);

            Assert.AreEqual(40f, z, 0.001f,
                "With the 12 unit standoff the offset is fully clamped - the original bug.");
        }

        [Test]
        public void TheRunnerStandoffProducesACloserSpawnThanTheBasicStandoff()
        {
            float withRunnerStandoff =
                EnemySpawnMath.ClampForwardOffset(40f, 29.19f, RunnerOffset, RunnerStandoff);
            float withBasicStandoff =
                EnemySpawnMath.ClampForwardOffset(40f, 29.19f, RunnerOffset, BasicStandoff);

            Assert.Less(withRunnerStandoff, withBasicStandoff,
                "The whole point of the milestone: the Runner now genuinely enters closer.");
        }

        // ------------------------------------------------------ authored scene configuration

        private static string ReadArchetypeBlock()
        {
            Assert.IsTrue(File.Exists(ScenePath), $"Expected the gameplay scene at {ScenePath}.");

            string scene = File.ReadAllText(ScenePath);
            int start = scene.IndexOf("archetypes:", System.StringComparison.Ordinal);

            Assert.Greater(start, -1, "The spawner's archetype list is missing from the scene.");

            int end = scene.IndexOf("waveOneCount", start, System.StringComparison.Ordinal);
            Assert.Greater(end, start, "Could not delimit the archetype list.");

            return scene.Substring(start, end - start);
        }

        private static float ReadArchetypeFloat(string block, string archetypeId, string field)
        {
            // Grab the chunk belonging to this archetype id, then the field inside it.
            int idIndex = block.IndexOf("id: " + archetypeId, System.StringComparison.Ordinal);
            Assert.Greater(idIndex, -1, $"Archetype {archetypeId} is not authored in the scene.");

            int nextId = block.IndexOf("- id: ", idIndex + 1, System.StringComparison.Ordinal);
            string chunk = nextId > idIndex
                ? block.Substring(idIndex, nextId - idIndex)
                : block.Substring(idIndex);

            Match match = Regex.Match(chunk, field + @":\s*(-?[0-9.]+)");
            Assert.IsTrue(match.Success,
                $"Archetype {archetypeId} does not author {field}.");

            return float.Parse(match.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        [Test]
        public void SceneAuthorsTheApprovedBasicSpawnConfiguration()
        {
            string block = ReadArchetypeBlock();

            Assert.AreEqual(BasicOffset,
                ReadArchetypeFloat(block, EnemyArchetypeId.Basic, "spawnDistanceOffset"), 0.0001f,
                "The Basic zombie must keep a zero spawn offset.");

            Assert.AreEqual(BasicStandoff,
                ReadArchetypeFloat(block, EnemyArchetypeId.Basic, "minimumSpawnStandoffOverride"),
                0.0001f,
                "The Basic zombie must keep the 12 unit standoff.");
        }

        [Test]
        public void SceneAuthorsTheApprovedRunnerSpawnConfiguration()
        {
            string block = ReadArchetypeBlock();

            Assert.AreEqual(RunnerOffset,
                ReadArchetypeFloat(block, EnemyArchetypeId.Runner, "spawnDistanceOffset"), 0.0001f,
                "The Runner must keep its approved 5 unit spawn offset.");

            Assert.AreEqual(RunnerStandoff,
                ReadArchetypeFloat(block, EnemyArchetypeId.Runner, "minimumSpawnStandoffOverride"),
                0.0001f,
                "The Runner must use the 6 unit standoff that lets that offset apply.");
        }

        [Test]
        public void TheRunnerStandoffIsSmallerThanTheBasicStandoffButStillLeavesAWindow()
        {
            string block = ReadArchetypeBlock();

            float basic = ReadArchetypeFloat(
                block, EnemyArchetypeId.Basic, "minimumSpawnStandoffOverride");
            float runner = ReadArchetypeFloat(
                block, EnemyArchetypeId.Runner, "minimumSpawnStandoffOverride");

            Assert.Less(runner, basic, "The Runner is the archetype that gets to press closer.");
            Assert.Greater(runner, 0f, "A zero standoff would allow a spawn on top of the player.");
            Assert.GreaterOrEqual(runner, RunnerOffset,
                "The standoff must still exceed the offset, so the Runner cannot be spawned " +
                "closer than a full band-length shortcut.");
        }

        [Test]
        public void TheGlobalDefaultStandoffIsStillTwelve()
        {
            string scene = File.ReadAllText(ScenePath);

            Match match = Regex.Match(scene, @"\n  minimumSpawnStandoff:\s*([0-9.]+)");

            Assert.IsTrue(match.Success, "The spawner's global standoff is missing.");
            Assert.AreEqual(12f,
                float.Parse(match.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture),
                0.0001f,
                "The global fallback must remain 12 for any archetype that does not override.");
        }
    }
}
