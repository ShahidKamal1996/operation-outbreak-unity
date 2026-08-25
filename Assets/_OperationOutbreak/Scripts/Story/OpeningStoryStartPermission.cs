using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1B QA fix #10 — implemented by any component that can legitimately claim
    /// ownership of Mission 01 startup and therefore needs the opening story to stay deferred.
    ///
    /// The critical contract is that <see cref="RequestsStoryHold"/> MUST be answerable purely
    /// from SERIALIZED state, i.e. it must return the correct answer even if the implementing
    /// component's Awake()/OnEnable() has not executed yet. Unity deserializes all
    /// [SerializeField] values before ANY Awake runs, so an implementation that reads only
    /// serialized fields (plus enabled/activeInHierarchy) is inherently order-independent.
    ///
    /// This interface lives in the Story namespace so the Story layer never has to reference the
    /// Cinematic layer. That keeps the dependency arrow one-way (Cinematic -> Story) and remains
    /// safe if assembly definitions are introduced later.
    /// </summary>
    public interface IOpeningStoryHoldSource
    {
        /// <summary>
        /// True while this source claims ownership of Mission 01 startup and the opening story
        /// must NOT auto-start. Must be readable before this component initializes.
        /// </summary>
        bool RequestsStoryHold { get; }

        /// <summary>
        /// Permanently relinquishes this source's claim on Mission 01 startup and releases any
        /// permission token it holds. After this call <see cref="RequestsStoryHold"/> must be false.
        /// Idempotent.
        /// </summary>
        void ReleaseStoryHandoff();
    }

    /// <summary>
    /// Milestone 1Z.1B QA fix #10 — THE single authoritative answer to the question
    /// "is the Mission 01 opening story allowed to start right now?".
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// QA fix #8 gated the opening with an instance flag that
    /// OpeningCinematicController.Awake() pushed onto MissionStoryDirector:
    ///
    ///     OpeningCinematicController.Awake()  -> director.HoldOpeningSequence = true
    ///     MissionStoryDirector.OnEnable()     -> TryStartOpening()
    ///
    /// Unity guarantees that ALL Awake calls precede ALL OnEnable calls only for objects that are
    /// active at scene load. It does NOT guarantee an ordering between two *different* components'
    /// Awake calls, and the guarantee breaks entirely for objects instantiated at runtime, objects
    /// activated later, additive scene loads, or a director that was already enabled. Whenever
    /// MissionStoryDirector.OnEnable ran before OpeningCinematicController.Awake, the flag was
    /// still false, the opening auto-started, and RAVEN ORTIZ dialogue played over the exterior
    /// flyover.
    ///
    /// THE FIX HAS TWO INDEPENDENT LAYERS
    /// ----------------------------------
    ///   Layer 1 (this class): an explicit process-wide permission with hold tokens. Authoritative
    ///           once a holder has actually registered.
    ///   Layer 2 (the scene scan below): answers correctly even BEFORE anyone has registered,
    ///           by reading serialized intent straight off the scene. This is what actually
    ///           removes the ordering race - see <see cref="AnyActiveHoldSourceRequestsHold"/>.
    ///
    /// DEFAULT STATE IS ALLOWED. With no cinematic present (or with Auto Start On Play OFF) nothing
    /// ever holds, and the original Mission 01 flow is byte-for-byte unchanged.
    /// </summary>
    public static class OpeningStoryStartPermission
    {
        /// <summary>
        /// Owner-keyed hold tokens. Using a set (not a raw counter) makes Hold/Release idempotent
        /// per owner: a double Hold(x) does not require a double Release(x), and a stray
        /// Release(x) from an owner that never held cannot unbalance the gate.
        /// </summary>
        private static readonly HashSet<object> Owners = new HashSet<object>(ReferenceComparer.Instance);

        /// <summary>Balanced counter backing the parameterless Hold()/Release() overloads.</summary>
        private static int _anonymousHolds;

        /// <summary>True when the Mission 01 opening story is permitted to start.</summary>
        public static bool IsAllowed => _anonymousHolds <= 0 && Owners.Count == 0;

        /// <summary>Total number of outstanding holds (owner tokens + anonymous holds).</summary>
        public static int HoldCount => Owners.Count + Mathf.Max(0, _anonymousHolds);

        /// <summary>Anonymous hold. Prefer the owner-keyed overload where an owner exists.</summary>
        public static void Hold()
        {
            _anonymousHolds++;
            LogState("Hold()");
        }

        /// <summary>Releases one anonymous hold. Clamped at zero, so over-releasing is harmless.</summary>
        public static void Release()
        {
            _anonymousHolds = Mathf.Max(0, _anonymousHolds - 1);
            LogState("Release()");
        }

        /// <summary>
        /// Owner-keyed hold. Idempotent: holding twice with the same owner is a single token.
        /// A null owner degrades to the anonymous overload.
        /// </summary>
        public static void Hold(object owner)
        {
            if (owner == null) { Hold(); return; }
            if (Owners.Add(owner))
                LogState("Hold(" + Describe(owner) + ")");
        }

        /// <summary>
        /// Releases an owner-keyed hold. Safe to call when the owner does not hold a token.
        /// A null owner degrades to the anonymous overload.
        /// </summary>
        public static void Release(object owner)
        {
            if (owner == null) { Release(); return; }
            if (Owners.Remove(owner))
                LogState("Release(" + Describe(owner) + ")");
        }

        /// <summary>True when this specific owner currently holds a token.</summary>
        public static bool HoldsToken(object owner) => owner != null && Owners.Contains(owner);

        /// <summary>
        /// LAYER 2 - THE RACE FIX.
        ///
        /// Scans the loaded scene(s) for any active <see cref="IOpeningStoryHoldSource"/> that
        /// declares it owns Mission 01 startup. This deliberately does NOT depend on the permission
        /// tokens above, because a token is only acquired once the holder's Awake has run - which
        /// is precisely the ordering we cannot rely on.
        ///
        /// Unity deserializes [SerializeField] data before any Awake executes, so a hold source can
        /// answer RequestsStoryHold correctly from the moment the scene is loaded. Asking the scene
        /// directly therefore yields the right answer regardless of which component initializes
        /// first, which is what makes MissionStoryDirector authoritative rather than dependent on
        /// somebody else having already pushed a flag onto it.
        ///
        /// Cost: one scan of active MonoBehaviours, performed only at story-start decision points
        /// (OnEnable / explicit release), never per-frame.
        /// </summary>
        public static bool AnyActiveHoldSourceRequestsHold()
        {
            // FindObjectsInactive.Exclude skips inactive GameObjects. Components that are merely
            // disabled on an active GameObject are still returned, so IOpeningStoryHoldSource
            // implementations must fold `enabled` into RequestsStoryHold themselves.
            var behaviours = Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            for (int i = 0; i < behaviours.Length; i++)
            {
                var source = behaviours[i] as IOpeningStoryHoldSource;
                if (source == null) continue;
                if (source.RequestsStoryHold) return true;
            }
            return false;
        }

        /// <summary>
        /// Explicit handoff hook for 1Z.1C. Tells every active hold source to relinquish its claim.
        /// NOTHING calls this automatically in QA fix #10 - the exterior cinematic keeps the story
        /// held for its whole lifetime. Returns the number of sources released.
        /// </summary>
        public static int ReleaseSceneHoldSources()
        {
            var behaviours = Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            int released = 0;
            for (int i = 0; i < behaviours.Length; i++)
            {
                var source = behaviours[i] as IOpeningStoryHoldSource;
                if (source == null || !source.RequestsStoryHold) continue;
                source.ReleaseStoryHandoff();
                released++;
            }
            return released;
        }

        /// <summary>
        /// Clears ALL permission state. Invoked automatically before every play session and
        /// available to tests. Static fields survive "Enter Play Mode Options" with domain reload
        /// disabled, so without this a hold leaked from a previous session could permanently
        /// suppress the Mission 01 opening.
        /// </summary>
        public static void ResetState()
        {
            Owners.Clear();
            _anonymousHolds = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStateOnEnterPlayMode() => ResetState();

        private static void LogState(string action)
        {
            Debug.Log($"[STORY GATE] {action} -> IsAllowed={IsAllowed}, holds={HoldCount}");
        }

        private static string Describe(object owner)
        {
            var unityObject = owner as Object;
            return unityObject != null ? unityObject.name : owner.GetType().Name;
        }

        /// <summary>
        /// Pure reference identity. UnityEngine.Object overrides Equals so that a destroyed object
        /// compares equal to null (and thus to another destroyed object). That would let one
        /// destroyed holder evict a different destroyed holder's token, so tokens are keyed on
        /// reference identity instead.
        /// </summary>
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
