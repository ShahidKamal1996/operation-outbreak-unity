using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.Mission;
using OperationOutbreak.UI;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1U - EditMode regression tests for the data-driven Objective
    /// Framework Foundation. They pin:
    ///   - Mission 01 carries exactly one REQUIRED ClearAllSections objective;
    ///   - required progress DERIVES from the mission's section count;
    ///   - progress starts at zero, increments once per section, never double-counts,
    ///     completes exactly when every section cleared and never early;
    ///   - required objectives gate completion while optional objectives never do;
    ///   - validation rejects null/empty/duplicate/unsupported objectives and a
    ///     mission with no required completion objective;
    ///   - runtime progress is NOT serialized into MissionDefinition;
    ///   - the verified Mission 01 shape (3 sections / 12 enemies / 9 Basic + 3
    ///     Runner) is preserved;
    ///   - no mission-specific controller duplication, and Mission Complete stays a
    ///     single authoritative path.
    /// </summary>
    public sealed class MissionObjectiveTests
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

        private static MissionObjectiveDefinition Objective(
            string id, bool required = true,
            MissionObjectiveType type = MissionObjectiveType.ClearAllSections)
        {
            return new MissionObjectiveDefinition
            {
                objectiveId = id,
                title = id,
                objectiveType = type,
                required = required
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

        /// <summary>Builds a valid mission (3 sections) with the given objective entries.</summary>
        private static MissionDefinition BuildMission(params MissionObjectiveDefinition[] objectives)
        {
            MissionDefinition mission = ScriptableObject.CreateInstance<MissionDefinition>();

            SetField(mission, "missionId", "mission_test");
            SetField(mission, "missionNumber", 1);
            SetField(mission, "chapterNumber", 1);
            SetField(mission, "displayName", "mission_test");
            SetField(mission, "sections", BuildThreeSections());
            SetField(mission, "objectives", new List<MissionObjectiveDefinition>(objectives));

            return mission;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Field '" + fieldName + "' missing on " + target.GetType().Name + ".");
            field.SetValue(target, value);
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

        // ------------------------------------------------- committed Mission 01 objective

        [Test]
        public void Mission01HasAtLeastOneObjective()
        {
            MissionDefinition mission = LoadCommittedMission();

            Assert.GreaterOrEqual(mission.ObjectiveCount, 1,
                "Mission 01 must carry at least one objective.");
        }

        [Test]
        public void Mission01HasExactlyOneRequiredObjective()
        {
            MissionDefinition mission = LoadCommittedMission();

            Assert.AreEqual(1, mission.RequiredObjectiveCount,
                "Mission 01 must gate completion on exactly one required objective.");
            Assert.IsTrue(mission.HasRequiredObjective,
                "Mission 01 must have a required objective.");
        }

        [Test]
        public void Mission01ObjectiveRepresentsClearAllSections()
        {
            MissionDefinition mission = LoadCommittedMission();

            MissionObjectiveDefinition objective = mission.GetObjective("clear_all_sections");
            Assert.IsNotNull(objective,
                "Mission 01 must define the 'clear_all_sections' objective.");
            Assert.AreEqual(MissionObjectiveType.ClearAllSections, objective.objectiveType,
                "Mission 01's objective must be the ClearAllSections type.");
            Assert.IsTrue(objective.required,
                "Mission 01's objective must be REQUIRED (it gates completion).");
        }

        [Test]
        public void ObjectiveRequiredProgressDerivesFromSectionCount()
        {
            MissionDefinition mission = LoadCommittedMission();
            MissionObjectiveDefinition objective = mission.GetObjective("clear_all_sections");

            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(objective, mission.SectionCount);

            Assert.AreEqual(mission.SectionCount, runtime.RequiredProgress,
                "ClearAllSections required progress must derive from the section count.");
            Assert.AreEqual(3, runtime.RequiredProgress,
                "Mission 01 has three sections, so the objective requires three clears.");
        }

        // ------------------------------------------------- runtime progress semantics

        [Test]
        public void ProgressBeginsAtZero()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective("clear_all_sections"), 3);

            Assert.AreEqual(0, runtime.CurrentProgress, "Progress must begin at zero.");
            Assert.IsFalse(runtime.IsComplete, "A fresh objective must not be complete.");
            Assert.AreEqual(0f, runtime.NormalizedProgress, 0.0001f,
                "Normalized progress must begin at zero.");
        }

        [Test]
        public void ProgressIncrementsWhenASectionClears()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective("clear_all_sections"), 3);

            runtime.RecordSectionCleared(0);

            Assert.AreEqual(1, runtime.CurrentProgress,
                "Clearing one section must advance progress by one.");
        }

        [Test]
        public void ProgressDoesNotIncrementTwiceForTheSameSection()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective("clear_all_sections"), 3);

            runtime.RecordSectionCleared(0);
            runtime.RecordSectionCleared(0);

            Assert.AreEqual(1, runtime.CurrentProgress,
                "A section must never count twice toward the objective.");
        }

        [Test]
        public void ObjectiveCompletesAfterAllSectionsClear()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective("clear_all_sections"), 3);

            runtime.RecordSectionCleared(0);
            runtime.RecordSectionCleared(1);
            runtime.RecordSectionCleared(2);

            Assert.IsTrue(runtime.IsComplete,
                "The objective must complete once every section has cleared.");
            Assert.AreEqual(3, runtime.CurrentProgress);
            Assert.AreEqual(1f, runtime.NormalizedProgress, 0.0001f);
        }

        [Test]
        public void ObjectiveDoesNotCompleteEarly()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective("clear_all_sections"), 3);

            runtime.RecordSectionCleared(0);
            runtime.RecordSectionCleared(1);

            Assert.IsFalse(runtime.IsComplete,
                "Clearing 2 of 3 sections must not complete the objective.");
        }

        // ------------------------------------------------- required vs optional gating

        [Test]
        public void RequiredObjectivesGateMissionCompletion()
        {
            MissionObjectiveRuntime required = new MissionObjectiveRuntime(
                Objective("clear_all_sections", required: true), 3);
            MissionObjectiveRuntime optional = new MissionObjectiveRuntime(
                Objective("bonus", required: false), 3);

            var objectives = new List<MissionObjectiveRuntime> { required, optional };

            Assert.IsFalse(MissionObjectiveRuntime.AllRequiredObjectivesComplete(objectives),
                "An incomplete required objective must hold completion.");

            required.RecordSectionCleared(0);
            required.RecordSectionCleared(1);
            required.RecordSectionCleared(2);

            Assert.IsTrue(MissionObjectiveRuntime.AllRequiredObjectivesComplete(objectives),
                "Once every required objective completes, completion must be allowed.");
        }

        [Test]
        public void OptionalObjectivesDoNotGateCompletion()
        {
            MissionObjectiveRuntime required = new MissionObjectiveRuntime(
                Objective("clear_all_sections", required: true), 3);
            MissionObjectiveRuntime optional = new MissionObjectiveRuntime(
                Objective("bonus", required: false), 3);

            required.RecordSectionCleared(0);
            required.RecordSectionCleared(1);
            required.RecordSectionCleared(2);

            // Optional objective remains incomplete - completion is still allowed.
            Assert.IsTrue(
                MissionObjectiveRuntime.AllRequiredObjectivesComplete(
                    new List<MissionObjectiveRuntime> { required, optional }),
                "An incomplete optional objective must never gate completion.");

            // Optional-only missions have no required objective -> never complete.
            Assert.IsFalse(
                MissionObjectiveRuntime.AllRequiredObjectivesComplete(
                    new List<MissionObjectiveRuntime> { optional }),
                "A mission with no required objective must not complete.");
        }

        // ------------------------------------------------- validation

        [Test]
        public void ValidationRejectsNullObjectiveEntries()
        {
            MissionDefinition mission = BuildMission((MissionObjectiveDefinition)null);

            try
            {
                Assert.IsTrue(HasProblem(mission, KnownArchetypeIds(), "objective entry 1 is null"),
                    "A null objective entry must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void ValidationRejectsEmptyObjectiveIds()
        {
            MissionDefinition mission = BuildMission(Objective(""));

            try
            {
                Assert.IsTrue(HasProblem(mission, KnownArchetypeIds(), "missing stable objective id"),
                    "An empty objective id must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void ValidationRejectsDuplicateObjectiveIds()
        {
            MissionDefinition mission = BuildMission(Objective("dup"), Objective("dup"));

            try
            {
                Assert.IsTrue(HasProblem(mission, KnownArchetypeIds(), "duplicate objective id"),
                    "Duplicate objective ids must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void ValidationRejectsUnsupportedObjectiveTypes()
        {
            MissionDefinition mission = BuildMission(
                Objective("future_type", required: true, type: (MissionObjectiveType)999));

            try
            {
                Assert.IsTrue(HasProblem(mission, KnownArchetypeIds(), "unsupported objective type"),
                    "An unsupported objective type must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mission);
            }
        }

        [Test]
        public void ValidationRejectsMissionWithNoRequiredObjective()
        {
            MissionDefinition noRequired = BuildMission(Objective("bonus", required: false));
            MissionDefinition noObjectives = BuildMission();

            try
            {
                Assert.IsTrue(HasProblem(noRequired, KnownArchetypeIds(), "no REQUIRED objective"),
                    "A mission whose objectives are all optional must be rejected.");
                Assert.IsTrue(HasProblem(noObjectives, KnownArchetypeIds(), "mission has no objectives"),
                    "A mission with no objectives must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(noRequired);
                UnityEngine.Object.DestroyImmediate(noObjectives);
            }
        }

        [Test]
        public void CommittedMissionObjectivesValidateCleanly()
        {
            MissionDefinition mission = LoadCommittedMission();
            List<string> problems = MissionDefinition.CollectProblems(mission, KnownArchetypeIds());

            Assert.IsEmpty(problems,
                "Mission 01 (with its objective) must validate cleanly: " +
                string.Join(" | ", problems));
        }

        // ------------------------------------------------- architecture invariants

        [Test]
        public void RuntimeObjectiveProgressIsNotSerializedIntoMissionDefinition()
        {
            // Runtime progress lives in MissionObjectiveRuntime, a plain class -
            // never in MissionDefinition (static configuration only).
            Assert.IsFalse(typeof(MissionObjectiveRuntime).IsSubclassOf(typeof(UnityEngine.Object)),
                "Runtime objective progress must not be a Unity asset/serialized object.");

            foreach (FieldInfo field in typeof(MissionDefinition).GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.Name.Contains("Progress", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Fail("MissionDefinition must not serialize runtime progress: " + field.Name);
                }
            }
        }

        [Test]
        public void Mission01BehaviorRemainsThreeSectionsTwelveEnemiesNineBasicThreeRunner()
        {
            MissionDefinition mission = LoadCommittedMission();

            Assert.AreEqual(3, mission.SectionCount,
                "Mission 01 must keep three sections.");
            Assert.AreEqual(12, mission.TotalEnemyCount,
                "Mission 01 must keep twelve enemies.");
            Assert.AreEqual(9, mission.GetArchetypeCount(MissionDefinition.BasicArchetypeId),
                "Mission 01 must keep nine Basic.");
            Assert.AreEqual(3, mission.GetArchetypeCount(MissionDefinition.RunnerArchetypeId),
                "Mission 01 must keep three Runners.");
        }

        [Test]
        public void ObjectiveFrameworkIntroducesNoMissionSpecificControllerDuplication()
        {
            Assembly assembly = typeof(MissionObjectiveController).Assembly;

            Assert.IsNull(assembly.GetType("OperationOutbreak.Mission.Mission01ObjectiveController"),
                "Mission01ObjectiveController must not exist.");
            Assert.IsNull(assembly.GetType("OperationOutbreak.Mission.KillMissionController"),
                "KillMissionController must not exist.");
            Assert.IsNull(assembly.GetType("OperationOutbreak.Mission.RunnerObjectiveController"),
                "RunnerObjectiveController must not exist.");
            Assert.IsNull(assembly.GetType("OperationOutbreak.Mission.Chapter1ObjectiveManager"),
                "Chapter1ObjectiveManager must not exist.");

            Assert.IsNotNull(typeof(MissionObjectiveController),
                "Exactly ONE generic MissionObjectiveController must exist.");
        }

        [Test]
        public void MissionCompleteRemainsSingleAuthoritativePath()
        {
            // Presentation owner (unchanged): MissionCompleteController listens to
            // EnemySpawner.EncounterCompleted through HandleEncounterCompleted.
            Assert.IsNotNull(
                typeof(MissionCompleteController).GetMethod(
                    "HandleEncounterCompleted", BindingFlags.Instance | BindingFlags.NonPublic),
                "MissionCompleteController must keep its EncounterCompleted presentation handler.");

            // Single completion gate: MissionObjectiveController evaluates required
            // objectives and triggers the victory presentation exactly once.
            Assert.IsNotNull(
                typeof(MissionObjectiveController).GetMethod(
                    "EvaluateRequiredObjectives", BindingFlags.Instance | BindingFlags.NonPublic),
                "MissionObjectiveController must own the completion-gate evaluation.");
            Assert.IsNotNull(
                typeof(MissionObjectiveController).GetEvent("AllRequiredObjectivesCompleted"),
                "MissionObjectiveController must expose the all-required-complete signal.");
            Assert.IsNotNull(
                typeof(MissionObjectiveController).GetField(
                    "enemySpawner", BindingFlags.Instance | BindingFlags.NonPublic),
                "MissionObjectiveController must trigger the shared spawner's victory path.");

            // MissionSectionController publishes section progress only; it must not
            // declare victory itself.
            Assert.IsNull(
                typeof(MissionSectionController).GetMethod(
                    "CompleteEncounter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                "MissionSectionController must not declare victory - it only publishes progress.");
        }
    }
}
