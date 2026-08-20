namespace OperationOutbreak.Rewards
{
    /// <summary>
    /// Milestone 1V - immutable, read-only summary of ONE mission outcome. Produced by
    /// the RewardService after the authoritative outcome event (success or failure) and
    /// consumed by the result UI / future SaveService. It is a plain runtime object -
    /// NEVER serialized into MissionDefinition (which stays static configuration).
    ///
    /// Deliberately small: this is NOT an analytics framework and it does NOT duplicate
    /// GameplayDiagnostics - it carries only the data a Result screen needs (identity,
    /// outcome, reward actually granted, sections completed).
    /// </summary>
    public sealed class MissionResultData
    {
        /// <summary>Stable mission id (e.g. 'mission_01').</summary>
        public string MissionId { get; }

        /// <summary>Human-facing mission number (1-based).</summary>
        public int MissionNumber { get; }

        /// <summary>True when the mission was completed (all required objectives satisfied).</summary>
        public bool Success { get; }

        /// <summary>Coins earned by this run (0 for a failed run).</summary>
        public int CoinsEarned { get; }

        /// <summary>Supplies earned by this run (0 for a failed run).</summary>
        public int SuppliesEarned { get; }

        /// <summary>True when the reward was actually granted to the wallet (success only).</summary>
        public bool RewardsGranted { get; }

        /// <summary>Sections cleared before the outcome was reached.</summary>
        public int SectionsCompleted { get; }

        /// <summary>Total sections configured on the mission.</summary>
        public int TotalSections { get; }

        private MissionResultData(
            string missionId, int missionNumber, bool success,
            int coins, int supplies, bool rewardsGranted,
            int sectionsCompleted, int totalSections)
        {
            MissionId = missionId ?? string.Empty;
            MissionNumber = missionNumber;
            Success = success;
            CoinsEarned = coins;
            SuppliesEarned = supplies;
            RewardsGranted = rewardsGranted;
            SectionsCompleted = sectionsCompleted;
            TotalSections = totalSections;
        }

        /// <summary>A completed-mission result carrying the configured reward.</summary>
        public static MissionResultData ForSuccess(
            string missionId, int missionNumber, int coins, int supplies,
            int sectionsCompleted, int totalSections)
        {
            return new MissionResultData(
                missionId, missionNumber, true,
                coins, supplies, true,
                sectionsCompleted, totalSections);
        }

        /// <summary>A failed-mission result: no reward, no grant.</summary>
        public static MissionResultData ForFailure(
            string missionId, int missionNumber, int sectionsCompleted, int totalSections)
        {
            return new MissionResultData(
                missionId, missionNumber, false,
                0, 0, false,
                sectionsCompleted, totalSections);
        }
    }
}
