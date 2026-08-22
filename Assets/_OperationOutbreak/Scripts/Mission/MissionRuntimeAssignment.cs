using OperationOutbreak.Rewards;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - the single scene boot step that makes a SELECTED mission authoritative.
    ///
    /// When the gameplay scene is started through the mission-selection system,
    /// ActiveMissionContext holds the chosen MissionDefinition. This component runs in an
    /// EARLY Awake (see [DefaultExecutionOrder]) - before the mission consumers' own Awake /
    /// OnEnable - and pushes that definition into the three existing consumers via their
    /// additive AssignActiveMission setters. From that point on those systems execute the
    /// selected mission exactly as they always executed Mission 01; no consumer logic changed.
    ///
    /// When ActiveMissionContext has no mission (the gameplay scene opened directly for QA, or
    /// no mission was started), this component does NOTHING and every consumer keeps its
    /// serialized default - so the verified direct-QA path is byte-for-byte unchanged.
    ///
    /// Execution order: Unity deserializes the whole scene before any Awake, so
    /// FindAnyObjectByType resolves every consumer here even though their Awake has not run
    /// yet. The negative DefaultExecutionOrder guarantees this Awake precedes the consumers'
    /// Awake/OnEnable, which is what matters (MissionObjectiveController builds its objective
    /// runtime in OnEnable).
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    public sealed class MissionRuntimeAssignment : MonoBehaviour
    {
        [SerializeField] private bool verboseLogging = false;

        private void Awake()
        {
            MissionDefinition active = ActiveMissionContext.Current;

            if (active == null)
            {
                // No mission was started through the selection system: keep every consumer's
                // serialized default (Mission 01). This is the normal direct-QA path.
                return;
            }

            AssignToConsumers(active);
        }

        private void AssignToConsumers(MissionDefinition active)
        {
            bool anyAssigned = false;

            MissionSectionController sections = FindAnyObjectByType<MissionSectionController>();
            if (sections != null)
            {
                sections.AssignActiveMission(active);
                anyAssigned = true;
            }

            MissionObjectiveController objectives = FindAnyObjectByType<MissionObjectiveController>();
            if (objectives != null)
            {
                objectives.AssignActiveMission(active);
                anyAssigned = true;
            }

            MissionRewardService rewards = FindAnyObjectByType<MissionRewardService>();
            if (rewards != null)
            {
                rewards.AssignActiveMission(active);
                anyAssigned = true;
            }

            if (verboseLogging)
            {
                if (anyAssigned)
                {
                    Debug.Log(
                        "[1X] Active mission '" + active.DisplayName + "' (" + active.MissionId +
                        ") assigned to the gameplay mission consumers.", this);
                }
                else
                {
                    Debug.LogWarning(
                        "[1X] An active mission was set ('" + active.MissionId + "') but no " +
                        "mission consumer was found in the scene to receive it.", this);
                }
            }
        }
    }
}
