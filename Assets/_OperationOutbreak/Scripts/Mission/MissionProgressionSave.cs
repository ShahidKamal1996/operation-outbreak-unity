using System;
using System.Collections.Generic;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - the serializable snapshot of mission progression.
    ///
    /// Plain [Serializable] data that JsonUtility (and therefore PlayerPrefs) can round-trip.
    /// It carries a schema version so future fields can be migrated without silently
    /// discarding a player's saved progress, and the completed mission id list.
    ///
    /// This is intentionally NOT a ScriptableObject and NOT runtime state - it is a transfer
    /// object between MissionProgression (in-memory) and IMissionProgressionStore (disk).
    /// </summary>
    [Serializable]
    public sealed class MissionProgressionSave
    {
        /// <summary>Bump when the save schema changes; old saves are migrated or reset safely.</summary>
        public int version = CurrentVersion;

        /// <summary>The stable ids of every completed mission, in arbitrary order.</summary>
        public List<string> completedMissionIds = new List<string>();

        /// <summary>The current save schema version this code understands.</summary>
        public const int CurrentVersion = 1;

        /// <summary>A fresh, empty save with the current version.</summary>
        public static MissionProgressionSave CreateEmpty()
        {
            return new MissionProgressionSave
            {
                version = CurrentVersion,
                completedMissionIds = new List<string>()
            };
        }

        /// <summary>
        /// Builds a save snapshot from an in-memory progression (defensive copy). Never null.
        /// </summary>
        public static MissionProgressionSave FromProgression(MissionProgression progression)
        {
            MissionProgressionSave save = CreateEmpty();

            if (progression != null)
            {
                save.completedMissionIds = progression.GetCompletedMissionIds();
            }

            return save;
        }
    }
}
