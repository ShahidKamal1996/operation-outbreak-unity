using NUnit.Framework;
using OperationOutbreak.Enemies;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1O - EditMode tests that assert the Runner/Basic relationship on the REAL
    /// prefabs, loaded from disk with AssetDatabase. Nothing is instantiated into a scene
    /// and nothing is written back, so these tests cannot alter balance.
    ///
    /// They deliberately assert RELATIONSHIPS (faster, frailer, same damage) plus the
    /// currently approved absolute values, so an accidental rebalance is caught rather
    /// than silently accepted.
    /// </summary>
    public sealed class EnemyVarietyTests
    {
        private const string BasicPrefabPath =
            "Assets/_OperationOutbreak/Prefabs/Enemies/Zombie_Prototype.prefab";

        private const string RunnerPrefabPath =
            "Assets/_OperationOutbreak/Prefabs/Enemies/Runner_Prototype.prefab";

        private static ZombieController LoadEnemy(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"Expected an enemy prefab at {path}.");

            ZombieController controller = prefab.GetComponent<ZombieController>();
            Assert.IsNotNull(controller, $"{path} is missing its ZombieController.");
            return controller;
        }

        [Test]
        public void BothEnemyPrefabsExistAndCarryAZombieController()
        {
            Assert.IsNotNull(LoadEnemy(BasicPrefabPath));
            Assert.IsNotNull(LoadEnemy(RunnerPrefabPath));
        }

        [Test]
        public void RunnerMovesFasterThanBasic()
        {
            ZombieController basic = LoadEnemy(BasicPrefabPath);
            ZombieController runner = LoadEnemy(RunnerPrefabPath);

            Assert.Greater(runner.MoveSpeed, basic.MoveSpeed,
                "The Runner's whole identity is that it closes distance faster than a Basic.");
        }

        [Test]
        public void RunnerHasLessHealthThanBasic()
        {
            ZombieController basic = LoadEnemy(BasicPrefabPath);
            ZombieController runner = LoadEnemy(RunnerPrefabPath);

            Assert.Less(runner.MaxHealth, basic.MaxHealth,
                "The Runner trades durability for speed.");
        }

        [Test]
        public void RunnerDealsTheSameDamageAsBasic()
        {
            ZombieController basic = LoadEnemy(BasicPrefabPath);
            ZombieController runner = LoadEnemy(RunnerPrefabPath);

            Assert.AreEqual(basic.AttackDamage, runner.AttackDamage,
                "Milestone 1N kept contact damage identical across archetypes.");
        }

        [Test]
        public void ApprovedEnemyStatValuesAreUnchanged()
        {
            // Locks the approved 1N/1N.1 balance: Basic 2.5 / 3 HP / 1 dmg,
            // Runner 3.5 / 2 HP / 1 dmg. Milestone 1O must not move these.
            ZombieController basic = LoadEnemy(BasicPrefabPath);
            ZombieController runner = LoadEnemy(RunnerPrefabPath);

            Assert.AreEqual(2.5f, basic.MoveSpeed, 0.0001f, "Basic move speed changed.");
            Assert.AreEqual(3, basic.MaxHealth, "Basic max health changed.");
            Assert.AreEqual(1, basic.AttackDamage, "Basic attack damage changed.");

            Assert.AreEqual(3.5f, runner.MoveSpeed, 0.0001f, "Runner move speed changed.");
            Assert.AreEqual(2, runner.MaxHealth, "Runner max health changed.");
            Assert.AreEqual(1, runner.AttackDamage, "Runner attack damage changed.");
        }
    }
}
