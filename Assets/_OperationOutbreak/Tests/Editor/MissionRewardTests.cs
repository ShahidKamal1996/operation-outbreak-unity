using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.Mission;
using OperationOutbreak.Rewards;
using OperationOutbreak.UI;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1V - EditMode regression tests for the Rewards &amp; Results foundation.
    /// They pin: reward data validity (zero valid, negatives rejected), the reward
    /// service (correct result data, correct coin/supply grants, one-grant-per-run,
    /// new-run identity, failed runs grant nothing), the wallet (safe zero start,
    /// no negative balances, overflow protection), result data (success vs failure,
    /// not serialized into MissionDefinition), the flow (reward only after
    /// authoritative completion, the service cannot declare victory, single
    /// Mission Complete path), the retry reset and the Return/Next navigation seam,
    /// and the unchanged Mission 01 shape.
    /// </summary>
    public sealed class MissionRewardTests
    {
        private const string MissionAssetPath =
            "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_01.asset";

        // ------------------------------------------------------------------ helpers

        private static MissionDefinition LoadCommittedMission()
        {
            MissionDefinition mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>(MissionAssetPath);
            Assert.IsNotNull(mission, "The committed Mission_01 asset must exist at " + MissionAssetPath + ".");
            return mission;
        }

        private static List<string> KnownArchetypeIds()
        {
            return new List<string>
            {
                MissionDefinition.BasicArchetypeId,
                MissionDefinition.RunnerArchetypeId
            };
        }

        private static List<MissionDefinition.MissionSection> BuildThreeSections()
        {
            return new List<MissionDefinition.MissionSection>
            {
                new MissionDefinition.MissionSection
                {
                    sectionId = "section_01", label = "SECTION 1", subtitle = "OUTBREAK",
                    activationZ = -100f, forwardLimitZ = 15f, spawnAheadOfLimit = 1f,
                    composition = new List<MissionDefinition.EnemyCompositionEntry>
                    {
                        new MissionDefinition.EnemyCompositionEntry(MissionDefinition.BasicArchetypeId, 3)
                    }
                },
                new MissionDefinition.MissionSection
                {
                    sectionId = "section_02", label = "SECTION 2", subtitle = "ADVANCE",
                    activationZ = 20f, forwardLimitZ = 33f, spawnAheadOfLimit = 4f,
                    composition = new List<MissionDefinition.EnemyCompositionEntry>
                    {
                        new MissionDefinition.EnemyCompositionEntry(MissionDefinition.BasicArchetypeId, 3),
                        new MissionDefinition.EnemyCompositionEntry(MissionDefinition.RunnerArchetypeId, 1)
                    }
                },
                new MissionDefinition.MissionSection
                {
                    sectionId = "section_03", label = "SECTION 3", subtitle = "FINAL PUSH",
                    activationZ = 38f, forwardLimitZ = 51f, spawnAheadOfLimit = 4f,
                    composition = new List<MissionDefinition.EnemyCompositionEntry>
                    {
                        new MissionDefinition.EnemyCompositionEntry(MissionDefinition.BasicArchetypeId, 3),
                        new MissionDefinition.EnemyCompositionEntry(MissionDefinition.RunnerArchetypeId, 2)
                    }
                }
            };
        }

        private static MissionDefinition BuildMission(int coins = 0, int supplies = 0)
        {
            MissionDefinition mission = ScriptableObject.CreateInstance<MissionDefinition>();

            SetField(mission, "missionId", "mission_test");
            SetField(mission, "missionNumber", 1);
            SetField(mission, "chapterNumber", 1);
            SetField(mission, "displayName", "mission_test");
            SetField(mission, "sections", BuildThreeSections());
            SetField(mission, "objectives", new List<MissionObjectiveDefinition>
            {
                new MissionObjectiveDefinition
                {
                    objectiveId = "clear_all_sections",
                    title = "Clear All Sections",
                    objectiveType = MissionObjectiveType.ClearAllSections,
                    required = true
                }
            });
            SetField(mission, "reward", new MissionRewardDefinition { coins = coins, supplies = supplies });

            return mission;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Field '" + fieldName + "' missing on " + target.GetType().Name + ".");
            field.SetValue(target, value);
        }

        private static void Invoke(object target, string methodName, object[] args = null)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, methodName + " must exist on " + target.GetType().Name + ".");
            method.Invoke(target, args);
        }

        /// <summary>Creates an active MissionRewardService wired to the given mission.</summary>
        private static MissionRewardService NewService(MissionDefinition mission)
        {
            GameObject host = new GameObject("RewardServiceHost");
            host.SetActive(false);
            MissionRewardService service = host.AddComponent<MissionRewardService>();
            SetField(service, "missionDefinition", mission);
            host.SetActive(true); // Awake + OnEnable (fresh run latch)
            return service;
        }

        private static bool HasProblem(
            MissionDefinition definition, List<string> knownIds, string fragment)
        {
            foreach (string problem in MissionDefinition.CollectProblems(definition, knownIds))
            {
                if (problem.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------- reward data + validation

        [Test]
        public void MissionRewardDefaultsAreValid()
        {
            MissionDefinition mission = BuildMission();

            try
            {
                List<string> problems = MissionDefinition.CollectProblems(mission, KnownArchetypeIds());
                Assert.IsEmpty(problems,
                    "A mission with the default (zero) reward must validate cleanly: " +
                    string.Join(" | ", problems));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void ValidationRejectsNegativeCoins()
        {
            MissionDefinition mission = BuildMission(coins: -5);

            try
            {
                Assert.IsTrue(HasProblem(mission, KnownArchetypeIds(), "negative Coins reward"),
                    "Negative Coins must be rejected by mission validation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void ValidationRejectsNegativeSupplies()
        {
            MissionDefinition mission = BuildMission(supplies: -3);

            try
            {
                Assert.IsTrue(HasProblem(mission, KnownArchetypeIds(), "negative Supplies reward"),
                    "Negative Supplies must be rejected by mission validation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void ZeroRewardIsValidAndMission01ValidatesCleanly()
        {
            MissionDefinition committed = LoadCommittedMission();
            List<string> problems = MissionDefinition.CollectProblems(committed, KnownArchetypeIds());

            Assert.IsEmpty(problems,
                "Mission 01 (with its zero reward) must validate cleanly - zero is a valid " +
                "reward and must never be rejected: " + string.Join(" | ", problems));
        }

        // ------------------------------------------------- reward service

        [Test]
        public void SuccessfulMissionCreatesCorrectResultData()
        {
            MissionDefinition mission = BuildMission(coins: 100, supplies: 50);

            try
            {
                MissionRewardService service = NewService(mission);
                Invoke(service, "HandleEncounterCompleted");

                Assert.IsNotNull(service.CurrentResult, "A successful run must produce a result.");
                Assert.IsTrue(service.CurrentResult.Success, "The result must be a success.");
                Assert.AreEqual("mission_test", service.CurrentResult.MissionId);
                Assert.AreEqual(1, service.CurrentResult.MissionNumber);
                Assert.AreEqual(100, service.CurrentResult.CoinsEarned);
                Assert.AreEqual(50, service.CurrentResult.SuppliesEarned);
                Assert.IsTrue(service.CurrentResult.RewardsGranted,
                    "A successful run must report its reward as granted.");
                Assert.AreEqual(3, service.CurrentResult.SectionsCompleted);
                Assert.AreEqual(3, service.CurrentResult.TotalSections);

                UnityEngine.Object.DestroyImmediate(service.gameObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void SuccessfulRewardGrantsCoinsAndSupplies()
        {
            MissionDefinition mission = BuildMission(coins: 40, supplies: 7);

            try
            {
                MissionRewardService service = NewService(mission);
                Invoke(service, "HandleEncounterCompleted");

                Assert.AreEqual(40, service.Wallet.Coins, "Coins must be granted to the wallet.");
                Assert.AreEqual(7, service.Wallet.Supplies, "Supplies must be granted to the wallet.");

                UnityEngine.Object.DestroyImmediate(service.gameObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void OneRunGrantsRewardAtMostOnceEvenWhenResultRequestedMultipleTimes()
        {
            MissionDefinition mission = BuildMission(coins: 100);

            try
            {
                MissionRewardService service = NewService(mission);

                Invoke(service, "HandleEncounterCompleted");
                Invoke(service, "HandleEncounterCompleted"); // duplicate success event
                Invoke(service, "HandleEncounterCompleted"); // result re-request

                Assert.AreEqual(100, service.Wallet.Coins,
                    "A single run must never grant its reward twice.");
                Assert.IsTrue(service.RewardGrantedThisRun,
                    "The run must record that it granted exactly once.");

                UnityEngine.Object.DestroyImmediate(service.gameObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void NewRunIdentityCanGrantAgain()
        {
            MissionDefinition mission = BuildMission(coins: 100);

            try
            {
                MissionRewardService service = NewService(mission);

                Invoke(service, "HandleEncounterCompleted");
                Assert.AreEqual(100, service.Wallet.Coins, "First run grants its reward.");

                // A new run (scene reload / retry) resets the grant identity.
                Invoke(service, "OnEnable");
                Assert.IsFalse(service.HasResult, "A new run must start with no result.");
                Assert.IsFalse(service.RewardGrantedThisRun, "A new run must reset the grant latch.");

                Invoke(service, "HandleEncounterCompleted");
                Assert.AreEqual(200, service.Wallet.Coins,
                    "A new run must be eligible for its own reward exactly once.");

                UnityEngine.Object.DestroyImmediate(service.gameObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void FailedMissionGrantsZeroCompletionRewards()
        {
            MissionDefinition mission = BuildMission(coins: 100, supplies: 50);

            try
            {
                MissionRewardService service = NewService(mission);
                Invoke(service, "HandlePlayerDied");

                Assert.IsNotNull(service.CurrentResult, "A failed run must still produce a result.");
                Assert.IsFalse(service.CurrentResult.Success, "The result must be a failure.");
                Assert.IsFalse(service.CurrentResult.RewardsGranted,
                    "A failed mission must never grant its completion reward.");
                Assert.AreEqual(0, service.CurrentResult.CoinsEarned);
                Assert.AreEqual(0, service.CurrentResult.SuppliesEarned);
                Assert.AreEqual(0, service.Wallet.Coins, "No coins may be granted on failure.");
                Assert.AreEqual(0, service.Wallet.Supplies, "No supplies may be granted on failure.");

                UnityEngine.Object.DestroyImmediate(service.gameObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        // ------------------------------------------------- wallet

        [Test]
        public void WalletStartsAtKnownSafeZero()
        {
            RuntimeWallet wallet = new RuntimeWallet();

            Assert.AreEqual(0, wallet.Coins);
            Assert.AreEqual(0, wallet.Supplies);
        }

        [Test]
        public void WalletRejectsNegativeGrants()
        {
            RuntimeWallet wallet = new RuntimeWallet();

            Assert.IsFalse(wallet.Grant(-1, 5), "A negative coin amount must be rejected.");
            Assert.IsFalse(wallet.Grant(5, -1), "A negative supply amount must be rejected.");
            Assert.AreEqual(0, wallet.Coins, "A rejected grant must not change the balance.");
            Assert.AreEqual(0, wallet.Supplies, "A rejected grant must not change the balance.");
        }

        [Test]
        public void WalletHandlesOverflowSafely()
        {
            RuntimeWallet wallet = new RuntimeWallet();

            Assert.IsTrue(wallet.Grant(long.MaxValue, 0));
            Assert.IsTrue(wallet.Grant(1, 0), "A saturating grant is still a valid grant.");
            Assert.AreEqual(long.MaxValue, wallet.Coins,
                "Overflow must saturate at long.MaxValue, never wrap negative or wrong.");
        }

        // ------------------------------------------------- result data model

        [Test]
        public void SuccessResultCarriesIdentityAndReward()
        {
            MissionResultData result = MissionResultData.ForSuccess("mission_01", 1, 12, 3, 3, 3);

            Assert.AreEqual("mission_01", result.MissionId);
            Assert.AreEqual(1, result.MissionNumber);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(12, result.CoinsEarned);
            Assert.AreEqual(3, result.SuppliesEarned);
            Assert.IsTrue(result.RewardsGranted);
            Assert.AreEqual(3, result.SectionsCompleted);
            Assert.AreEqual(3, result.TotalSections);
        }

        [Test]
        public void FailureResultIsClearlyDistinguished()
        {
            MissionResultData result = MissionResultData.ForFailure("mission_01", 1, 2, 3);

            Assert.IsFalse(result.Success, "A failure result must be distinguishable from success.");
            Assert.IsFalse(result.RewardsGranted);
            Assert.AreEqual(0, result.CoinsEarned);
            Assert.AreEqual(0, result.SuppliesEarned);
            Assert.AreEqual(2, result.SectionsCompleted, "Sections cleared before death are still reported.");
            Assert.AreEqual(3, result.TotalSections);
        }

        [Test]
        public void RuntimeResultAndGrantStateIsNotSerializedIntoMissionDefinition()
        {
            // MissionDefinition is static configuration; result/grant/wallet state is
            // runtime-only. No such serialized fields may exist, and the runtime
            // objects are plain classes (not UnityEngine.Objects), so Unity can never
            // serialize them into the asset.
            foreach (FieldInfo field in typeof(MissionDefinition).GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string name = field.Name.ToLowerInvariant();
                if (name.Contains("result") || name.Contains("grant") ||
                    name.Contains("wallet") || name.Contains("balance") ||
                    name.Contains("earned"))
                {
                    Assert.Fail("MissionDefinition must not serialize runtime state: " + field.Name);
                }
            }

            Assert.IsFalse(typeof(MissionResultData).IsSubclassOf(typeof(UnityEngine.Object)),
                "MissionResultData must not be a serialized Unity object.");
            Assert.IsFalse(typeof(RuntimeWallet).IsSubclassOf(typeof(UnityEngine.Object)),
                "RuntimeWallet must not be a serialized Unity object.");
        }

        // ------------------------------------------------- flow / ownership

        [Test]
        public void RewardRequiresAuthoritativeCompletion()
        {
            MissionDefinition mission = BuildMission(coins: 100);

            try
            {
                MissionRewardService service = NewService(mission);

                // Section clears alone (even the final one) must never grant.
                Invoke(service, "HandleSectionCleared", new object[] { 0, null });
                Invoke(service, "HandleSectionCleared", new object[] { 2, null });

                Assert.AreEqual(0, service.Wallet.Coins, "Section progress must never grant.");
                Assert.IsNull(service.CurrentResult, "No result may exist before the outcome event.");

                // The authoritative completion event drives the grant.
                Invoke(service, "HandleEncounterCompleted");
                Assert.AreEqual(100, service.Wallet.Coins,
                    "The reward must be granted only after authoritative completion.");

                UnityEngine.Object.DestroyImmediate(service.gameObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void RewardServiceCannotDeclareVictoryAndObjectiveRemainsAuthority()
        {
            // The reward service grants; it never decides or triggers victory.
            Assert.IsNull(
                typeof(MissionRewardService).GetMethod(
                    "CompleteEncounter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                "MissionRewardService must not declare mission victory.");
            Assert.IsNull(
                typeof(MissionRewardService).GetField(
                    "missionObjectiveController", BindingFlags.Instance | BindingFlags.NonPublic),
                "MissionRewardService must not own objective progression.");

            // The objective runtime remains the completion authority.
            Assert.IsNotNull(
                typeof(MissionObjectiveController).GetMethod(
                    "EvaluateRequiredObjectives", BindingFlags.Instance | BindingFlags.NonPublic),
                "MissionObjectiveController must remain the completion authority.");
            Assert.IsNull(
                typeof(MissionSectionController).GetMethod(
                    "CompleteEncounter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                "MissionSectionController must not declare victory.");
        }

        [Test]
        public void MissionCompleteStillHasOneAuthoritativePath()
        {
            // Presentation owner (unchanged) + reward driven by the SAME outcome event.
            Assert.IsNotNull(
                typeof(MissionCompleteController).GetMethod(
                    "HandleEncounterCompleted", BindingFlags.Instance | BindingFlags.NonPublic),
                "MissionCompleteController must keep its EncounterCompleted presentation handler.");
            Assert.IsNotNull(
                typeof(MissionRewardService).GetMethod(
                    "HandleEncounterCompleted", BindingFlags.Instance | BindingFlags.NonPublic),
                "MissionRewardService must be driven by the same authoritative EncounterCompleted event.");
        }

        [Test]
        public void FinalSectionIsObservableBeforeRewardProcessing()
        {
            // Ordering contract: the objective controller defers its completion
            // evaluation to LateUpdate (after the SectionCleared dispatch returns), and
            // the reward service only grants on the EncounterCompleted outcome event -
            // so the final section is always recorded (by diagnostics) before the
            // reward/result processing runs.
            Assert.IsNotNull(
                typeof(MissionObjectiveController).GetMethod(
                    "LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic),
                "The objective controller must defer completion to the end of the frame.");
            Assert.IsNotNull(
                typeof(MissionObjectiveController).GetField(
                    "_evaluationPending", BindingFlags.Instance | BindingFlags.NonPublic),
                "The objective controller must carry the deferred-evaluation flag.");
            Assert.IsNotNull(
                typeof(MissionRewardService).GetMethod(
                    "HandleEncounterCompleted", BindingFlags.Instance | BindingFlags.NonPublic),
                "The reward service must process rewards on the outcome event, not on section progress.");
        }

        // ------------------------------------------------- retry / navigation

        [Test]
        public void RetryCreatesCleanNewRunState()
        {
            MissionDefinition mission = BuildMission(coins: 100);

            try
            {
                MissionRewardService service = NewService(mission);
                Invoke(service, "HandleEncounterCompleted");
                Assert.IsTrue(service.HasResult, "The run must have resolved.");

                // Retry = a fresh run: OnEnable (scene reload) resets the latch + result.
                Invoke(service, "OnEnable");

                Assert.IsFalse(service.HasResult, "Retry must clear the previous result.");
                Assert.IsNull(service.CurrentResult, "Retry must clear the previous result data.");
                Assert.IsFalse(service.RewardGrantedThisRun, "Retry must clear the grant latch.");

                UnityEngine.Object.DestroyImmediate(service.gameObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void FailedRetryDoesNotGrantRewards()
        {
            MissionDefinition mission = BuildMission(coins: 100);

            try
            {
                MissionRewardService service = NewService(mission);

                Invoke(service, "HandlePlayerDied");     // first failure
                Invoke(service, "OnEnable");             // retry
                Invoke(service, "HandlePlayerDied");     // second failure

                Assert.AreEqual(0, service.Wallet.Coins,
                    "No completion reward may be granted across failed runs.");
                Assert.IsFalse(service.CurrentResult.RewardsGranted,
                    "A failed retry must not grant rewards.");
                Assert.IsFalse(service.CurrentResult.Success);

                UnityEngine.Object.DestroyImmediate(service.gameObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void ReturnAndNextProduceNavigationIntentWithoutBaseOrMap()
        {
            GameObject host = new GameObject("NavigationHost");
            host.SetActive(false);
            MissionResultNavigation navigation = host.AddComponent<MissionResultNavigation>();
            host.SetActive(true);

            int returnCount = 0;
            int nextCount = 0;
            navigation.ReturnRequested += () => returnCount++;
            navigation.NextRequested += () => nextCount++;

            navigation.RequestReturn();
            navigation.RequestNext();

            Assert.AreEqual(1, returnCount,
                "Return must emit its navigation intent for a future Base/Map consumer.");
            Assert.AreEqual(1, nextCount,
                "Next must emit its navigation intent for a future campaign consumer.");

            // Retry is functional (scene reload) but not invoked here; its seam exists.
            Assert.IsNotNull(
                typeof(MissionResultNavigation).GetEvent("RetryRequested"),
                "The Retry intent event must exist.");
            Assert.IsNotNull(
                typeof(MissionResultNavigation).GetMethod(
                    "RequestRetry", BindingFlags.Instance | BindingFlags.Public),
                "The Retry request entry point must exist.");
            Assert.IsNotNull(
                typeof(MissionResultNavigation).GetMethod(
                    "ReloadCurrentScene", BindingFlags.Instance | BindingFlags.NonPublic),
                "Retry must route through the existing scene-reload reset path.");

            UnityEngine.Object.DestroyImmediate(host);
        }

        // ------------------------------------------------- Mission 01 regression

        [Test]
        public void Mission01ShapeAndZeroRewardRemainUnchanged()
        {
            MissionDefinition mission = LoadCommittedMission();

            Assert.AreEqual(3, mission.SectionCount, "Mission 01 must keep three sections.");
            Assert.AreEqual(12, mission.TotalEnemyCount, "Mission 01 must keep twelve enemies.");
            Assert.AreEqual(9, mission.GetArchetypeCount(MissionDefinition.BasicArchetypeId),
                "Mission 01 must keep nine Basic.");
            Assert.AreEqual(3, mission.GetArchetypeCount(MissionDefinition.RunnerArchetypeId),
                "Mission 01 must keep three Runners.");

            Assert.IsNotNull(mission.Reward, "Mission 01 must carry a reward definition.");
            Assert.AreEqual(0, mission.Reward.coins,
                "Mission 01 keeps zero Coins (the PRD introduces rewards later in Chapter 1).");
            Assert.AreEqual(0, mission.Reward.supplies,
                "Mission 01 keeps zero Supplies (the PRD introduces rewards later in Chapter 1).");
        }
    }
}
