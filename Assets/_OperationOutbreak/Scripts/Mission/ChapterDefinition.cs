using System.Collections.Generic;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - the data-driven CHAPTER container.
    ///
    /// A ChapterDefinition is PURE DATA: an ordered list of the MissionDefinition
    /// assets that belong to one chapter, plus chapter identity. It contains no
    /// gameplay state, no progression state and no runtime logic - it only DESCRIBES
    /// which missions make up a chapter and in what order they unlock.
    ///
    /// The progression system (MissionProgressionService) consumes a ChapterDefinition
    /// to derive sequential unlocks: Mission 1 is unlocked by default, and every later
    /// mission unlocks when its predecessor in THIS ordered list is completed. Adding
    /// a mission to a chapter therefore needs only a new MissionDefinition asset plus
    /// one more entry here - no new C#, no new scene, no duplicated gameplay.
    ///
    /// Authoring workflow: Assets > Create > Operation Outbreak > Chapter Definition,
    /// then drag the chapter's MissionDefinition assets into the ordered list. The
    /// committed Chapter 1 asset lives under
    /// Assets/_OperationOutbreak/Resources/ChapterDefinitions/Chapter_01.asset.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ChapterDefinition_New",
        menuName = "Operation Outbreak/Chapter Definition")]
    public sealed class ChapterDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("STABLE chapter id, e.g. 'chapter_01'. Referenced by future campaign/" +
                 "progression systems; never change it once content references it.")]
        [SerializeField] private string chapterId = string.Empty;

        [Tooltip("Human-facing chapter number (1-based).")]
        [Min(1)] [SerializeField] private int chapterNumber = 1;

        [Tooltip("Human-readable debug/display name.")]
        [SerializeField] private string displayName = string.Empty;

        [Header("Missions (ordered unlock sequence)")]
        [Tooltip("The ordered missions of this chapter. The FIRST entry is unlocked by " +
                 "default; every later entry unlocks when the previous entry is completed. " +
                 "Order IS the unlock sequence.")]
        [SerializeField]
        private List<MissionDefinition> missions = new List<MissionDefinition>();

        // ------------------------------------------------------------------ read-only views

        public string ChapterId => chapterId;
        public int ChapterNumber => chapterNumber;
        public string DisplayName => displayName;

        /// <summary>Ordered, read-only mission list (authored data must not be writable at runtime).</summary>
        public IReadOnlyList<MissionDefinition> Missions => missions;

        /// <summary>How many missions this chapter declares.</summary>
        public int MissionCount => missions != null ? missions.Count : 0;

        /// <summary>
        /// The mission at <paramref name="index"/>, or null when out of range. Used by the
        /// progression service to walk the unlock sequence.
        /// </summary>
        public MissionDefinition GetMission(int index)
        {
            if (missions == null || index < 0 || index >= missions.Count)
            {
                return null;
            }

            return missions[index];
        }

        /// <summary>
        /// The zero-based position of <paramref name="mission"/> in this chapter's ordered
        /// list, or -1 when it is absent. Identity is by reference (the same asset the
        /// chapter references), which is unambiguous and stable.
        /// </summary>
        public int IndexOf(MissionDefinition mission)
        {
            if (missions == null || mission == null)
            {
                return -1;
            }

            for (int i = 0; i < missions.Count; i++)
            {
                if (ReferenceEquals(missions[i], mission))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// The mission that unlocks after <paramref name="mission"/> (the next entry in the
        /// ordered list), or null when <paramref name="mission"/> is the last one. This is
        /// the single definition of "next mission"; Mission 10 (the last entry) deliberately
        /// returns null so completing it cannot reach a non-existent Mission 11.
        /// </summary>
        public MissionDefinition GetNextMission(MissionDefinition mission)
        {
            int index = IndexOf(mission);
            if (index < 0)
            {
                return null;
            }

            return GetMission(index + 1);
        }

        // ------------------------------------------------------------------ validation

        /// <summary>
        /// Pure, side-effect-free validation of the chapter AND every mission it contains.
        /// Returns every problem that makes this chapter unsafe to ship: missing identity,
        /// empty mission list, null entries, duplicate mission ids, non-sequential mission
        /// numbers, chapter-number mismatches, or any per-mission problem (sections,
        /// composition, objectives, reward, environment). Broken chapter data must fail
        /// loudly in editor QA, never silently produce an unplayable chapter.
        ///
        /// The caller supplies the set of known archetype ids; the editor validator resolves
        /// it from the project, tests pass an explicit set, so this stays testable without
        /// an asset database for the archetype half (the committed mission assets ARE read
        /// by the editor validator and the Chapter 1 regression tests).
        /// </summary>
        public static List<string> CollectProblems(
            ChapterDefinition chapter, IReadOnlyCollection<string> knownArchetypeIds)
        {
            List<string> problems = new List<string>();

            if (chapter == null)
            {
                problems.Add("Chapter definition is null.");
                return problems;
            }

            string label = !string.IsNullOrEmpty(chapter.displayName)
                ? chapter.displayName
                : (!string.IsNullOrEmpty(chapter.chapterId) ? chapter.chapterId : chapter.name);

            if (string.IsNullOrEmpty(chapter.chapterId))
            {
                problems.Add(label + ": missing stable chapter id.");
            }

            if (chapter.chapterNumber <= 0)
            {
                problems.Add(label + ": invalid chapter number " + chapter.chapterNumber +
                             " (must be >= 1).");
            }

            if (chapter.missions == null || chapter.missions.Count == 0)
            {
                problems.Add(label + ": chapter declares no missions.");
                return problems;
            }

            HashSet<string> seenMissionIds = new HashSet<string>();

            for (int i = 0; i < chapter.missions.Count; i++)
            {
                MissionDefinition mission = chapter.missions[i];
                string where = label + " / mission slot " + (i + 1);

                if (mission == null)
                {
                    problems.Add(where + ": mission reference is null.");
                    continue;
                }

                // Per-mission structural validation (sections, composition, objectives, reward).
                List<string> missionProblems =
                    MissionDefinition.CollectProblems(mission, knownArchetypeIds);

                for (int j = 0; j < missionProblems.Count; j++)
                {
                    problems.Add(where + " (" + mission.name + "): " + missionProblems[j]);
                }

                // Identity uniqueness across the chapter.
                string missionId = mission.MissionId;
                if (string.IsNullOrEmpty(missionId))
                {
                    problems.Add(where + " (" + mission.name + "): missing stable mission id.");
                }
                else if (!seenMissionIds.Add(missionId))
                {
                    problems.Add(where + " (" + mission.name + "): duplicate mission id '" +
                                 missionId + "'.");
                }

                // Mission numbers must be sequential 1..N matching list order.
                if (mission.MissionNumber != i + 1)
                {
                    problems.Add(where + " (" + mission.name + "): mission number is " +
                                 mission.MissionNumber + " but slot " + (i + 1) +
                                 " requires number " + (i + 1) +
                                 " (numbers must be sequential 1..N in list order).");
                }

                // Every mission must belong to this chapter.
                if (mission.ChapterNumber != chapter.chapterNumber)
                {
                    problems.Add(where + " (" + mission.name + "): mission chapter number " +
                                 mission.ChapterNumber + " does not match chapter " +
                                 chapter.chapterNumber + ".");
                }

                // Every mission must carry a valid environment reference (1X requirement).
                if (mission.Environment == null)
                {
                    problems.Add(where + " (" + mission.name +
                                 "): missing environment profile reference.");
                }
            }

            return problems;
        }
    }
}
