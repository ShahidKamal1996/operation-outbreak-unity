using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OperationOutbreak.Rewards
{
    /// <summary>
    /// Milestone 1V - the clean navigation seam for the Result screens.
    ///
    /// Result UI buttons produce a TESTABLE navigation INTENT through the instance
    /// events below; future Base / World Map systems subscribe to `ReturnRequested` /
    /// `NextRequested` and consume it. Those screens do NOT exist yet, so Return and
    /// Next currently log a clear development fallback instead of inventing a fake
    /// Base scene or hard-coding fragile scene names.
    ///
    /// RETRY is functional NOW: it routes through the existing authoritative restart
    /// path (the same SceneManager.LoadScene(activeBuildIndex) the verified restart
    /// buttons already use), which resets every run-scoped system - objectives, section
    /// progression, spawner/enemies, temporary upgrades, reward latch and result state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionResultNavigation : MonoBehaviour
    {
        /// <summary>Raised when the player requests to retry the current mission.</summary>
        public event Action RetryRequested;

        /// <summary>Raised when the player requests to return (toward a future Base/Map).</summary>
        public event Action ReturnRequested;

        /// <summary>Raised when the player requests the next mission (future campaigns).</summary>
        public event Action NextRequested;

        /// <summary>True when a valid scene is loaded that Retry can reload.</summary>
        public bool CanRetry
        {
            get
            {
                Scene active = SceneManager.GetActiveScene();
                return active.IsValid() && active.buildIndex >= 0;
            }
        }

        /// <summary>
        /// Retry intent: raises RetryRequested and reloads the current scene through the
        /// existing restart path (a scene reload is the project's authoritative reset).
        /// </summary>
        public void RequestRetry()
        {
            Debug.Log("[1V] Retry requested.", this);
            RetryRequested?.Invoke();
            ReloadCurrentScene();
        }

        /// <summary>
        /// Return intent: raises ReturnRequested. No Base/Map scene exists yet (2C+), so
        /// this only emits the intent and logs the documented development fallback.
        /// </summary>
        public void RequestReturn()
        {
            Debug.Log("[1V] Return requested - no Base/Map scene exists yet (2C+); navigation intent emitted only.", this);
            ReturnRequested?.Invoke();
        }

        /// <summary>
        /// Next intent: raises NextRequested. There is no next mission yet, so this only
        /// emits the intent and logs the documented development fallback.
        /// </summary>
        public void RequestNext()
        {
            Debug.Log("[1V] Next requested - no next mission exists yet; navigation intent emitted only.", this);
            NextRequested?.Invoke();
        }

        /// <summary>The shared scene-reload that resets every run-scoped system.</summary>
        private void ReloadCurrentScene()
        {
            Scene active = SceneManager.GetActiveScene();

            if (!active.IsValid() || active.buildIndex < 0)
            {
                Debug.LogWarning(
                    "[1V] Retry requested but no reloadable scene is active; reload skipped.", this);
                return;
            }

            SceneManager.LoadScene(active.buildIndex);
        }
    }
}
