namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - the persistence seam for mission progression.
    ///
    /// Decouples the progression LOGIC (MissionProgression / MissionProgressionService) from
    /// WHERE the save lives. The production implementation (PlayerPrefsMissionProgressionStore)
    /// uses Unity's lightweight PlayerPrefs so progression survives an application restart with
    /// no cloud, no plugins and no file-format risk; tests inject an in-memory implementation
    /// so save/load round-trips are deterministic and never touch real player data.
    ///
    /// Contract: Load must never return null (an absent/failed save returns an empty save);
    /// Save must persist the snapshot durably enough to survive a restart; Delete must remove
    /// any stored save so the next Load is empty (used by Reset).
    /// </summary>
    public interface IMissionProgressionStore
    {
        /// <summary>Loads the saved progression, or an empty save when none exists.</summary>
        MissionProgressionSave Load();

        /// <summary>Persists <paramref name="save"/> durably.</summary>
        void Save(MissionProgressionSave save);

        /// <summary>Removes any stored progression (full reset).</summary>
        void Delete();
    }
}
