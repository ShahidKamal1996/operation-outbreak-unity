using System;
using System.Collections.Generic;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - PURE, in-memory mission completion state.
    ///
    /// This is deliberately a plain C# class (NOT a UnityEngine.Object, NOT serialized into
    /// a scene/asset): it holds nothing but the SET of completed mission ids and the
    /// operations on that set. It knows nothing about chapters, unlocks or persistence -
    /// those belong to MissionProgressionService (unlocks, which needs the ordered chapter)
    /// and to the IMissionProgressionStore (persistence). Keeping it pure makes the entire
    /// completion contract unit-testable without Unity, a scene or PlayerPrefs.
    ///
    /// The set is append-only by design: MarkCompleted only ever ADDS, and Clear is the only
    /// removal path. That is what guarantees the two hard progression invariants:
    ///   * completing an earlier mission can NEVER erase later progress (add-only);
    ///   * a completed mission stays completed (replayable) forever until a full Reset.
    /// </summary>
    public sealed class MissionProgression
    {
        private readonly HashSet<string> _completed = new HashSet<string>();

        /// <summary>How many distinct missions are recorded completed.</summary>
        public int CompletedCount => _completed.Count;

        /// <summary>True when <paramref name="missionId"/> is recorded completed.</summary>
        public bool IsCompleted(string missionId)
        {
            return missionId != null && _completed.Contains(missionId);
        }

        /// <summary>
        /// Records <paramref name="missionId"/> completed (idempotent). A null/empty id is
        /// rejected and returns false so a malformed completion can never pollute the set.
        /// Returns true when the id was newly added.
        /// </summary>
        public bool MarkCompleted(string missionId)
        {
            if (string.IsNullOrEmpty(missionId))
            {
                return false;
            }

            return _completed.Add(missionId);
        }

        /// <summary>Removes all completion records (full development/testing reset).</summary>
        public void Clear()
        {
            _completed.Clear();
        }

        /// <summary>
        /// A defensive copy of the completed mission ids, in undefined order. Used to
        /// snapshot/restore for save/load round-trips and tests.
        /// </summary>
        public List<string> GetCompletedMissionIds()
        {
            return new List<string>(_completed);
        }

        /// <summary>
        /// Replaces all completion records with <paramref name="missionIds"/>. Null/empty
        /// entries are skipped so corrupted save data cannot insert a phantom completion.
        /// </summary>
        public void Restore(IEnumerable<string> missionIds)
        {
            _completed.Clear();

            if (missionIds == null)
            {
                return;
            }

            foreach (string id in missionIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    _completed.Add(id);
                }
            }
        }

        /// <summary>
        /// True when this progression holds EXACTLY the same completed ids as
        /// <paramref name="other"/> (order-independent). Used by save/load round-trip tests.
        /// </summary>
        public bool Matches(IEnumerable<string> other)
        {
            if (other == null)
            {
                return _completed.Count == 0;
            }

            int count = 0;
            foreach (string id in other)
            {
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (!_completed.Contains(id))
                {
                    return false;
                }

                count++;
            }

            return count == _completed.Count;
        }
    }
}
