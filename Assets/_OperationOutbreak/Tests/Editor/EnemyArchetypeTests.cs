using System.Collections.Generic;
using NUnit.Framework;
using OperationOutbreak.EditorTools;
using OperationOutbreak.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1S - EditMode regression tests for the data-driven enemy
    /// archetype/variant architecture. They pin:
    ///   - ONE reusable gameplay framework (ZombieController stays the single
    ///     enemy authority; no per-variant controller classes exist);
    ///   - the Basic archetype preserves the VERIFIED 1Q values byte-for-byte;
    ///   - the Runner archetype is pure data over the same framework (run
    ///     locomotion profile resolving the reserved zombie run clip);
    ///   - stable ids are unique, invalid definitions are rejected loudly;
    ///   - the spawner seam resolves archetypes by id and defaults to Basic;
    ///   - the hybrid ragdoll death stays shared across production archetypes.
    /// </summary>
    public sealed class EnemyArchetypeTests
    {
        // ------------------------------------------------------------------ helpers

        private static EnemyArchetypeDefinition CreateDefinition(
            string id, string displayName, int health, float speed, int damage,
            float interval, float range, float separationRadius, float separationStrength,
            string profile, string resourcesPath)
        {
            EnemyArchetypeDefinition definition =
                ScriptableObject.CreateInstance<EnemyArchetypeDefinition>();

            var so = new SerializedObject(definition);
            so.FindProperty("archetypeId").stringValue = id;
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("maxHealth").intValue = health;
            so.FindProperty("moveSpeed").floatValue = speed;
            so.FindProperty("attackDamage").intValue = damage;
            so.FindProperty("attackInterval").floatValue = interval;
            so.FindProperty("attackRange").floatValue = range;
            so.FindProperty("separationRadius").floatValue = separationRadius;
            so.FindProperty("separationStrength").floatValue = separationStrength;
            so.FindProperty("productionPrefabPath").stringValue =
                "Assets/ArtStore3D/Stylized Zombie/Prefab/StylizedZombie_01.prefab";
            so.FindProperty("locomotionProfileName").stringValue = profile;
            so.FindProperty("locomotionResourcesPath").stringValue = resourcesPath;
            so.FindProperty("locomotionReferenceSpeed").floatValue = 1.3f;
            so.FindProperty("requiresRagdoll").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            return definition;
        }

        private static EnemyArchetypeDefinition LoadDefinition(string archetypeId)
        {
            foreach (EnemyArchetypeDefinition definition in
                     EnemyArchetypeEditorTools.LoadAllArchetypeDefinitions())
            {
                if (definition.ArchetypeId == archetypeId)
                {
                    return definition;
                }
            }

            return null;
        }

        private static bool HasProblem(List<string> problems, string fragment)
        {
            foreach (string problem in problems)
            {
                if (problem.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ tests

        [Test]
        public void BasicArchetypePreservesVerifiedGameplayValues()
        {
            // The Basic Infected must be driven through the new architecture with
            // ZERO numerical change: these are the verified 1Q serialized values.
            EnemyArchetypeDefinition basic = LoadDefinition("basic_infected");

            Assert.IsNotNull(basic, "The Basic archetype asset must exist.");

            Assert.AreEqual(3, basic.MaxHealth,
                "Basic max health must stay 3 (verified).");
            Assert.AreEqual(2.5f, basic.MoveSpeed, 0.0001f,
                "Basic move speed must stay 2.5 (verified).");
            Assert.AreEqual(1, basic.AttackDamage,
                "Basic attack damage must stay 1 (verified).");
            Assert.AreEqual(1f, basic.AttackInterval, 0.0001f,
                "Basic attack interval must stay 1 (verified).");
            Assert.AreEqual(1.25f, basic.AttackRange, 0.0001f,
                "Basic attack range must stay 1.25 (verified).");
            Assert.AreEqual(1.1f, basic.SeparationRadius, 0.0001f,
                "Basic separation radius must stay 1.1 (verified).");
            Assert.AreEqual(1.5f, basic.SeparationStrength, 0.0001f,
                "Basic separation strength must stay 1.5 (verified).");
        }

        [Test]
        public void BasicArchetypeUsesTheProductionBasicPresentation()
        {
            // Basic keeps the verified production presentation: the production
            // Stylized Zombie source, the WALK locomotion profile and the SHARED
            // prefab's authored controller (no swap, no regression risk). Its
            // cadence reference matches the VERIFIED prefab serialization.
            EnemyArchetypeDefinition basic = LoadDefinition("basic_infected");

            Assert.IsNotNull(basic, "The Basic archetype asset must exist.");
            Assert.AreEqual(
                EnemyVisualSetup.ProductionPrefabPath,
                basic.ProductionPrefabPath,
                "Basic must declare the production Stylized Zombie prefab source.");
            Assert.AreEqual(
                EnemyArchetypeDefinition.WalkProfile,
                basic.LocomotionProfileName,
                "Basic must use the Walk locomotion profile.");
            Assert.AreEqual(
                string.Empty,
                basic.LocomotionResourcesPath,
                "Basic must keep the shared prefab's authored controller (no swap).");
            Assert.AreEqual(
                0.29091793f,
                basic.LocomotionReferenceSpeed,
                0.000001f,
                "Basic cadence reference must match the verified prefab value.");
            Assert.IsTrue(basic.RequiresRagdoll,
                "Basic is a production archetype and must require the shared ragdoll.");
        }

        [Test]
        public void RunnerArchetypeUsesTheSharedGameplayController()
        {
            // The single shared framework applies the Runner's data: same
            // ZombieController class, values read from the definition, and a
            // null definition leaves the verified defaults untouched.
            //
            // 1S QA fix #1 - the fixture must represent the MINIMUM valid
            // shared enemy root: ZombieController carries
            // RequireComponent(typeof(Collider)), so the gameplay CapsuleCollider
            // (the collider the real Zombie_Prototype prefab uses) is added
            // FIRST. The RequireComponent rule is deliberately NOT weakened -
            // the fixture now satisfies it, exactly like a real spawn does.
            GameObject holder = new GameObject("ArchetypeApplicationCheck");
            holder.AddComponent<CapsuleCollider>();
            ZombieController zombie = holder.AddComponent<ZombieController>();

            try
            {
                // Fresh instance carries the verified defaults.
                Assert.AreEqual(2.5f, zombie.MoveSpeed, 0.0001f,
                    "The shared controller must keep its verified default speed.");
                Assert.AreEqual(3, zombie.MaxHealth,
                    "The shared controller must keep its verified default health.");

                EnemyArchetypeDefinition runner = CreateDefinition(
                    "runner", "Runner", 2, 4.5f, 1, 1f, 1.25f, 1.1f, 1.5f,
                    EnemyArchetypeDefinition.RunProfile, "EnemyArchetypes/OO_Runner");

                try
                {
                    zombie.ApplyArchetype(runner);

                    Assert.AreEqual(4.5f, zombie.MoveSpeed, 0.0001f,
                        "The Runner's data-driven speed must apply through the SHARED controller.");
                    Assert.AreEqual(2, zombie.MaxHealth,
                        "The Runner's data-driven health must apply through the SHARED controller.");
                    Assert.AreEqual(2, zombie.CurrentHealth,
                        "Applying the archetype at spawn re-seeds health from its max health.");
                    Assert.AreEqual(1, zombie.AttackDamage,
                        "The shared attack damage value applies.");
                    Assert.AreEqual(1.25f, zombie.AttackRange, 0.0001f,
                        "The shared attack range value applies.");
                }
                finally
                {
                    Object.DestroyImmediate(runner);
                }

                // A null definition is a deliberate no-op (verified defaults stay).
                zombie.ApplyArchetype(null);
                Assert.AreEqual(4.5f, zombie.MoveSpeed, 0.0001f,
                    "Applying null must be a no-op - the last applied values stay.");
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void RunnerLocomotionResolvesTheRunClipAndProfile()
        {
            // The Runner's locomotion is DATA: its profile maps to the imported
            // zombie run clip through the shared controller tool - no gameplay
            // code branch exists anywhere.
            EnemyArchetypeDefinition runner = LoadDefinition("runner");

            Assert.IsNotNull(runner, "The Runner archetype asset must exist.");
            Assert.AreEqual(
                EnemyArchetypeDefinition.RunProfile,
                runner.LocomotionProfileName,
                "The Runner must declare the Run locomotion profile.");
            Assert.AreEqual(
                EnemyAnimationSetup.RunFbxPath,
                EnemyAnimationSetup.ResolveLocomotionClipPath(EnemyArchetypeDefinition.RunProfile),
                "The Run profile must map to the zombie run FBX.");
            Assert.AreEqual(
                EnemyAnimationSetup.WalkFbxPath,
                EnemyAnimationSetup.ResolveLocomotionClipPath(EnemyArchetypeDefinition.WalkProfile),
                "The Walk profile must map to the zombie walk FBX.");
            Assert.IsNull(
                EnemyAnimationSetup.ResolveLocomotionClipPath("unknown_profile"),
                "An unknown profile must resolve to nothing (callers report it).");

            Assert.IsNotNull(
                EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.RunFbxPath),
                "The imported zombie run clip must resolve in the project.");
        }

        [Test]
        public void ArchetypeStableIdsAreUnique()
        {
            // Every committed archetype asset has a unique stable id, and the
            // pure duplicate detector finds fabricated duplicates.
            List<EnemyArchetypeDefinition> all =
                EnemyArchetypeEditorTools.LoadAllArchetypeDefinitions();

            Assert.GreaterOrEqual(all.Count, 2,
                "At least the Basic and Runner archetype assets must exist.");

            List<string> duplicates = EnemyArchetypeDefinition.FindDuplicateArchetypeIds(all);
            Assert.AreEqual(0, duplicates.Count,
                "Committed archetype ids must be unique (found duplicates).");

            // Pure detector behaviour with fabricated duplicates.
            EnemyArchetypeDefinition first = CreateDefinition(
                "dup", "First", 3, 2.5f, 1, 1f, 1.25f, 1.1f, 1.5f,
                EnemyArchetypeDefinition.WalkProfile, string.Empty);
            EnemyArchetypeDefinition second = CreateDefinition(
                "dup", "Second", 2, 4.5f, 1, 1f, 1.25f, 1.1f, 1.5f,
                EnemyArchetypeDefinition.RunProfile, "EnemyArchetypes/OO_Runner");

            try
            {
                var fabricated = new List<EnemyArchetypeDefinition> { first, second };
                List<string> found = EnemyArchetypeDefinition.FindDuplicateArchetypeIds(fabricated);

                Assert.AreEqual(1, found.Count,
                    "Two definitions sharing an id must be detected.");
                Assert.AreEqual("dup", found[0], "The duplicated id must be reported.");
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void InvalidArchetypesAreRejectedByValidation()
        {
            // A broken archetype must fail clearly - never spawn silently.
            EnemyArchetypeDefinition missingId = CreateDefinition(
                string.Empty, "No Id", 3, 2.5f, 1, 1f, 1.25f, 1.1f, 1.5f,
                EnemyArchetypeDefinition.WalkProfile, string.Empty);
            EnemyArchetypeDefinition zeroHealth = CreateDefinition(
                "zero_health", "Zero Health", 0, 2.5f, 1, 1f, 1.25f, 1.1f, 1.5f,
                EnemyArchetypeDefinition.WalkProfile, string.Empty);
            EnemyArchetypeDefinition absurdSpeed = CreateDefinition(
                "absurd_speed", "Absurd Speed", 3, 100f, 1, 1f, 1.25f, 1.1f, 1.5f,
                EnemyArchetypeDefinition.WalkProfile, string.Empty);
            EnemyArchetypeDefinition zeroDamage = CreateDefinition(
                "zero_damage", "Zero Damage", 3, 2.5f, 0, 1f, 1.25f, 1.1f, 1.5f,
                EnemyArchetypeDefinition.WalkProfile, string.Empty);
            EnemyArchetypeDefinition runnerWithoutController = CreateDefinition(
                "runner_no_controller", "Runner No Controller", 2, 4.5f, 1, 1f, 1.25f, 1.1f, 1.5f,
                EnemyArchetypeDefinition.RunProfile, string.Empty);
            EnemyArchetypeDefinition unknownProfile = CreateDefinition(
                "unknown_profile", "Unknown Profile", 3, 2.5f, 1, 1f, 1.25f, 1.1f, 1.5f,
                "Gallop", string.Empty);
            EnemyArchetypeDefinition valid = CreateDefinition(
                "valid", "Valid", 3, 2.5f, 1, 1f, 1.25f, 1.1f, 1.5f,
                EnemyArchetypeDefinition.WalkProfile, string.Empty);

            try
            {
                List<string> missingIdProblems =
                    EnemyArchetypeDefinition.CollectDefinitionProblems(missingId);
                Assert.IsTrue(HasProblem(missingIdProblems, "stable archetype id"),
                    "A missing id must be rejected.");

                List<string> zeroHealthProblems =
                    EnemyArchetypeDefinition.CollectDefinitionProblems(zeroHealth);
                Assert.IsTrue(HasProblem(zeroHealthProblems, "maxHealth"),
                    "Zero health must be rejected.");

                List<string> absurdSpeedProblems =
                    EnemyArchetypeDefinition.CollectDefinitionProblems(absurdSpeed);
                Assert.IsTrue(HasProblem(absurdSpeedProblems, "moveSpeed"),
                    "An absurd speed must be rejected.");

                List<string> zeroDamageProblems =
                    EnemyArchetypeDefinition.CollectDefinitionProblems(zeroDamage);
                Assert.IsTrue(HasProblem(zeroDamageProblems, "attackDamage"),
                    "Zero attack damage must be rejected.");

                List<string> runnerWithoutControllerProblems =
                    EnemyArchetypeDefinition.CollectDefinitionProblems(runnerWithoutController);
                Assert.IsTrue(HasProblem(runnerWithoutControllerProblems, "locomotion"),
                    "A Run profile without its locomotion controller must be rejected.");

                List<string> unknownProfileProblems =
                    EnemyArchetypeDefinition.CollectDefinitionProblems(unknownProfile);
                Assert.IsTrue(HasProblem(unknownProfileProblems, "Unknown locomotion profile"),
                    "An unknown locomotion profile must be rejected.");

                Assert.AreEqual(0,
                    EnemyArchetypeDefinition.CollectDefinitionProblems(valid).Count,
                    "A fully valid definition must produce no problems.");
            }
            finally
            {
                Object.DestroyImmediate(missingId);
                Object.DestroyImmediate(zeroHealth);
                Object.DestroyImmediate(absurdSpeed);
                Object.DestroyImmediate(zeroDamage);
                Object.DestroyImmediate(runnerWithoutController);
                Object.DestroyImmediate(unknownProfile);
                Object.DestroyImmediate(valid);
            }
        }

        [Test]
        public void SpawnerSeamResolvesBasicByArchetypeId()
        {
            // The spawner seam resolves a request by STABLE id through the
            // registry - the exact lookup the future 1T mission definitions
            // will use.
            Assert.IsTrue(
                EnemyArchetypeRegistry.TryGetArchetype("basic_infected", out EnemyArchetypeDefinition basic),
                "The registry must resolve 'basic_infected'.");
            Assert.AreEqual("basic_infected", basic.ArchetypeId,
                "The resolved definition must be the Basic archetype.");

            EnemyArchetypeDefinition resolved =
                EnemyArchetypeRegistry.ResolveRequestedArchetype("basic_infected");
            Assert.IsNotNull(resolved, "Resolving by id must return the definition.");
            Assert.AreEqual("basic_infected", resolved.ArchetypeId,
                "The resolved definition must carry the requested id.");
        }

        [Test]
        public void GameplayDefaultsToBasicAndPreservesExistingSpawnBehavior()
        {
            // Any spawn request that does not name an archetype (or names an
            // unknown one) resolves to the DEFAULT - the verified Basic Infected -
            // exactly like the pre-1S spawn path behaved.
            Assert.AreEqual(
                "basic_infected",
                EnemyArchetypeRegistry.DefaultArchetypeId,
                "The default archetype must be the verified Basic Infected.");

            EnemyArchetypeDefinition fromNull =
                EnemyArchetypeRegistry.ResolveRequestedArchetype(null);
            Assert.IsNotNull(fromNull, "A null request must resolve to the default.");
            Assert.AreEqual("basic_infected", fromNull.ArchetypeId,
                "A null request must resolve to Basic.");

            EnemyArchetypeDefinition fromEmpty =
                EnemyArchetypeRegistry.ResolveRequestedArchetype(string.Empty);
            Assert.AreEqual("basic_infected", fromEmpty.ArchetypeId,
                "An empty request must resolve to Basic.");

            EnemyArchetypeDefinition fromUnknown;

            // 1S QA fix #4 - the unknown-id fallback is CORRECT behaviour, and
            // the registry deliberately logs an Error diagnostic so a typo in
            // authoring data is visible. The test must prove BOTH: the
            // diagnostic IS emitted, and the fallback still resolves to Basic.
            //
            // LogAssert.Expect(LogType, string) compares the FULL message passed
            // to Debug.LogError - the QA fix #1 partial string
            // ("Unknown archetype id ...") therefore never matched in real
            // Unity. The expectation below is the production message VERBATIM
            // (EnemyArchetypeRegistry.ResolveRequestedArchetype). The "[Error]"
            // prefix is the console's rendered level marker, NOT part of the
            // matched message - the level is covered by LogType.Error.
            LogAssert.Expect(
                LogType.Error,
                "[1S] Unknown archetype id 'typo_or_unknown' - falling back to the default " +
                "('basic_infected'). Check the spawn request.");

            fromUnknown = EnemyArchetypeRegistry.ResolveRequestedArchetype("typo_or_unknown");

            Assert.IsNotNull(fromUnknown,
                "An unknown id must degrade to the default enemy, never nothing.");
            Assert.AreEqual("basic_infected", fromUnknown.ArchetypeId,
                "An unknown id must fall back to Basic (the 1N fallback rule).");

            // 1S QA fix #4 - additionally prove the expectation actually MATCHED
            // (if the diagnostic had not fired, the Expect would remain
            // unfulfilled and this would fail) and that nothing else logged
            // unexpectedly during the test.
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void VariantDataDoesNotDuplicateGameplayControllerClasses()
        {
            // There remains ONE reusable gameplay enemy framework: exactly one
            // *ZombieController type may exist in the enemy namespace - no
            // BasicZombieController / RunnerZombieController / FastZombieController.
            System.Type[] types = typeof(ZombieController).Assembly.GetTypes();
            int controllerClasses = 0;

            foreach (System.Type type in types)
            {
                if (type.Namespace == "OperationOutbreak.Enemies" &&
                    type.Name.EndsWith("ZombieController"))
                {
                    controllerClasses++;
                }
            }

            Assert.AreEqual(1, controllerClasses,
                "Exactly ONE enemy gameplay controller class may exist - variant " +
                "differences must come from data, never from duplicated classes.");

            // And the per-variant names the brief forbids simply do not exist.
            Assert.IsNull(
                System.Type.GetType("OperationOutbreak.Enemies.BasicZombieController"),
                "BasicZombieController must not exist.");
            Assert.IsNull(
                System.Type.GetType("OperationOutbreak.Enemies.RunnerZombieController"),
                "RunnerZombieController must not exist.");
            Assert.IsNull(
                System.Type.GetType("OperationOutbreak.Enemies.FastZombieController"),
                "FastZombieController must not exist.");
        }

        [Test]
        public void HybridRagdollDeathRemainsSharedAcrossProductionArchetypes()
        {
            // Death stays a SHARED system: both production archetypes require
            // the same hybrid ragdoll on the SAME shared gameplay prefab, and
            // the prefab actually carries the verified framework (controller,
            // bridge, configured ragdoll).
            EnemyArchetypeDefinition basic = LoadDefinition("basic_infected");
            EnemyArchetypeDefinition runner = LoadDefinition("runner");

            Assert.IsNotNull(basic, "The Basic archetype asset must exist.");
            Assert.IsNotNull(runner, "The Runner archetype asset must exist.");

            Assert.IsTrue(basic.RequiresRagdoll,
                "Basic must require the shared hybrid ragdoll.");
            Assert.IsTrue(runner.RequiresRagdoll,
                "Runner must require the same shared hybrid ragdoll - no forked death.");

            Assert.AreEqual(
                basic.ProductionPrefabPath, runner.ProductionPrefabPath,
                "Both production archetypes must declare the same production visual source.");

            GameObject sharedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                EnemyVisualSetup.ZombiePrefabPath);

            Assert.IsNotNull(sharedPrefab, "The shared gameplay enemy prefab must exist.");
            Assert.IsNotNull(sharedPrefab.GetComponent<ZombieController>(),
                "The shared prefab must carry the single gameplay authority.");
            Assert.IsNotNull(sharedPrefab.GetComponent<EnemyAnimationBridge>(),
                "The shared prefab must carry the presentation bridge.");
            Assert.IsNotNull(sharedPrefab.GetComponent<EnemyRagdoll>(),
                "The shared prefab must carry the hybrid ragdoll.");
            Assert.IsTrue(sharedPrefab.GetComponent<EnemyRagdoll>().IsConfigured,
                "The shared prefab's ragdoll must be configured (verified 1Q assets).");
        }

        [Test]
        public void RunnerControllerToolTargetsTheResourcesPath()
        {
            // The Runner's controller is authored into the Resources folder by
            // the shared controller tool, and the archetype's runtime load path
            // matches that location exactly - so the bridge's Resources.Load
            // resolves it once the asset is generated and committed.
            EnemyArchetypeDefinition runner = LoadDefinition("runner");

            Assert.IsNotNull(runner, "The Runner archetype asset must exist.");
            Assert.AreEqual(
                "EnemyArchetypes/OO_Runner",
                runner.LocomotionResourcesPath,
                "The Runner's runtime controller load path must be pinned.");

            Assert.AreEqual(
                "Assets/_OperationOutbreak/Resources/EnemyArchetypes/OO_Runner.controller",
                EnemyAnimationSetup.RunnerControllerPath,
                "The tool must author the Runner controller exactly where the " +
                "archetype loads it from.");

            string expectedEnd = runner.LocomotionResourcesPath + ".controller";
            Assert.IsTrue(
                EnemyAnimationSetup.RunnerControllerPath.EndsWith(expectedEnd),
                "The authoring path and the runtime Resources path must agree.");

            Assert.AreEqual(
                EnemyAnimationSetup.RunnerArchetypeAssetPath,
                "Assets/_OperationOutbreak/Resources/EnemyArchetypes/EnemyArchetype_Runner.asset",
                "The Runner archetype asset path must be pinned for the tool's cadence patch.");
        }
    }
}
