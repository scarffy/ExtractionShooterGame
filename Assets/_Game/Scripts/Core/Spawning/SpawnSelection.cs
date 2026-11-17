using System.Collections.Generic;
using UnityEngine;

namespace ExtractionShooter.Core.Spawning
{
    /// <summary>Picks spawn points away from danger. Pure logic, unit-tested.</summary>
    public static class SpawnSelection
    {
        /// <summary>
        /// Index of the candidate whose nearest threat is farthest away. With no threats, or on a tie,
        /// the first candidate at or after <paramref name="startIndex"/> wins (so spawns still rotate).
        /// Returns -1 when there are no candidates.
        /// </summary>
        public static int PickSafest(IReadOnlyList<Vector3> candidates, IReadOnlyList<Vector3> threats, int startIndex)
        {
            if (candidates == null || candidates.Count == 0) return -1;

            int count = candidates.Count;
            int start = ((startIndex % count) + count) % count;
            int best = start;
            float bestDistance = float.MinValue;

            for (int step = 0; step < count; step++)
            {
                int index = (start + step) % count;
                float nearest = float.MaxValue;
                if (threats != null)
                {
                    foreach (Vector3 threat in threats)
                    {
                        float distance = (candidates[index] - threat).sqrMagnitude;
                        if (distance < nearest) nearest = distance;
                    }
                }

                if (nearest > bestDistance)
                {
                    best = index;
                    bestDistance = nearest;
                }
            }
            return best;
        }
    }
}
