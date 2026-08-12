using System.Collections.Generic;

namespace OperationOutbreak.Diagnostics
{
    /// <summary>
    /// Milestone 1O - the complete in-memory observation set for one scene run.
    ///
    /// Instance state only, owned by the recorder component. There is no static or global
    /// diagnostics state anywhere, so a scene reload starts a genuinely empty run and the
    /// restart-resettability requirement holds by construction.
    ///
    /// This is a plain data container: it records, it never judges. All judgement lives in
    /// <see cref="DiagnosticReportBuilder"/>, which is what allows the whole verdict layer
    /// to be unit tested without Play Mode.
    /// </summary>
    public sealed class DiagnosticRunData
    {
        public float MissionStartTime;

        public bool MissionCompleted;
        public float MissionCompleteTime = -1f;

        public bool GameOver;
        public float GameOverTime = -1f;

        /// <summary>
        /// Counts every Mission Complete signal seen, not just the first. Anything above 1
        /// means a duplicate completion path exists, which is a hard failure.
        /// </summary>
        public int MissionCompleteEventCount;

        /// <summary>Counts every Game Over signal seen. Above 1 means duplicate death handling.</summary>
        public int GameOverEventCount;

        /// <summary>Section index reported cleared more than once, if any. -1 when clean.</summary>
        public int DuplicateSectionClearIndex = -1;

        /// <summary>The shuffled upgrade run order exactly as the director produced it.</summary>
        public readonly List<int> UpgradeRunOrder = new List<int>();

        public int AuthoredUpgradeCount;

        public readonly List<SectionRecord> Sections = new List<SectionRecord>();
        public readonly List<EnemyRecord> Enemies = new List<EnemyRecord>();
        public readonly List<UpgradeRecord> Upgrades = new List<UpgradeRecord>();
        public readonly PlayerRecord Player = new PlayerRecord();

        /// <summary>Lane rectangle captured once at start, used for the reachable-bounds check.</summary>
        public float LaneMinX, LaneMaxX, LaneMinZ, LaneMaxZ;

        public bool LaneBoundsCaptured;

        /// <summary>Authored placement constraints, captured once so checks assert real values.</summary>
        public float MinimumDistanceFromPlayer;
        public float MinimumDistanceFromPreviousPickup;

        /// <summary>Clearance radius the spawner uses; the overlap check reuses it.</summary>
        public float SpawnClearanceRadius;

        public SectionRecord GetSection(int sectionIndex)
        {
            for (int i = 0; i < Sections.Count; i++)
            {
                if (Sections[i].SectionIndex == sectionIndex)
                {
                    return Sections[i];
                }
            }

            return null;
        }

        public string Outcome
        {
            get
            {
                if (MissionCompleted)
                {
                    return "MISSION COMPLETE";
                }

                return GameOver ? "GAME OVER" : "RUN ENDED WITHOUT AN OUTCOME";
            }
        }
    }
}
