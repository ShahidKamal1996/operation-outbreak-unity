using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.Mission;
using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1X.5 - EditMode tests for the new objective types (SurviveDuration,
    /// DestroyTargets, ActivateTargets): data validation, runtime progress/dedup/completion/
    /// gating, the barricade target component, and the committed M1-M5 objective configurations.
    /// The single completion authority (MissionObjectiveController) and the existing
    /// ClearAllSections behaviour are untouched and covered by MissionObjectiveTests.
    /// </summary>
    public sealed class MissionObjectiveTypesTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        // ---------------------------------------------------------------- helpers

        private static MissionObjectiveDefinition Objective(MissionObjectiveType type, string id) =>
            new MissionObjectiveDefinition
            {
                objectiveId = id,
                title = id,
                objectiveType = type,
                required = true,
                durationSeconds = 5f,
                requiredTargetCount = 2,
                targetHealth = 3,
                activationDuration = 1.5f,
                activationRadius = 2f
            };

        private static void SetField(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "Field '" + name + "' missing on " + target.GetType().Name + ".");
            f.SetValue(target, value);
        }

        private MissionDefinition MissionWithSectionsAndObjectives(params MissionObjectiveDefinition[] objectives)
        {
            MissionDefinition mission = ScriptableObject.CreateInstance<MissionDefinition>();
            SetField(mission, "missionId", "mission_test");
            SetField(mission, "missionNumber", 1);
            SetField(mission, "chapterNumber", 1);
            SetField(mission, "displayName", "mission_test");

            List<MissionDefinition.MissionSection> sections = new List<MissionDefinition.MissionSection>
            {
                new MissionDefinition.MissionSection
                {
                    sectionId = "section_01",
                    label = "SECTION 1",
                    subtitle = "X",
                    activationZ = -100f,
                    forwardLimitZ = 15f,
                    spawnAheadOfLimit = 1f,
                    composition = new List<MissionDefinition.EnemyCompositionEntry>
                    {
                        new MissionDefinition.EnemyCompositionEntry(MissionDefinition.BasicArchetypeId, 2)
                    }
                },
                new MissionDefinition.MissionSection
                {
                    sectionId = "section_02",
                    label = "SECTION 2",
                    subtitle = "Y",
                    activationZ = 20f,
                    forwardLimitZ = 33f,
                    spawnAheadOfLimit = 4f,
                    composition = new List<MissionDefinition.EnemyCompositionEntry>
                    {
                        new MissionDefinition.EnemyCompositionEntry(MissionDefinition.BasicArchetypeId, 2)
                    }
                }
            };

            SetField(mission, "sections", sections);
            SetField(mission, "objectives", new List<MissionObjectiveDefinition>(objectives));
            _created.Add(mission);
            return mission;
        }

        private static List<string> KnownArchetypes() =>
            new List<string> { MissionDefinition.BasicArchetypeId, MissionDefinition.RunnerArchetypeId };

        private static bool HasProblem(List<string> problems, string fragment)
        {
            foreach (string p in problems)
            {
                if (p.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        // ============================================================ objective DATA validation

        [Test]
        public void SurviveDurationRejectsNonPositiveDuration()
        {
            MissionObjectiveDefinition o = Objective(MissionObjectiveType.SurviveDuration, "survive");
            o.durationSeconds = 0f;
            MissionDefinition m = MissionWithSectionsAndObjectives(o);

            Assert.IsTrue(HasProblem(
                MissionDefinition.CollectProblems(m, KnownArchetypes()), "durationSeconds > 0"));
        }

        [Test]
        public void DestroyTargetsRejectsNonPositiveRequiredCount()
        {
            MissionObjectiveDefinition o = Objective(MissionObjectiveType.DestroyTargets, "destroy");
            o.requiredTargetCount = 0;
            MissionDefinition m = MissionWithSectionsAndObjectives(o);

            Assert.IsTrue(HasProblem(
                MissionDefinition.CollectProblems(m, KnownArchetypes()), "requiredTargetCount > 0"));
        }

        [Test]
        public void DestroyTargetsRejectsNonPositiveTargetHealth()
        {
            MissionObjectiveDefinition o = Objective(MissionObjectiveType.DestroyTargets, "destroy");
            o.targetHealth = 0;
            MissionDefinition m = MissionWithSectionsAndObjectives(o);

            Assert.IsTrue(HasProblem(
                MissionDefinition.CollectProblems(m, KnownArchetypes()), "targetHealth > 0"));
        }

        [Test]
        public void ActivateTargetsRejectsNonPositiveCount()
        {
            MissionObjectiveDefinition o = Objective(MissionObjectiveType.ActivateTargets, "activate");
            o.requiredTargetCount = -1;
            MissionDefinition m = MissionWithSectionsAndObjectives(o);

            Assert.IsTrue(HasProblem(
                MissionDefinition.CollectProblems(m, KnownArchetypes()), "requiredTargetCount > 0"));
        }

        [Test]
        public void ActivateTargetsRejectsNonPositiveDurationAndRadius()
        {
            MissionObjectiveDefinition o = Objective(MissionObjectiveType.ActivateTargets, "activate");
            o.activationDuration = 0f;
            o.activationRadius = 0f;
            MissionDefinition m = MissionWithSectionsAndObjectives(o);
            List<string> problems = MissionDefinition.CollectProblems(m, KnownArchetypes());

            Assert.IsTrue(HasProblem(problems, "activationDuration > 0"));
            Assert.IsTrue(HasProblem(problems, "activationRadius > 0"));
        }

        [Test]
        public void ObjectiveSequencingRejectsMissingAndSelfPrerequisite()
        {
            MissionObjectiveDefinition a = Objective(MissionObjectiveType.ClearAllSections, "clear");
            MissionObjectiveDefinition b = Objective(MissionObjectiveType.ActivateTargets, "activate");
            b.activateAfterObjectiveId = "does_not_exist";
            MissionObjectiveDefinition c = Objective(MissionObjectiveType.SurviveDuration, "survive");
            c.activateAfterObjectiveId = "survive"; // self-reference
            MissionDefinition m = MissionWithSectionsAndObjectives(a, b, c);
            List<string> problems = MissionDefinition.CollectProblems(m, KnownArchetypes());

            Assert.IsTrue(HasProblem(problems, "does not match any objective id"));
            Assert.IsTrue(HasProblem(problems, "references itself"));
        }

        [Test]
        public void ValidNewObjectiveConfigurationsPassValidation()
        {
            MissionObjectiveDefinition survive = Objective(MissionObjectiveType.SurviveDuration, "survive");
            survive.durationSeconds = 12f;
            MissionDefinition m = MissionWithSectionsAndObjectives(survive);

            Assert.IsEmpty(MissionDefinition.CollectProblems(m, KnownArchetypes()),
                "A well-formed SurviveDuration objective must validate cleanly.");
        }

        // ============================================================ SURVIVE runtime

        [Test]
        public void SurvivalDoesNotCompleteBeforeDuration()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective(MissionObjectiveType.SurviveDuration, "survive"), 3);

            Assert.IsFalse(runtime.RecordSurviveTick(2f), "Should not complete before duration.");
            Assert.IsFalse(runtime.IsComplete);
            Assert.AreEqual(2f, runtime.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void SurvivalCompletesAtConfiguredDuration()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective(MissionObjectiveType.SurviveDuration, "survive"), 3);

            Assert.IsTrue(runtime.RecordSurviveTick(5f), "Should complete at/after duration.");
            Assert.IsTrue(runtime.IsComplete);
        }

        [Test]
        public void SurvivalDoesNotProgressWhileInactive()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective(MissionObjectiveType.SurviveDuration, "survive"), 3);
            runtime.Deactivate();

            Assert.IsFalse(runtime.RecordSurviveTick(10f));
            Assert.AreEqual(0f, runtime.ElapsedSeconds, 0.0001f);
            Assert.IsFalse(runtime.IsComplete);
        }

        [Test]
        public void SurvivalDoesNotDoubleComplete()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective(MissionObjectiveType.SurviveDuration, "survive"), 3);

            Assert.IsTrue(runtime.RecordSurviveTick(5f));
            Assert.IsFalse(runtime.RecordSurviveTick(5f), "A completed objective must not re-complete.");
            Assert.IsTrue(runtime.IsComplete);
        }

        [Test]
        public void SectionClearsDoNotSatisfySurvival()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective(MissionObjectiveType.SurviveDuration, "survive"), 3);

            Assert.IsFalse(runtime.RecordSectionCleared(0), "Survive ignores section clears.");
            Assert.IsFalse(runtime.IsComplete);
        }

        // ============================================================ DESTROY runtime (barricades)

        [Test]
        public void DestroyObjectiveCountsDistinctDestroyedTargets()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective(MissionObjectiveType.DestroyTargets, "destroy"), 3);

            Assert.IsFalse(runtime.RecordTargetDestroyed("b1"));
            Assert.AreEqual(1, runtime.CurrentProgress);
            Assert.IsFalse(runtime.RecordTargetDestroyed("b1"), "Same target counts once.");
            Assert.AreEqual(1, runtime.CurrentProgress);
            Assert.IsTrue(runtime.RecordTargetDestroyed("b2"));
            Assert.IsTrue(runtime.IsComplete);
        }

        [Test]
        public void DestroyObjectiveIgnoresEnemySectionClears()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective(MissionObjectiveType.DestroyTargets, "destroy"), 3);

            for (int i = 0; i < 3; i++)
            {
                runtime.RecordSectionCleared(i);
            }

            Assert.IsFalse(runtime.IsComplete, "Killing enemies (section clears) must not satisfy destroy objective.");
        }

        [Test]
        public void DestroyObjectiveDoesNotDoubleComplete()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective(MissionObjectiveType.DestroyTargets, "destroy"), 3);

            runtime.RecordTargetDestroyed("b1");
            Assert.IsTrue(runtime.RecordTargetDestroyed("b2"));
            Assert.IsFalse(runtime.RecordTargetDestroyed("b3"), "No re-complete after done.");
        }

        // ============================================================ ACTIVATE runtime

        [Test]
        public void ActivateObjectiveCountsDistinctActivatedTargets()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective(MissionObjectiveType.ActivateTargets, "activate"), 3);

            Assert.IsFalse(runtime.RecordTargetActivated("p1"));
            Assert.AreEqual(1, runtime.CurrentProgress);
            Assert.IsFalse(runtime.RecordTargetActivated("p1"), "Same point counts once.");
            Assert.IsTrue(runtime.RecordTargetActivated("p2"));
            Assert.IsTrue(runtime.IsComplete, "Required count gates completion.");
        }

        [Test]
        public void ActivateObjectiveIgnoresEnemySectionClears()
        {
            MissionObjectiveRuntime runtime = new MissionObjectiveRuntime(
                Objective(MissionObjectiveType.ActivateTargets, "activate"), 3);

            for (int i = 0; i < 3; i++)
            {
                runtime.RecordSectionCleared(i);
            }

            Assert.IsFalse(runtime.IsComplete, "Killing enemies must not satisfy activate objective.");
        }

        [Test]
        public void NewObjectiveTypesAreInactiveSafeUntilActivated()
        {
            MissionObjectiveRuntime destroy = new MissionObjectiveRuntime(
                Objective(MissionObjectiveType.DestroyTargets, "destroy"), 3);
            destroy.Deactivate();

            Assert.IsFalse(destroy.RecordTargetDestroyed("b1"), "Inactive objective accepts no progress.");
            Assert.AreEqual(0, destroy.CurrentProgress);
        }

        // ============================================================ BARRICADE target component

        [Test]
        public void BarricadeTargetTakesDamageAndRaisesDestroyedOnce()
        {
            GameObject go = new GameObject("Barricade",
                typeof(UnityEngine.BoxCollider));
            _created.Add(go);
            BarricadeTarget target = go.AddComponent<BarricadeTarget>();
            target.Configure("b_test", 3, null);

            int raised = 0;
            MissionObjectiveTargetEvents.TargetDestroyed += handler;
            try
            {
                Assert.IsTrue(target.IsAlive);
                Assert.AreEqual(3, target.CurrentHealth);

                target.TakeDamage(1);
                Assert.AreEqual(2, target.CurrentHealth, "Damage reduces health.");
                Assert.AreEqual(0, raised, "Not destroyed yet.");

                target.TakeDamage(2); // destroys
                Assert.IsFalse(target.IsAlive);
                Assert.AreEqual(1, raised, "Destruction raises the event exactly once.");

                target.TakeDamage(5); // already destroyed
                Assert.AreEqual(1, raised, "No second event after destruction.");
            }
            finally
            {
                MissionObjectiveTargetEvents.TargetDestroyed -= handler;
            }

            void handler(string id)
            {
                if (id == "b_test")
                {
                    raised++;
                }
            }
        }

        // ============================================================ committed M1-M5 objective configs

        private static T Load<T>(string path) where T : Object =>
            UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);

        [Test]
        public void Mission01UsesIntroClearObjective()
        {
            MissionDefinition m = Load<MissionDefinition>(
                "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_01.asset");
            Assert.IsNotNull(m);
            Assert.AreEqual(1, m.ObjectiveCount);
            Assert.AreEqual(MissionObjectiveType.ClearAllSections, m.Objectives[0].objectiveType);
        }

        [Test]
        public void Mission02EmphasizesRunnerPressure()
        {
            MissionDefinition m = Load<MissionDefinition>(
                "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_02.asset");
            Assert.IsNotNull(m);
            int runners = m.GetArchetypeCount(MissionDefinition.RunnerArchetypeId);
            int basics = m.GetArchetypeCount(MissionDefinition.BasicArchetypeId);
            Assert.GreaterOrEqual(runners, 5, "M2 should be runner-heavy (>= 5 runners) for pressure.");
            Assert.IsTrue(runners * 100 >= basics * 50, "Runners should be a large share of M2.");
        }

        [Test]
        public void Mission03RequiresSurviveDuration()
        {
            MissionDefinition m = Load<MissionDefinition>(
                "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_03.asset");
            Assert.IsNotNull(m);
            bool hasSurvive = false;
            for (int i = 0; i < m.ObjectiveCount; i++)
            {
                if (m.Objectives[i].objectiveType == MissionObjectiveType.SurviveDuration && m.Objectives[i].required)
                {
                    Assert.Greater(m.Objectives[i].durationSeconds, 0f);
                    hasSurvive = true;
                }
            }

            Assert.IsTrue(hasSurvive, "M3 must contain a required SurviveDuration objective.");
        }

        [Test]
        public void Mission04RequiresDestroyTargets()
        {
            MissionDefinition m = Load<MissionDefinition>(
                "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_04.asset");
            Assert.IsNotNull(m);
            bool hasDestroy = false;
            for (int i = 0; i < m.ObjectiveCount; i++)
            {
                if (m.Objectives[i].objectiveType == MissionObjectiveType.DestroyTargets && m.Objectives[i].required)
                {
                    Assert.Greater(m.Objectives[i].requiredTargetCount, 0);
                    Assert.Greater(m.Objectives[i].targetHealth, 0);
                    hasDestroy = true;
                }
            }

            Assert.IsTrue(hasDestroy, "M4 must contain a required DestroyTargets objective.");
        }

        [Test]
        public void Mission05HasActivateAndSurviveObjectiveChain()
        {
            MissionDefinition m = Load<MissionDefinition>(
                "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_05.asset");
            Assert.IsNotNull(m);

            bool hasClear = false, hasActivate = false, hasSurvive = false;
            for (int i = 0; i < m.ObjectiveCount; i++)
            {
                MissionObjectiveDefinition o = m.Objectives[i];
                if (o.objectiveType == MissionObjectiveType.ClearAllSections) hasClear = true;
                if (o.objectiveType == MissionObjectiveType.ActivateTargets) hasActivate = true;
                if (o.objectiveType == MissionObjectiveType.SurviveDuration) hasSurvive = true;
            }

            Assert.IsTrue(hasClear && hasActivate && hasSurvive,
                "M5 must contain Clear + Activate + Survive objectives.");
            Assert.IsTrue(m.HasRequiredObjective);
            Assert.IsEmpty(MissionDefinition.CollectProblems(m, KnownArchetypes()),
                "M5 must validate cleanly (including its sequencing references).");
        }

        [Test]
        public void MissionsOneThroughFiveAllValidateCleanly()
        {
            for (int n = 1; n <= 5; n++)
            {
                MissionDefinition m = Load<MissionDefinition>(
                    "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_" + n.ToString("00") + ".asset");
                Assert.IsNotNull(m, "Mission_" + n.ToString("00") + " must exist.");
                Assert.IsEmpty(MissionDefinition.CollectProblems(m, KnownArchetypes()),
                    "Mission_" + n.ToString("00") + " must validate cleanly.");
            }
        }
    }
}
