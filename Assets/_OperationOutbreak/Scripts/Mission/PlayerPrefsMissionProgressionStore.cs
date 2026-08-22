using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - the production progression store, backed by Unity PlayerPrefs.
    ///
    /// PlayerPrefs is the lightest built-in Unity persistence that survives an application
    /// restart, needs no cloud, no plugins and no custom file format - exactly the
    /// "lightweight, consistent with the current architecture" persistence the milestone asks
    /// for. The save is serialized to JSON via JsonUtility and stored under one stable key.
    ///
    /// Resilience: a missing key, an empty value or an unreadable payload never throws - Load
    /// returns an empty save and logs a warning, so corrupted player data degrades to a fresh
    /// progression instead of crashing on boot. The version field on the save lets future
    /// migrations detect and reset incompatible data.
    /// </summary>
    public sealed class PlayerPrefsMissionProgressionStore : IMissionProgressionStore
    {
        /// <summary>
        /// The single PlayerPrefs key all Operation: Outbreak progression lives under.
        /// Stable forever once released; never reuse it for anything else.
        /// </summary>
        public const string SaveKey = "oo_mission_progression_v1";

        private readonly string _key;

        /// <summary>Creates a store using the default stable save key.</summary>
        public PlayerPrefsMissionProgressionStore() : this(SaveKey) { }

        /// <summary>Creates a store using a custom key (used by tests to isolate saves).</summary>
        public PlayerPrefsMissionProgressionStore(string key)
        {
            _key = string.IsNullOrEmpty(key) ? SaveKey : key;
        }

        /// <summary>
        /// Loads the saved progression, or an empty save when none/corrupt. Never returns null.
        /// </summary>
        public MissionProgressionSave Load()
        {
            if (!PlayerPrefs.HasKey(_key))
            {
                return MissionProgressionSave.CreateEmpty();
            }

            string json = PlayerPrefs.GetString(_key, string.Empty);

            if (string.IsNullOrEmpty(json))
            {
                return MissionProgressionSave.CreateEmpty();
            }

            try
            {
                MissionProgressionSave save =
                    JsonUtility.FromJson<MissionProgressionSave>(json);

                if (save == null)
                {
                    return MissionProgressionSave.CreateEmpty();
                }

                // A future/incompatible version must not be trusted blindly: reset cleanly
                // rather than partially applying unknown fields.
                if (save.version != MissionProgressionSave.CurrentVersion)
                {
                    Debug.LogWarning(
                        "[1X] Mission progression save version " + save.version +
                        " is not the current version " + MissionProgressionSave.CurrentVersion +
                        " - the saved progression is being reset.");
                    return MissionProgressionSave.CreateEmpty();
                }

                if (save.completedMissionIds == null)
                {
                    save.completedMissionIds = new System.Collections.Generic.List<string>();
                }

                return save;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(
                    "[1X] Mission progression save at key '" + _key + "' could not be read " +
                    "and was ignored (reset to empty). Reason: " + e.Message);
                return MissionProgressionSave.CreateEmpty();
            }
        }

        /// <summary>Persists <paramref name="save"/> to PlayerPrefs and flushes to disk.</summary>
        public void Save(MissionProgressionSave save)
        {
            MissionProgressionSave payload = save ?? MissionProgressionSave.CreateEmpty();

            string json = JsonUtility.ToJson(payload);
            PlayerPrefs.SetString(_key, json);
            PlayerPrefs.Save();
        }

        /// <summary>Removes any stored progression and flushes.</summary>
        public void Delete()
        {
            if (PlayerPrefs.HasKey(_key))
            {
                PlayerPrefs.DeleteKey(_key);
                PlayerPrefs.Save();
            }
        }
    }
}
