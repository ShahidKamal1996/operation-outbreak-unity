using System;
using System.Collections.Generic;
using UnityEngine;

namespace OperationOutbreak.Feedback
{
    /// <summary>
    /// Milestone 1P - tiny object pool for short-lived combat feedback visuals (muzzle
    /// flashes, hit sparks). This exists so rapid auto-fire does not instantiate and
    /// destroy GameObjects per shot, and so a leaked visual can never accumulate past a
    /// hard cap.
    ///
    /// Deliberately generic and presentation-only:
    ///   - It knows nothing about weapons, projectiles or enemies. It only hands out and
    ///     takes back GameObjects created by an injected factory.
    ///   - The discard action is injected (Object.Destroy in gameplay, DestroyImmediate or
    ///     a capture list in EditMode tests) so the retention decision can be unit tested
    ///     without Unity lifetime semantics getting in the way.
    ///   - All fields are instance fields on this pool object. The single shared CombatFeedback
    ///     entry point owns one pool per visual kind, which mirrors how the pre-1P static
    ///     CombatFeedback helper already worked.
    /// </summary>
    public sealed class FeedbackObjectPool
    {
        private readonly Stack<GameObject> _available = new Stack<GameObject>();
        private readonly Func<GameObject> _factory;
        private readonly int _maxRetained;
        private readonly Action<GameObject> _discard;

        /// <summary>
        /// Creates a pool.
        /// </summary>
        /// <param name="factory">Builds a new visual when nothing is available. The factory
        /// owns the visual's initial configuration (shape, material, no collider).</param>
        /// <param name="maxRetained">Hard cap on how many deactivated visuals this pool
        /// keeps around. Releases beyond the cap are discarded instead of stored.</param>
        /// <param name="discard">Called with a visual that must be permanently removed.
        /// Null falls back to Object.Destroy.</param>
        public FeedbackObjectPool(Func<GameObject> factory, int maxRetained, Action<GameObject> discard)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _maxRetained = Mathf.Max(0, maxRetained);
            _discard = discard ?? (visual =>
            {
                if (visual != null)
                {
                    UnityEngine.Object.Destroy(visual);
                }
            });
        }

        /// <summary>How many deactivated visuals are currently stored and reusable.</summary>
        public int RetainedCount => _available.Count;

        /// <summary>
        /// Pure retention decision, separated so tests can pin the boundary exactly: a
        /// release must be discarded when the pool already holds its cap. "At least" rather
        /// than "more than" so the pool can never grow past the cap under any ordering.
        /// </summary>
        public static bool ShouldDiscardOnRelease(int retainedCount, int maxRetained)
        {
            return retainedCount >= Mathf.Max(0, maxRetained);
        }

        /// <summary>
        /// Returns an ACTIVE visual: a reused one from the stack when available, otherwise
        /// a freshly built one.
        ///
        /// Activation is part of the acquire contract (Milestone 1P QA fix): a stored
        /// visual is inactive because Release deactivates it, and a component such as
        /// FeedbackVisualLifecycle must never start a coroutine on an inactive object.
        /// The factory result is activated too, so no factory can hand out a dead object.
        ///
        /// Null-sweeps the stack in case something destroyed a pooled visual behind the
        /// pool's back (e.g. scene unload).
        /// </summary>
        public GameObject Acquire()
        {
            while (_available.Count > 0)
            {
                GameObject visual = _available.Pop();

                if (visual != null)
                {
                    visual.SetActive(true);
                    return visual;
                }
            }

            GameObject created = _factory();

            if (created != null)
            {
                created.SetActive(true);
            }

            return created;
        }

        /// <summary>
        /// Takes a finished visual back. The visual is deactivated and stored for reuse
        /// unless the pool is already at its cap, in which case it is discarded.
        /// </summary>
        public void Release(GameObject visual)
        {
            if (visual == null)
            {
                return;
            }

            if (ShouldDiscardOnRelease(_available.Count, _maxRetained))
            {
                _discard(visual);
                return;
            }

            visual.SetActive(false);
            _available.Push(visual);
        }

        /// <summary>Destroys every stored visual and empties the pool. Used for scene teardown.</summary>
        public void Drain()
        {
            while (_available.Count > 0)
            {
                GameObject visual = _available.Pop();

                if (visual != null)
                {
                    _discard(visual);
                }
            }
        }
    }
}
