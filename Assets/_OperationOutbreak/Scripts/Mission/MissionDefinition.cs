using System;
using System.Collections.Generic;
using System.Linq;
using OperationOutbreak.Enemies;
using OperationOutbreak.Environment;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1T - the DATA-DRIVEN mission definition foundation.
    ///
    /// A MissionDefinition is PURE DATA: it describes WHAT a mission contains
    /// (identity, ordered sections, per-section enemy composition by 1S stable
    /// archetype id). It contains no Update loop, no combat logic, no spawning
    /// and no progression state - the runtime mission-flow system
    /// (MissionSectionController) reads it and the shared EnemySpawner executes
    /// the requested spawns.
    ///
    /// CORE ARCHITECTURAL RULE: mission data defines what happens; gameplay
    /// systems execute it. A normal future mission is creatable primarily by
    /// making and configuring a MissionDefinition asset - no new C# class, no
    /// Mission1Controller / Mission2Controller duplication.
    ///
    /// Enemy composition uses the Milestone 1S stable archetype architecture:
    /// each entry names an archetype by its STABLE id ("basic_infected",
    /// "runner") and EnemyArchetypeRegistry remains the resolution authority.
    /// There is no variant-specific branching anywhere in this class.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MissionDefinition_New",
        menuName = "Operation Outbreak/Mission Definition")]
    public sealed class MissionDefinition : ScriptableObject
    {
        /// <summary>The verified Basic Infected stable id (the 1S default).</summary>
        public const string BasicArchetypeId = EnemyArchetypeRegistry.DefaultArchetypeId;

        /// <summary>The Runner stable id prepared by Milestone 1S.</summary>
        public const string RunnerArchetypeId = "runner";

        // ------------------------------------------------------------------ identity

        [Header("Identity")]
        [Tooltip("STABLE mission id, e.g. 'mission_01'. Referenced by future campaign/" +
                 "progression systems; never change it once content references it.")]
        [SerializeField] private string missionId = string.Empty;

        [Tooltip("Human-facing mission number (1-based).")]
        [Min(1)] [SerializeField] private int missionNumber = 1;

        [Tooltip("Human-facing chapter number (1-based).")]
        [Min(1)] [SerializeField] private int chapterNumber = 1;

        [Tooltip("Human-readable debug/display name.")]
        [SerializeField] private string displayName = string.Empty;

        // ------------------------------------------------------------------ structure

        [Header("Structure")]
        [Tooltip("Ordered mission sections. Section order IS the deterministic mission " +
                 "sequence - the runtime flow advances forward through this list only.")]
        [SerializeField]
        private List<MissionSection> sections = new List<MissionSection>();

        [Header("Objectives (Milestone 1U)")]
        [Tooltip("Ordered objective definitions. REQUIRED objectives gate mission " +
                 "completion; optional objectives are tracked but never block victory.")]
        [SerializeField]
        private List<MissionObjectiveDefinition> objectives = new List<MissionObjectiveDefinition>();

        [Header("Rewards (Milestone 1V)")]
        [Tooltip("Static reward configuration granted on completion. Zero is valid - the " +
                 "currently authored Mission 01 legitimately grants nothing yet.")]
        [SerializeField]
        private MissionRewardDefinition reward = new MissionRewardDefinition();

        [Header("Environment (Milestone 1W)")]
        [Tooltip("The Chapter 1 environment profile this mission presents (road, barriers, " +
                 "roadside dressing, landmarks). Assign a profile - no hard-coded " +
                 "environment logic lives in mission code.")]
        [SerializeField]
        private MissionEnvironmentDefinition environment;

        [Header("Cinematics (Milestone 1Z)")]
        [Tooltip("Optional pre-mission cinematic sequence. May be null.")]
        [SerializeField] private OperationOutbreak.Story.StorySequenceDefinition preMissionSequence;

        [Tooltip("Optional post-mission cinematic sequence. May be null.")]
        [SerializeField] private OperationOutbreak.Story.StorySequenceDefinition postMissionSequence;

        // ------------------------------------------------------------------ read-only views

        public string MissionId => missionId;
        public int MissionNumber => missionNumber;
        public int ChapterNumber => chapterNumber;
        public string DisplayName => displayName;

        /// <summary>Ordered, read-only section list (authored data must not be writable at runtime).</summary>
        public IReadOnlyList<MissionSection> Sections => sections;

        public int SectionCount => sections != null ? sections.Count : 0;

        /// <summary>Ordered, read-only objective list (static configuration only).</summary>
        public IReadOnlyList<MissionObjectiveDefinition> Objectives => objectives;

        public int ObjectiveCount => objectives != null ? objectives.Count : 0;

        /// <summary>How many objectives are REQUIRED (gate completion). Derived, never stored.</summary>
        public int RequiredObjectiveCount
        {
            get
            {
                if (objectives == null)
                {
                    return 0;
                }

                int required = 0;
                for (int i = 0; i < objectives.Count; i++)
                {
                    if (objectives[i] != null && objectives[i].required)
                    {
                        required++;
                    }
                }

                return required;
            }
        }

        /// <summary>True when the mission declares at least one required objective.</summary>
        public bool HasRequiredObjective => RequiredObjectiveCount > 0;

        /// <summary>Static reward configuration (Coins / Supplies). Never null once constructed.</summary>
        public MissionRewardDefinition Reward => reward;

        /// <summary>The Chapter 1 environment profile this mission presents (may be null).</summary>
        public MissionEnvironmentDefinition Environment => environment;

        /// <summary>1Z - optional pre-mission cinematic. Null = no cinematic.</summary>
        public OperationOutbreak.Story.StorySequenceDefinition PreMissionSequence => preMissionSequence;

        /// <summary>1Z - optional post-mission cinematic. Null = no cinematic.</summary>
        public OperationOutbreak.Story.StorySequenceDefinition PostMissionSequence => postMissionSequence;

        /// <summary>The objective with <paramref name="objectiveId"/>, or null when absent.</summary>
        public MissionObjectiveDefinition GetObjective(string objectiveId)
        {
            if (objectives == null)
            {
                return null;
            }

            for (int i = 0; i < objectives.Count; i++)
            {
                if (objectives[i] != null && objectives[i].objectiveId == objectiveId)
                {
                    return objectives[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Total enemies across every section, DERIVED from the section composition
        /// rather than stored independently - a stored total could drift out of sync
        /// with the authored composition and is deliberately not kept.
        /// </summary>
        public int TotalEnemyCount
        {
            get
            {
                if (sections == null)
                {
                    return 0;
                }

                int total = 0;
                for (int i = 0; i < sections.Count; i++)
                {
                    MissionSection section = sections[i];
                    if (section != null)
                    {
                        total += section.TotalEnemyCount;
                    }
                }

                return total;
            }
        }

        /// <summary>The section at <paramref name="index"/>, or null when out of range.</summary>
        public MissionSection GetSection(int index)
        {
            if (sections == null || index < 0 || index >= sections.Count)
            {
                return null;
            }

            return sections[index];
        }

        /// <summary>
        /// Total enemies of one archetype across the whole mission, derived from the
        /// composition. 0 when the id is absent (or the section list is empty).
        /// </summary>
        public int GetArchetypeCount(string archetypeId)
        {
            if (sections == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < sections.Count; i++)
            {
                MissionSection section = sections[i];
                if (section == null || section.composition == null)
                {
                    continue;
                }

                for (int j = 0; j < section.composition.Count; j++)
                {
                    EnemyCompositionEntry entry = section.composition[j];
                    if (entry != null && entry.archetypeId == archetypeId)
                    {
                        total += entry.count;
                    }
                }
            }

            return total;
        }

        // ------------------------------------------------------------------ validation

        /// <summary>
        /// Pure, side-effect-free validation: returns every problem that makes this
        /// mission unsafe or impossible to execute. Static and testable without an
        /// asset database - the caller supplies the set of known archetype ids (the
        /// editor validator resolves it from the project; tests pass an explicit set).
        /// Broken mission data must fail loudly in editor QA, never silently spawn
        /// unpredictably.
        /// </summary>
        public static List<string> CollectProblems(
            MissionDefinition definition, IReadOnlyCollection<string> knownArchetypeIds)
        {
            List<string> problems = new List<string>();

            if (definition == null)
            {
                problems.Add("Mission definition is null.");
                return problems;
            }

            string label = !string.IsNullOrEmpty(definition.displayName)
                ? definition.displayName
                : (!string.IsNullOrEmpty(definition.missionId) ? definition.missionId : definition.name);

            if (string.IsNullOrEmpty(definition.missionId))
            {
                problems.Add(label + ": missing stable mission id.");
            }

            if (definition.missionNumber <= 0)
            {
                problems.Add(label + ": invalid mission number " + definition.missionNumber + " (must be >= 1).");
            }

            if (definition.chapterNumber <= 0)
            {
                problems.Add(label + ": invalid chapter number " + definition.chapterNumber + " (must be >= 1).");
            }

            if (definition.sections == null || definition.sections.Count == 0)
            {
                problems.Add(label + ": mission has no sections (a mission needs at least one).");
                return problems;
            }

            HashSet<string> seenSectionIds = new HashSet<string>();

            for (int i = 0; i < definition.sections.Count; i++)
            {
                MissionSection section = definition.sections[i];
                string where = label + " / section " + (i + 1);

                if (section == null)
                {
                    problems.Add(label + ": section entry " + (i + 1) + " is null.");
                    continue;
                }

                if (string.IsNullOrEmpty(section.sectionId))
                {
                    problems.Add(where + ": missing stable section id.");
                }
                else if (!seenSectionIds.Add(section.sectionId))
                {
                    problems.Add(where + ": duplicate section id '" + section.sectionId + "'.");
                }

                if (section.composition == null || section.composition.Count == 0)
                {
                    problems.Add(where + ": section has no enemy composition (at least one " +
                                 "archetype entry is required).");
                }
                else
                {
                    for (int j = 0; j < section.composition.Count; j++)
                    {
                        EnemyCompositionEntry entry = section.composition[j];
                        string entryWhere = where + " / composition entry " + (j + 1);

                        if (entry == null)
                        {
                            problems.Add(where + ": composition entry " + (j + 1) + " is null.");
                            continue;
                        }

                        if (string.IsNullOrEmpty(entry.archetypeId))
                        {
                            problems.Add(entryWhere + ": empty archetype id.");
                        }
                        else if (knownArchetypeIds != null && !knownArchetypeIds.Contains(entry.archetypeId))
                        {
                            problems.Add(entryWhere + ": unknown archetype id '" + entry.archetypeId +
                                         "' (not a 1S stable id).");
                        }

                        if (entry.count <= 0)
                        {
                            problems.Add(entryWhere + ": enemy count must be > 0 (got " + entry.count + ").");
                        }
                    }
                }

                // Structural impossibility: the player must be able to REACH the next
                // activation line, and enemies must spawn ahead of the stop line.
                if (section.spawnAheadOfLimit <= 0f)
                {
                    problems.Add(where + ": spawnAheadOfLimit must be > 0 or enemies spawn on the player.");
                }

                if (i > 0)
                {
                    MissionSection previous = definition.sections[i - 1];
                    if (previous != null && section.activationZ <= previous.forwardLimitZ)
                    {
                        problems.Add(where + ": activationZ (" + section.activationZ +
                                     ") must sit beyond the previous section's forwardLimitZ (" +
                                     previous.forwardLimitZ + ") or the section can never activate.");
                    }
                }
            }

            if (definition.TotalEnemyCount <= 0)
            {
                problems.Add(label + ": mission spawns no enemies.");
            }

            // ------------------------------------------------------------------ objectives (1U)

            if (definition.objectives == null || definition.objectives.Count == 0)
            {
                problems.Add(label + ": mission has no objectives (a required completion " +
                             "objective is mandatory).");
            }
            else
            {
                bool anyRequired = false;
                HashSet<string> seenObjectiveIds = new HashSet<string>();

                for (int k = 0; k < definition.objectives.Count; k++)
                {
                    MissionObjectiveDefinition objective = definition.objectives[k];
                    string objectiveWhere = label + " / objective " + (k + 1);

                    if (objective == null)
                    {
                        problems.Add(label + ": objective entry " + (k + 1) + " is null.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(objective.objectiveId))
                    {
                        problems.Add(objectiveWhere + ": missing stable objective id.");
                    }
                    else if (!seenObjectiveIds.Add(objective.objectiveId))
                    {
                        problems.Add(objectiveWhere + ": duplicate objective id '" +
                                     objective.objectiveId + "'.");
                    }

                    if (!Enum.IsDefined(typeof(MissionObjectiveType), objective.objectiveType))
                    {
                        problems.Add(objectiveWhere + ": unsupported objective type '" +
                                     objective.objectiveType + "'.");
                    }

                    if (objective.required)
                    {
                        anyRequired = true;
                    }

                    // Milestone 1X.5 - per-type data validation. Each new objective type owns
                    // specific parameters that must be sensible or the mission is unplayable.
                    switch (objective.objectiveType)
                    {
                        case MissionObjectiveType.SurviveDuration:
                            if (objective.durationSeconds <= 0f)
                            {
                                problems.Add(objectiveWhere + ": SurviveDuration requires durationSeconds > 0 (got " +
                                             objective.durationSeconds + ").");
                            }
                            break;
                        case MissionObjectiveType.DestroyTargets:
                            if (objective.requiredTargetCount <= 0)
                            {
                                problems.Add(objectiveWhere + ": DestroyTargets requires requiredTargetCount > 0 (got " +
                                             objective.requiredTargetCount + ").");
                            }
                            if (objective.targetHealth <= 0)
                            {
                                problems.Add(objectiveWhere + ": DestroyTargets requires targetHealth > 0 (got " +
                                             objective.targetHealth + ").");
                            }
                            break;
                        case MissionObjectiveType.ActivateTargets:
                            if (objective.requiredTargetCount <= 0)
                            {
                                problems.Add(objectiveWhere + ": ActivateTargets requires requiredTargetCount > 0 (got " +
                                             objective.requiredTargetCount + ").");
                            }
                            if (objective.activationDuration <= 0f)
                            {
                                problems.Add(objectiveWhere + ": ActivateTargets requires activationDuration > 0 (got " +
                                             objective.activationDuration + ").");
                            }
                            if (objective.activationRadius <= 0f)
                            {
                                problems.Add(objectiveWhere + ": ActivateTargets requires activationRadius > 0 (got " +
                                             objective.activationRadius + ").");
                            }
                            break;
                    }

                    // ClearAllSections carries no explicit section/archetype reference -
                    // its required progress derives from the mission's section count, and
                    // section validity is already enforced by the section loop above.
                }

                // Milestone 1X.5 - objective sequencing references must point at a real, DIFFERENT
                // objective id (a self- or missing-reference would make the stage permanently
                // inactive and the mission uncompletable).
                for (int k = 0; k < definition.objectives.Count; k++)
                {
                    MissionObjectiveDefinition objective = definition.objectives[k];
                    if (objective == null || string.IsNullOrEmpty(objective.activateAfterObjectiveId))
                    {
                        continue;
                    }

                    string prereq = objective.activateAfterObjectiveId;
                    if (prereq == objective.objectiveId)
                    {
                        problems.Add(label + " / objective " + (k + 1) + ": activateAfterObjectiveId '" +
                                     prereq + "' references itself (would never activate).");
                    }
                    else if (definition.GetObjective(prereq) == null)
                    {
                        problems.Add(label + " / objective " + (k + 1) + ": activateAfterObjectiveId '" +
                                     prereq + "' does not match any objective id.");
                    }
                }

                if (!anyRequired)
                {
                    problems.Add(label + ": mission has no REQUIRED objective - mission " +
                                 "completion would never gate.");
                }
            }

            // ------------------------------------------------------------------ rewards (1V)

            if (definition.reward == null)
            {
                problems.Add(label + ": reward definition is null.");
            }
            else
            {
                if (definition.reward.coins < 0)
                {
                    problems.Add(label + ": negative Coins reward (" + definition.reward.coins + ").");
                }

                if (definition.reward.supplies < 0)
                {
                    problems.Add(label + ": negative Supplies reward (" + definition.reward.supplies + ").");
                }
            }

            return problems;
        }

        /// <summary>
        /// Development fallback only: builds the VERIFIED prototype mission in memory
        /// (3 sections, 9 Basic + 3 Runner) using the 1S stable ids. Used by
        /// MissionSectionController when no MissionDefinition asset is assigned so a
        /// missing reference can never produce unpredictable partial gameplay; the
        /// controller logs a loud actionable error before using it. The committed
        /// Mission_01 asset is the production source of truth - this is a safety net,
        /// not a second mission authoring location.
        /// </summary>
        public static MissionDefinition CreateVerifiedPrototypeMission()
        {
            MissionDefinition mission = CreateInstance<MissionDefinition>();
            mission.missionId = "mission_01_fallback";
            mission.missionNumber = 1;
            mission.chapterNumber = 1;
            mission.displayName = "Outbreak (verified prototype fallback)";
            mission.sections = new List<MissionSection>
            {
                new MissionSection
                {
                    sectionId = "section_01",
                    label = "SECTION 1",
                    subtitle = "OUTBREAK",
                    activationZ = -100f,
                    forwardLimitZ = 15f,
                    spawnAheadOfLimit = 1f,
                    composition = new List<EnemyCompositionEntry>
                    {
                        new EnemyCompositionEntry(BasicArchetypeId, 3)
                    }
                },
                new MissionSection
                {
                    sectionId = "section_02",
                    label = "SECTION 2",
                    subtitle = "ADVANCE",
                    activationZ = 20f,
                    forwardLimitZ = 33f,
                    spawnAheadOfLimit = 4f,
                    composition = new List<EnemyCompositionEntry>
                    {
                        new EnemyCompositionEntry(BasicArchetypeId, 3),
                        new EnemyCompositionEntry(RunnerArchetypeId, 1)
                    }
                },
                new MissionSection
                {
                    sectionId = "section_03",
                    label = "SECTION 3",
                    subtitle = "FINAL PUSH",
                    activationZ = 38f,
                    forwardLimitZ = 51f,
                    spawnAheadOfLimit = 4f,
                    composition = new List<EnemyCompositionEntry>
                    {
                        new EnemyCompositionEntry(BasicArchetypeId, 3),
                        new EnemyCompositionEntry(RunnerArchetypeId, 2)
                    }
                }
            };

            // Milestone 1U - the fallback carries the same explicit objective data as the
            // committed Mission_01: one required ClearAllSections objective.
            mission.objectives = new List<MissionObjectiveDefinition>
            {
                new MissionObjectiveDefinition
                {
                    objectiveId = "clear_all_sections",
                    title = "Clear All Sections",
                    objectiveType = MissionObjectiveType.ClearAllSections,
                    required = true
                }
            };

            return mission;
        }

        /// <summary>
        /// One enemy composition entry: "how many of which archetype". The archetype
        /// is referenced by its 1S STABLE id and resolved at runtime by
        /// EnemyArchetypeRegistry / the shared EnemySpawner - never by a variant-
        /// specific code branch.
        /// </summary>
        [Serializable]
        public sealed class EnemyCompositionEntry
        {
            [Tooltip("1S stable archetype id to spawn (e.g. 'basic_infected' or 'runner').")]
            public string archetypeId = BasicArchetypeId;

            [Tooltip("How many of this archetype this section spawns.")]
            [Min(1)] public int count = 1;

            public EnemyCompositionEntry() { }

            public EnemyCompositionEntry(string archetypeId, int count)
            {
                this.archetypeId = archetypeId;
                this.count = count;
            }
        }

        /// <summary>
        /// Pure authoring data for one mission section: identity, the corridor the
        /// section occupies (activation + forward limit + spawn offset) and its enemy
        /// composition. No behaviour - the runtime flow consumes this.
        /// </summary>
        [Serializable]
        public sealed class MissionSection
        {
            [Tooltip("STABLE section id, e.g. 'section_01'. Unique within the mission.")]
            public string sectionId = string.Empty;

            [Tooltip("Short HUD label, e.g. 'SECTION 1'.")]
            public string label = "SECTION 1";

            [Tooltip("HUD subtitle, e.g. 'OUTBREAK'.")]
            public string subtitle = "OUTBREAK";

            [Tooltip("The player must reach this Z before the section activates. Section 1 " +
                     "normally uses the mission start Z so it opens immediately.")]
            public float activationZ;

            [Tooltip("How far forward the player may travel while this section is the " +
                     "current one. Also caps where upgrade pickups may appear.")]
            public float forwardLimitZ = 15f;

            [Tooltip("Where this section's zombies appear, relative to the section's forward " +
                     "limit. Positive values are ahead of the player's stop line.")]
            public float spawnAheadOfLimit = 4f;

            [Tooltip("Which enemy archetypes make up this section (by 1S stable id).")]
            public List<EnemyCompositionEntry> composition = new List<EnemyCompositionEntry>();

            /// <summary>
            /// Total enemies this section will spawn, DERIVED from the composition.
            /// This is the number the section must kill to clear, so a Runner counts
            /// exactly the same as a Basic zombie.
            /// </summary>
            public int TotalEnemyCount
            {
                get
                {
                    if (composition == null)
                    {
                        return 0;
                    }

                    int total = 0;
                    for (int i = 0; i < composition.Count; i++)
                    {
                        EnemyCompositionEntry entry = composition[i];
                        if (entry != null)
                        {
                            total += Mathf.Max(0, entry.count);
                        }
                    }

                    return total;
                }
            }
        }
    }
}
