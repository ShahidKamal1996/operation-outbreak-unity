using System.Collections.Generic;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - the ONE authoritative mission-progression facade for a chapter.
    ///
    /// It composes three already-built, single-responsibility pieces:
    ///   * a ChapterDefinition (which missions exist and in what unlock order),
    ///   * a MissionProgression (the pure, in-memory completed-id set),
    ///   * an IMissionProgressionStore (where the save lives).
    ///
    /// and adds the only thing those pieces cannot answer alone: SEQUENTIAL UNLOCK
    /// DERIVATION. The unlock rule is data, not stored state: Mission 1 (the first entry) is
    /// unlocked by default, and every later mission is unlocked exactly when the previous
    /// entry in the chapter is completed. Because unlocks are DERIVED from completion +
    /// chapter order, marking a mission completed automatically unlocks the next one, and the
    /// service never has to (and never does) store a separate "unlocked" set that could drift.
    ///
    /// Progress invariants this service guarantees:
    ///   * MarkCompleted is add-only -> completing an earlier mission can never erase later
    ///     progress, and a completed mission stays completed (replayable) until Reset.
    ///   * Reset clears everything and persists, so the next session starts fresh.
    ///   * Load reads the store once; Save is called after every state change so progression
    ///     survives a restart.
    ///
    /// This is a plain C# class so the ENTIRE contract is unit-testable with an in-memory
    /// store and an in-memory chapter. The runtime Chapter-1 instance is exposed through
    /// <see cref="Default"/> for the components that need it (recorder, debug UI) without
    /// each constructing its own copy and drifting out of sync.
    /// </summary>
    public sealed class MissionProgressionService
    {
        private readonly ChapterDefinition _chapter;
        private readonly MissionProgression _progression;
        private readonly IMissionProgressionStore _store;

        /// <summary>The chapter this service governs (e.g. Chapter 1).</summary>
        public ChapterDefinition Chapter => _chapter;

        /// <summary>The ordered missions of the chapter (read-only).</summary>
        public IReadOnlyList<MissionDefinition> Missions =>
            _chapter != null ? _chapter.Missions : System.Array.Empty<MissionDefinition>();

        /// <summary>How many missions the chapter declares.</summary>
        public int MissionCount => _chapter != null ? _chapter.MissionCount : 0;

        /// <summary>The underlying completion set (exposed for diagnostics/tests).</summary>
        public MissionProgression Progression => _progression;

        /// <summary>The persistence backing store.</summary>
        public IMissionProgressionStore Store => _store;

        /// <summary>
        /// Creates a service bound to <paramref name="chapter"/>, loading any saved
        /// progression from <paramref name="store"/> into a fresh in-memory set.
        /// </summary>
        public MissionProgressionService(
            ChapterDefinition chapter, IMissionProgressionStore store)
        {
            _chapter = chapter;
            _store = store ?? new PlayerPrefsMissionProgressionStore();
            _progression = new MissionProgression();
            Load();
        }

        // ------------------------------------------------------------------ queries

        /// <summary>The mission at <paramref name="index"/>, or null when out of range.</summary>
        public MissionDefinition GetMission(int index)
        {
            if (_chapter == null)
            {
                return null;
            }

            return _chapter.GetMission(index);
        }

        /// <summary>True when <paramref name="mission"/> is recorded completed.</summary>
        public bool IsCompleted(MissionDefinition mission)
        {
            return mission != null && _progression.IsCompleted(mission.MissionId);
        }

        /// <summary>True when the mission with <paramref name="missionId"/> is completed.</summary>
        public bool IsCompleted(string missionId)
        {
            return _progression.IsCompleted(missionId);
        }

        /// <summary>
        /// True when <paramref name="mission"/> may be played right now. The FIRST mission in
        /// the chapter is unlocked by default; every later mission is unlocked exactly when
        /// its predecessor is completed. A mission not in this chapter is never unlocked.
        /// </summary>
        public bool IsUnlocked(MissionDefinition mission)
        {
            if (mission == null || _chapter == null)
            {
                return false;
            }

            int index = _chapter.IndexOf(mission);
            if (index < 0)
            {
                return false;
            }

            if (index == 0)
            {
                return true;
            }

            MissionDefinition previous = _chapter.GetMission(index - 1);
            return previous != null && _progression.IsCompleted(previous.MissionId);
        }

        /// <summary>
        /// The mission that unlocks after <paramref name="mission"/>, or null when it is the
        /// last mission in the chapter. Completing the last mission therefore never reaches a
        /// non-existent "next" mission.
        /// </summary>
        public MissionDefinition GetNextMission(MissionDefinition mission)
        {
            if (_chapter == null)
            {
                return null;
            }

            return _chapter.GetNextMission(mission);
        }

        /// <summary>How many missions are completed so far.</summary>
        public int CompletedCount => _progression.CompletedCount;

        // ------------------------------------------------------------------ mutations

        /// <summary>
        /// Records <paramref name="mission"/> completed and persists. Add-only: never reduces
        /// progress. Returns true when the completion was newly recorded. A mission not in
        /// this chapter is still recorded (defensive) so an id mismatch never silently drops
        /// progress, but the normal caller always passes a chapter mission.
        /// </summary>
        public bool MarkCompleted(MissionDefinition mission)
        {
            if (mission == null)
            {
                return false;
            }

            return MarkCompleted(mission.MissionId);
        }

        /// <summary>
        /// Records the mission with <paramref name="missionId"/> completed and persists.
        /// Add-only; returns true when newly recorded.
        /// </summary>
        public bool MarkCompleted(string missionId)
        {
            bool added = _progression.MarkCompleted(missionId);
            if (added)
            {
                Save();
            }

            return added;
        }

        /// <summary>Clears all completion progress and persists (full development/testing reset).</summary>
        public void Reset()
        {
            _progression.Clear();
            _store.Delete();
        }

        // ------------------------------------------------------------------ persistence

        /// <summary>Reloads the completed set from the store, replacing any in-memory state.</summary>
        public void Load()
        {
            MissionProgressionSave save = _store.Load();
            _progression.Restore(save != null ? save.completedMissionIds : null);
        }

        /// <summary>Persists the current completed set to the store.</summary>
        public void Save()
        {
            _store.Save(MissionProgressionSave.FromProgression(_progression));
        }

        // ------------------------------------------------------------------ runtime default

        private static MissionProgressionService s_default;

        /// <summary>
        /// The lazy Chapter-1 runtime instance, shared by every component that needs live
        /// progression (recorder, debug UI) so they never hold divergent copies. Loads the
        /// committed Chapter 1 asset from Resources and a PlayerPrefs-backed store. Tests do
        /// NOT use this - they construct isolated services with in-memory stores.
        /// </summary>
        public static MissionProgressionService Default
        {
            get
            {
                if (s_default == null)
                {
                    ChapterDefinition chapter =
                        ChapterRuntimeLoader.LoadChapter1();
                    s_default = new MissionProgressionService(
                        chapter, new PlayerPrefsMissionProgressionStore());
                }

                return s_default;
            }
        }

        /// <summary>
        /// Drops the cached Chapter-1 Default so the next access reloads it from
        /// Resources + PlayerPrefs. Used after a Reset invoked through the static API, by
        /// the editor Reset Mission Progression tool, by the debug UI reset button and by
        /// test teardown.
        ///
        /// PUBLIC (not internal): MissionProgressionService lives in Assembly-CSharp, but
        /// the editor reset tool and the EditMode tests live in Assembly-CSharp-Editor
        /// (Unity assigns these by folder when there is no .asmdef). An internal member is
        /// invisible across that assembly boundary, so this must be public for the reset
        /// tooling and tests to invalidate the cache. This matches the project convention
        /// (EnemyArchetypeRegistry / ChapterRuntimeLoader / the *EditorTools are all public).
        /// </summary>
        public static void InvalidateDefaultCache()
        {
            s_default = null;
        }
    }
}
