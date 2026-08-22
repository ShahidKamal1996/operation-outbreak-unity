#if UNITY_EDITOR
using OperationOutbreak.Mission;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1X - editor affordances for the local mission progression save.
    ///
    ///   Tools > Operation Outbreak > Reset Mission Progression
    ///
    /// Progression is persisted locally via PlayerPrefs (PlayerPrefsMissionProgressionStore).
    /// During development and QA it must be resettable to a clean state without editing the
    /// registry by hand. This menu item clears the stored save and invalidates the cached
    /// runtime Default, so the next session/access starts with Mission 1 as the only unlocked
    /// mission. No cloud, no analytics.
    /// </summary>
    public static class MissionProgressionEditorTools
    {
        [MenuItem("Tools/Operation Outbreak/Reset Mission Progression")]
        public static void ResetMissionProgression()
        {
            new PlayerPrefsMissionProgressionStore().Delete();
            MissionProgressionService.InvalidateDefaultCache();

            Debug.Log("[1X] Mission progression reset: saved completion cleared. Mission 1 is " +
                      "now the only unlocked mission.");
            EditorUtility.DisplayDialog(
                "Mission Progression",
                "Mission progression has been reset.\nMission 1 is the only unlocked mission.",
                "OK");
        }
    }
}
#endif
