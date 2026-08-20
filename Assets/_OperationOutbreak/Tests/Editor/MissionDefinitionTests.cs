using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.EditorTools;
using OperationOutbreak.Enemies;
using OperationOutbreak.Mission;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1T - EditMode regression tests for the data-driven Mission Definition
    /// Foundation. They pin:
    ///   - the committed Mission_01 asset reproduces the verified mission exactly
    ///     (3 sections / 12 enemies / 9 Basic + 3 Runner);
    ///   - the MissionDefinition derives totals instead of storing them;
    ///   - the mission references only valid 1S stable archetypes;
    ///   - validation rejects every class of malformed mission data;
    ///   - the runtime mission flow consumes the MissionDefinition (not hard-coded
    ///     tables) and the shared spawner can resolve every requested archetype;
    ///   - no per-mission gameplay-controller duplication was introduced.
    /// </summary>
    public sealed class MissionDefinitionTests
    {
        private const string MissionAssetPath =
            "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_01.asset";

        private const string ScenePath =
            "Assets/_OperationOutbreak/Scenes/Gameplay_Prototype.unity";

        // ------------------------------------------------------------------ helpers

        private static MissionDefinition LoadCommittedMission()
        {
            MissionDefinition mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>(MissionAssetPath);
            Assert.IsNotNull(mission, "The committed Mission_01 asset must exist at " + MissionAssetPath + ".");
            return mission;
        }

        private static HashSet<string> KnownArchetypeIds()
        {
            var ids = new HashSet<string>();
            foreach (EnemyArchetypeDefinition archetype in
                     EnemyArchetypeEditorTools.LoadAllArchetypeDefinitions())
            {
                if (archetype != null && !string.IsNullOrEmpty(archetype.ArchetypeId))
                {
                    ids.Add(archetype.ArchetypeId);
                }
            }

            return ids;
        }

        private static string ReadSceneText()
        {
            Assert.IsTrue(File.Exists(ScenePath), "Expected the gameplay scene at " + ScenePath + ".");
            return File.ReadAllText(ScenePath);
        }

        /// <summary>
        /// Builds a MissionDefinition in memory: identity through the serialized
        /// fields, sections assigned directly (see the comment below).
        /// </summary>
        private static MissionDefinition CreateMission(
            string missionId, int missionNumber, int chapterNumber,
            params MissionDefinition.MissionSection[] sections)
        {
            MissionDefinition mission = ScriptableObject.CreateInstance<MissionDefinition>();

            SerializedObject so = new SerializedObject(mission);
            so.FindProperty("missionId").stringValue = missionId;
            so.FindProperty("missionNumber").intValue = missionNumber;
            so.FindProperty("chapterNumber").intValue = chapterNumber;
            so.FindProperty("displayName").stringValue = missionId;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Sections are assigned DIRECTLY rather than copied through the serialized
            // property. EnemyCompositionEntry is a plain [Serializable] class, and
            // Unity's serializer stores such classes BY VALUE - a null element inside a
            // List<EnemyCompositionEntry> cannot be represented, so a serialization
            // round-trip materializes the null entry into a default instance
            // (archetypeId 'basic_infected', count 1) and the null-entry validation
            // test could never observe one. Injecting the authored sections as-is keeps
            // the fixture faithful to the exact data model CollectProblems reads.
            FieldInfo sectionsField = typeof(MissionDefinition).GetField(
                "sections", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(sectionsField, "MissionDefinition must keep its 'sections' field.");
            sectionsField.SetValue(
                mission,
                new List<MissionDefinition.MissionSection>(
                    sections ?? new MissionDefinition.MissionSection[0]));

            return mission;
        }

        private static MissionDefinition.MissionSection Section(string id, params (string, int)[] entries)
        {
            var section = new MissionDefinition.MissionSection
            {
                sectionId = id,
                label = id,
                activationZ = 0f,
                forwardLimitZ = 15f,
                spawnAheadOfLimit = 4f,
                composition = new List<MissionDefinition.EnemyCompositionEntry>()
            };

            foreach ((string archetypeId, int count) in entries)
            {
                section.composition.Add(
                    new MissionDefinition.EnemyCompositionEntry(archetypeId, count));
            }

            return section;
        }

        // ------------------------------------------------- committed Mission_01 migration

        [Test]
        public void CommittedMissionHasExactlyThreeSections()
        {
            MissionDefinition mission = LoadCommittedMission();

            Assert.AreEqual(3, mission.SectionCount,
                "The migrated Mission_01 must keep the verified three sections.");
        }

        [Test]
        public void CommittedMissionDerivesTwelveTotalEnemies()
        {
            MissionDefinition mission = LoadCommittedMission();

            Assert.AreEqual(12, mission.TotalEnemyCount,
                "The migrated Mission_01 must keep the verified 12 total enemies.");
        }

        [Test]
        public void CommittedMissionCompositionIsNineBasicAndThreeRunner()
        {
            MissionDefinition mission = LoadCommittedMission();

            Assert.AreEqual(9, mission.GetArchetypeCount(MissionDefinition.BasicArchetypeId),
                "The migrated Mission_01 must keep 9 Basic Infected.");
            Assert.AreEqual(3, mission.GetArchetypeCount(MissionDefinition.RunnerArchetypeId),
                "The migrated Mission_01 must keep 3 Runners.");

            // The verified per-section distribution: 3 / 3+1 / 3+2.
            Assert.AreEqual(3, mission.GetSection(0).TotalEnemyCount);
            Assert.AreEqual(4, mission.GetSection(1).TotalEnemyCount);
            Assert.AreEqual(5, mission.GetSection(2).TotalEnemyCount);
        }

        [Test]
        public void CommittedMissionSectionOrderIsDeterministic()
        {
            MissionDefinition mission = LoadCommittedMission();

            Assert.AreEqual("section_01", mission.GetSection(0).sectionId);
            Assert.AreEqual("section_02", mission.GetSection(1).sectionId);
            Assert.AreEqual("section_03", mission.GetSection(2).sectionId);

            // Section order is deterministic and forward-progressing in the authored list.
            for (int i = 1; i < mission.SectionCount; i++)
            {
                Assert.Greater(mission.GetSection(i).activationZ,
                    mission.GetSection(i - 1).forwardLimitZ,
                    "Each section must activate beyond the previous stop line.");
            }
        }

        [Test]
        public void CommittedMissionReferencesOnlyKnownArchetypes()
        {
            MissionDefinition mission = LoadCommittedMission();
            HashSet<string> known = KnownArchetypeIds();

            for (int i = 0; i < mission.SectionCount; i++)
            {
                MissionDefinition.MissionSection section = mission.GetSection(i);
                foreach (MissionDefinition.EnemyCompositionEntry entry in section.composition)
                {
                    Assert.IsTrue(known.Contains(entry.archetypeId),
                        "Mission_01 section '" + section.sectionId + "' references archetype '" +
                        entry.archetypeId + "' which is not a known 1S stable id.");
                }
            }
        }

        [Test]
        public void CommittedMissionPassesValidation()
        {
            MissionDefinition mission = LoadCommittedMission();
            List<string> problems = MissionDefinition.CollectProblems(mission, KnownArchetypeIds());

            Assert.IsEmpty(problems,
                "The committed Mission_01 must validate cleanly: " + string.Join(" | ", problems));
        }

        [Test]
        public void QueryApisDeriveFromComposition()
        {
            MissionDefinition mission = LoadCommittedMission();

            Assert.AreEqual(3, mission.SectionCount);
            Assert.IsNotNull(mission.GetSection(0));
            Assert.IsNull(mission.GetSection(3), "Out-of-range sections must return null.");
            Assert.IsNull(mission.GetSection(-1), "Negative indices must return null.");
            Assert.AreEqual(0, mission.GetArchetypeCount("tank"),
                "An archetype absent from the composition must total 0.");
        }

        // ------------------------------------------------- validation (malformed data)

        [Test]
        public void ValidationRejectsInvalidIdentity()
        {
            MissionDefinition noId = CreateMission("", 1, 1, Section("section_01", (MissionDefinition.BasicArchetypeId, 3)));
            MissionDefinition badNumber = CreateMission("mission_x", 0, 1, Section("section_01", (MissionDefinition.BasicArchetypeId, 3)));
            MissionDefinition badChapter = CreateMission("mission_y", 1, 0, Section("section_01", (MissionDefinition.BasicArchetypeId, 3)));

            try
            {
                List<string> known = new List<string> { MissionDefinition.BasicArchetypeId, MissionDefinition.RunnerArchetypeId };

                Assert.IsTrue(HasProblem(noId, known, "missing stable mission id"),
                    "An empty mission id must be rejected.");
                Assert.IsTrue(HasProblem(badNumber, known, "invalid mission number"),
                    "A non-positive mission number must be rejected.");
                Assert.IsTrue(HasProblem(badChapter, known, "invalid chapter number"),
                    "A non-positive chapter number must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(noId);
                UnityEngine.Object.DestroyImmediate(badNumber);
                UnityEngine.Object.DestroyImmediate(badChapter);
            }
        }

        [Test]
        public void ValidationRejectsMissionWithNoSections()
        {
            MissionDefinition empty = CreateMission("mission_empty", 1, 1);

            try
            {
                List<string> known = new List<string> { MissionDefinition.BasicArchetypeId };
                Assert.IsTrue(HasProblem(empty, known, "mission has no sections"),
                    "A mission with zero sections must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(empty);
            }
        }

        [Test]
        public void ValidationRejectsDuplicateSectionIds()
        {
            MissionDefinition dup = CreateMission(
                "mission_dup", 1, 1,
                Section("section_01", (MissionDefinition.BasicArchetypeId, 3)),
                Section("section_01", (MissionDefinition.BasicArchetypeId, 3)));

            try
            {
                List<string> known = new List<string> { MissionDefinition.BasicArchetypeId };
                Assert.IsTrue(HasProblem(dup, known, "duplicate section id"),
                    "Duplicate section ids must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dup);
            }
        }

        [Test]
        public void ValidationRejectsSectionWithNoComposition()
        {
            var emptyComposition = Section("section_01");
            emptyComposition.composition = new List<MissionDefinition.EnemyCompositionEntry>();
            MissionDefinition noComp = CreateMission("mission_nocomp", 1, 1, emptyComposition);

            try
            {
                List<string> known = new List<string> { MissionDefinition.BasicArchetypeId };
                Assert.IsTrue(HasProblem(noComp, known, "section has no enemy composition"),
                    "A section with no composition must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(noComp);
            }
        }

        [Test]
        public void ValidationRejectsNonPositiveEnemyCounts()
        {
            MissionDefinition zero = CreateMission(
                "mission_zero", 1, 1,
                Section("section_01", (MissionDefinition.BasicArchetypeId, 0)));
            MissionDefinition negative = CreateMission(
                "mission_neg", 1, 1,
                Section("section_01", (MissionDefinition.BasicArchetypeId, -2)));

            try
            {
                List<string> known = new List<string> { MissionDefinition.BasicArchetypeId };
                Assert.IsTrue(HasProblem(zero, known, "enemy count must be > 0"),
                    "A zero enemy count must be rejected.");
                Assert.IsTrue(HasProblem(negative, known, "enemy count must be > 0"),
                    "A negative enemy count must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(zero);
                UnityEngine.Object.DestroyImmediate(negative);
            }
        }

        [Test]
        public void ValidationRejectsUnknownArchetypeIds()
        {
            MissionDefinition unknown = CreateMission(
                "mission_unknown", 1, 1,
                Section("section_01", ("mega_tank", 3)));

            try
            {
                List<string> known = new List<string> { MissionDefinition.BasicArchetypeId, MissionDefinition.RunnerArchetypeId };
                Assert.IsTrue(HasProblem(unknown, known, "unknown archetype id"),
                    "An unknown archetype id must be rejected by validation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(unknown);
            }
        }

        [Test]
        public void ValidationRejectsNullAndEmptyCompositionEntries()
        {
            var withNullEntry = new MissionDefinition.MissionSection
            {
                sectionId = "section_01",
                activationZ = 0f,
                forwardLimitZ = 15f,
                spawnAheadOfLimit = 4f,
                composition = new List<MissionDefinition.EnemyCompositionEntry> { null }
            };

            var withEmptyId = Section("section_01", ("", 3));

            MissionDefinition nullEntryMission = CreateMission("mission_nullentry", 1, 1, withNullEntry);
            MissionDefinition emptyIdMission = CreateMission("mission_emptyid", 1, 1, withEmptyId);

            try
            {
                List<string> known = new List<string> { MissionDefinition.BasicArchetypeId };
                Assert.IsTrue(HasProblem(nullEntryMission, known, "composition entry 1 is null"),
                    "A null composition entry must be rejected.");
                Assert.IsTrue(HasProblem(emptyIdMission, known, "empty archetype id"),
                    "An empty archetype id must be rejected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(nullEntryMission);
                UnityEngine.Object.DestroyImmediate(emptyIdMission);
            }
        }

        // ------------------------------------------------- runtime wiring / architecture

        [Test]
        public void MissionFlowConsumesTheDefinitionNotHardCodedTables()
        {
            // The mission-flow controller must carry a MissionDefinition reference and
            // must NOT carry its own serialized section table anymore - that is the
            // regression the 1T migration removes (a hard-coded table could drift from
            // the committed definition).
            Type controllerType = typeof(MissionSectionController);

            FieldInfo definitionField = controllerType.GetField(
                "missionDefinition", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(definitionField,
                "MissionSectionController must reference a MissionDefinition.");
            Assert.AreEqual(typeof(MissionDefinition), definitionField.FieldType,
                "The mission data reference must be a MissionDefinition.");

            Assert.IsNull(
                controllerType.GetField("sections", BindingFlags.Instance | BindingFlags.NonPublic),
                "MissionSectionController must not keep its own hard-coded section list - " +
                "section data belongs to MissionDefinition.");
        }

        [Test]
        public void SceneWiresTheDefinitionToTheFlowAndSpawner()
        {
            string scene = ReadSceneText();
            string missionGuid = AssetDatabase.AssetPathToGUID(MissionAssetPath);

            // 1. The scene's MissionSectionController is assigned the committed definition.
            Assert.IsTrue(
                scene.Contains("missionDefinition: {fileID: 11400000, guid: " + missionGuid),
                "The gameplay scene must assign the committed Mission_01 to " +
                "MissionSectionController.missionDefinition.");

            // 2. The shared EnemySpawner can resolve every archetype the mission requests:
            //    its per-archetype library carries the 1S stable ids used by Mission_01.
            int blockStart = scene.IndexOf("archetypes:", StringComparison.Ordinal);
            Assert.Greater(blockStart, -1, "The spawner's archetype list is missing from the scene.");
            int blockEnd = scene.IndexOf("waveOneCount", blockStart, StringComparison.Ordinal);
            Assert.Greater(blockEnd, blockStart, "Could not delimit the archetype list.");
            string archetypeBlock = scene.Substring(blockStart, blockEnd - blockStart);

            Assert.IsTrue(archetypeBlock.Contains("stableId: basic_infected"),
                "The shared spawner must resolve the mission's 'basic_infected' requests.");
            Assert.IsTrue(archetypeBlock.Contains("stableId: runner"),
                "The shared spawner must resolve the mission's 'runner' requests.");
        }

        [Test]
        public void NoPerMissionControllerDuplicationExists()
        {
            // The core architectural rule: mission data defines what happens; gameplay
            // systems execute it. There is exactly ONE mission-flow controller, and no
            // Mission1Controller / Mission2Controller / RunnerMissionController.
            Assembly assembly = typeof(MissionSectionController).Assembly;
            Type[] types = assembly.GetTypes();

            int flowControllers = 0;
            foreach (Type type in types)
            {
                if (type.Namespace == "OperationOutbreak.Mission" &&
                    type.Name.EndsWith("MissionSectionController"))
                {
                    flowControllers++;
                }
            }

            Assert.AreEqual(1, flowControllers,
                "Exactly ONE mission-flow controller may exist - mission variation must " +
                "come from MissionDefinition data, never from duplicated controllers.");

            Assert.IsNull(assembly.GetType("OperationOutbreak.Mission.Mission1Controller"),
                "Mission1Controller must not exist.");
            Assert.IsNull(assembly.GetType("OperationOutbreak.Mission.Mission2Controller"),
                "Mission2Controller must not exist.");
            Assert.IsNull(assembly.GetType("OperationOutbreak.Mission.Mission3Controller"),
                "Mission3Controller must not exist.");
            Assert.IsNull(assembly.GetType("OperationOutbreak.Mission.RunnerMissionController"),
                "RunnerMissionController must not exist.");
        }

        [Test]
        public void MissionCompletionRequiresClearingTheFinalSection()
        {
            MissionDefinition mission = LoadCommittedMission();

            int finalIndex = mission.SectionCount - 1;
            Assert.AreEqual(2, finalIndex,
                "The committed mission has three sections, so only index 2 (Section 3) is final.");

            Assert.IsNotNull(mission.GetSection(finalIndex),
                "The final configured section must exist.");
            Assert.IsNull(mission.GetSection(finalIndex + 1),
                "There must be no section after the final one - clearing the last " +
                "configured section is the single Mission Complete condition.");
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
    }
}
