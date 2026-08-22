using OperationOutbreak.Rewards;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - records mission completion into the persistent progression.
    ///
    /// It listens to the EXISTING result authority (MissionRewardService.ResultCreated) and,
    /// on a SUCCESS result, marks the completed mission in MissionProgressionService and
    /// persists it. Because unlocks are DERIVED from completion + chapter order, marking a
    /// mission completed automatically unlocks the next one - no separate unlock step exists.
    ///
    /// This deliberately reuses the existing result/reward architecture rather than observing
    /// a second signal: MissionRewardService already owns the single authoritative outcome
    /// decision (EncounterCompleted for success), and its result carries the MissionId of the
    /// mission that was actually played (which is the selected mission, once
    /// MissionRuntimeAssignment routed it in). No reward amounts, wallet or UI are touched.
    ///
    /// Idempotent and safe under replay: re-completing an already-completed mission is a
    /// no-op (MarkCompleted is add-only), so replaying a completed mission for currency never
    /// re-triggers anything and never erases later progress.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionProgressionRecorder : MonoBehaviour
    {
        [SerializeField] private MissionRewardService rewardService;
        [SerializeField] private bool verboseLogging = true;

        private bool _recordedThisRun;

        private void Awake()
        {
            if (rewardService == null)
            {
                rewardService = FindAnyObjectByType<MissionRewardService>();
            }
        }

        private void OnEnable()
        {
            // A scene reload is a new run: the completion latch is cleared so the new run can
            // record its own completion exactly once.
            _recordedThisRun = false;

            if (rewardService != null)
            {
                rewardService.ResultCreated += HandleResultCreated;
            }
        }

        private void OnDisable()
        {
            if (rewardService != null)
            {
                rewardService.ResultCreated -= HandleResultCreated;
            }
        }

        private void HandleResultCreated(MissionResultData result)
        {
            // Record EXACTLY ONCE per run, and only on a successful completion. A failure
            // grants nothing and must never mark the mission completed.
            if (_recordedThisRun || result == null || !result.Success)
            {
                return;
            }

            _recordedThisRun = true;

            string missionId = result.MissionId;
            if (string.IsNullOrEmpty(missionId))
            {
                if (verboseLogging)
                {
                    Debug.LogWarning(
                        "[1X] Successful mission result carried no mission id - completion " +
                        "could not be recorded into progression.", this);
                }

                return;
            }

            MissionProgressionService progression = MissionProgressionService.Default;
            if (progression == null)
            {
                return;
            }

            bool newlyCompleted = progression.MarkCompleted(missionId);

            if (verboseLogging)
            {
                if (newlyCompleted)
                {
                    MissionDefinition next = null;
                    int index = -1;
                    for (int i = 0; i < progression.MissionCount; i++)
                    {
                        if (progression.GetMission(i) != null
                            && progression.GetMission(i).MissionId == missionId)
                        {
                            index = i;
                            next = progression.GetMission(i + 1);
                            break;
                        }
                    }

                    string nextLine = next != null
                        ? " Next mission '" + next.MissionId + "' is now unlocked."
                        : (index >= 0 ? " This was the final mission - no next mission to unlock." : string.Empty);

                    Debug.Log(
                        "[1X] Mission '" + missionId + "' recorded completed and persisted." +
                        nextLine, this);
                }
                else
                {
                    Debug.Log(
                        "[1X] Mission '" + missionId + "' completed again - already recorded " +
                        "(replay), progression unchanged.", this);
                }
            }
        }
    }
}
