using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - runtime loading of the committed Chapter 1 definition from Resources.
    ///
    /// Kept separate from MissionProgressionService so the service stays a pure, testable
    /// facade (tests inject their own in-memory chapter) and the actual asset lookup - which
    /// needs Resources.Load and a loud failure path - lives in one place. Resources.Load works
    /// in builds with no scene wiring, mirroring how EnemyArchetypeRegistry resolves
    /// archetypes.
    /// </summary>
    public static class ChapterRuntimeLoader
    {
        /// <summary>The Resources folder (relative to any Resources dir) holding chapter assets.</summary>
        public const string ResourcesFolder = "ChapterDefinitions";

        /// <summary>The committed Chapter 1 asset name (no extension), under Resources.</summary>
        public const string Chapter1ResourceName = "Chapter_01";

        /// <summary>
        /// Loads the committed Chapter 1 definition from Resources. Returns null and logs a
        /// loud error when it is missing - a missing chapter is a setup error, not silent
        /// degradation. Callers (the progression Default) guard against null.
        /// </summary>
        public static ChapterDefinition LoadChapter1()
        {
            ChapterDefinition chapter = Resources.Load<ChapterDefinition>(
                ResourcesFolder + "/" + Chapter1ResourceName);

            if (chapter == null)
            {
                Debug.LogError(
                    "[1X] Could not load Chapter 1 definition from Resources/" + ResourcesFolder +
                    "/" + Chapter1ResourceName + ". Mission progression will be unavailable " +
                    "until the Chapter_01 asset exists. Create it via Assets > Create > " +
                    "Operation Outbreak > Chapter Definition.");
            }

            return chapter;
        }
    }
}
