using System.Collections.Generic;
using UnityEngine;

namespace OperationOutbreak.Diagnostics
{
    /// <summary>
    /// Milestone 1O - the pure predicates behind every diagnostic check.
    ///
    /// Deliberately static and side-effect free: no scene access, no component lookups,
    /// no state. Two things follow from that.
    ///
    ///   1. The runtime recorder and the EditMode test suite exercise the SAME rule code,
    ///      so a green test genuinely says something about what the game reports.
    ///   2. Diagnostics can never influence gameplay through these helpers - they only
    ///      read values that have already happened.
    ///
    /// All distance work is planar (XZ). Height is irrelevant on this lane and including
    /// it would make a floating pickup look further away than it plays.
    /// </summary>
    public static class DiagnosticRules
    {
        /// <summary>Ground-plane distance, ignoring Y.</summary>
        public static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;

            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        /// <summary>True when two points are at least <paramref name="minimum"/> apart on the ground plane.</summary>
        public static bool MeetsMinimumDistance(Vector3 a, Vector3 b, float minimum, float tolerance = 0.01f)
        {
            return PlanarDistance(a, b) >= minimum - tolerance;
        }

        /// <summary>
        /// True when a point sits inside the playable rectangle. A small tolerance absorbs
        /// float error from the lane clamp, so a position resting exactly on the inset edge
        /// is not reported as out of bounds.
        /// </summary>
        public static bool IsWithinBounds(
            Vector3 point, float minX, float maxX, float minZ, float maxZ, float tolerance = 0.01f)
        {
            return point.x >= minX - tolerance
                && point.x <= maxX + tolerance
                && point.z >= minZ - tolerance
                && point.z <= maxZ + tolerance;
        }

        /// <summary>
        /// Spawn-overlap test used at spawn time only. Diagnostics NEVER repositions an
        /// enemy as a result: the spawner's own nudge system stays authoritative and this
        /// merely records what the nudge left behind.
        /// </summary>
        public static bool IsOverlapping(Vector3 candidate, IReadOnlyList<Vector3> occupied, float clearanceRadius)
        {
            if (occupied == null)
            {
                return false;
            }

            for (int i = 0; i < occupied.Count; i++)
            {
                if (PlanarDistance(candidate, occupied[i]) < clearanceRadius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Distance to the closest occupied point, or -1 when there are none.</summary>
        public static float NearestDistance(Vector3 candidate, IReadOnlyList<Vector3> occupied)
        {
            if (occupied == null || occupied.Count == 0)
            {
                return -1f;
            }

            float nearest = float.MaxValue;

            for (int i = 0; i < occupied.Count; i++)
            {
                float distance = PlanarDistance(candidate, occupied[i]);

                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            return nearest;
        }

        /// <summary>True when the sequence contains the same value more than once.</summary>
        public static bool HasDuplicates(IReadOnlyList<int> values)
        {
            if (values == null || values.Count < 2)
            {
                return false;
            }

            HashSet<int> seen = new HashSet<int>();

            for (int i = 0; i < values.Count; i++)
            {
                if (!seen.Add(values[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="order"/> is a permutation of 0..count-1: every slot
        /// present exactly once, nothing missing, nothing repeated, nothing out of range.
        /// This is the shape a correct upgrade shuffle must always produce.
        /// </summary>
        public static bool IsPermutation(IReadOnlyList<int> order, int count)
        {
            if (order == null || count < 0 || order.Count != count)
            {
                return false;
            }

            HashSet<int> seen = new HashSet<int>();

            for (int i = 0; i < order.Count; i++)
            {
                int value = order[i];

                if (value < 0 || value >= count || !seen.Add(value))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Mission-structure invariant discovered in Milestone 1M: a section may only
        /// activate AHEAD of the previous section's stop line, otherwise the next section
        /// triggers the instant the previous one clears and the player never walks forward.
        /// Each section must also have room to fight, i.e. activationZ &lt; forwardLimitZ.
        /// </summary>
        public static bool IsStrictlyForwardProgressing(
            IReadOnlyList<float> activationZ, IReadOnlyList<float> forwardLimitZ)
        {
            if (activationZ == null || forwardLimitZ == null || activationZ.Count != forwardLimitZ.Count)
            {
                return false;
            }

            for (int i = 0; i < activationZ.Count; i++)
            {
                if (activationZ[i] >= forwardLimitZ[i])
                {
                    return false;
                }

                if (i > 0 && activationZ[i] <= forwardLimitZ[i - 1])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when two resolution timestamps are close enough to count as "at the same
        /// time", which is how the one-pickup-at-a-time invariant is falsified.
        /// </summary>
        public static bool AreSimultaneous(float timeA, float timeB, float tolerance = 0.05f)
        {
            return Mathf.Abs(timeA - timeB) <= tolerance;
        }

        /// <summary>
        /// True when two live time windows overlap. Used to prove no two upgrade pickups
        /// were ever collectable at once. An unresolved window is passed as a very large
        /// end time by the caller.
        /// </summary>
        public static bool WindowsOverlap(float startA, float endA, float startB, float endB)
        {
            return startA < endB && startB < endA;
        }
    }
}
