using System;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1V - the static reward configuration of a mission. PURE DATA: it
    /// declares what a COMPLETED mission grants (Coins / Supplies) and is never
    /// mutated at runtime - earned/granted state lives in the runtime result and
    /// wallet, never here.
    ///
    /// Values must be non-negative (zero is VALID - the currently authored Mission 01
    /// legitimately grants nothing yet; the PRD introduces the resource-reward concept
    /// later in Chapter 1). The RewardService uses this data to calculate/grants the
    /// reward; the editor validator rejects negative values.
    ///
    /// Only Coins and Supplies exist for the 1V foundation. Tech Parts are deliberately
    /// NOT added yet - the future Save/Progression milestone (2C) introduces further
    /// currency seams when the economy actually needs them.
    /// </summary>
    [Serializable]
    public sealed class MissionRewardDefinition
    {
        [UnityEngine.Tooltip("Coins granted for completing this mission. Non-negative; zero is valid.")]
        [UnityEngine.Min(0)] public int coins = 0;

        [UnityEngine.Tooltip("Supplies granted for completing this mission. Non-negative; zero is valid.")]
        [UnityEngine.Min(0)] public int supplies = 0;
    }
}
