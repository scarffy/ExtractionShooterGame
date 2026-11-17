using System;
using System.Collections.Generic;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>
    /// Decides what a reload does, as pure logic:
    /// keep the loaded ammo type if more of it is available, otherwise switch to the first compatible
    /// type found (in the order given) and unload the old rounds. Never loads past the magazine size.
    /// </summary>
    public static class ReloadPlanner
    {
        public readonly struct Plan
        {
            public Plan(string ammoId, int roundsToTake, string unloadAmmoId, int roundsToUnload, int roundsAfter)
            {
                AmmoId = ammoId;
                RoundsToTake = roundsToTake;
                UnloadAmmoId = unloadAmmoId;
                RoundsToUnload = roundsToUnload;
                RoundsAfter = roundsAfter;
            }

            /// <summary>Ammo type in the magazine after the reload.</summary>
            public string AmmoId { get; }

            /// <summary>Rounds to take from storage.</summary>
            public int RoundsToTake { get; }

            /// <summary>Ammo type to put back into storage (when switching type), or null.</summary>
            public string UnloadAmmoId { get; }

            public int RoundsToUnload { get; }

            public int RoundsAfter { get; }

            public bool IsNoOp => RoundsToTake == 0 && RoundsToUnload == 0;
        }

        /// <param name="loadedAmmoId">Currently loaded type (may be null).</param>
        /// <param name="loadedCount">Rounds currently loaded.</param>
        /// <param name="magazineSize">Magazine capacity.</param>
        /// <param name="available">Compatible ammo types in storage with counts, in preference order.</param>
        public static Plan Create(string loadedAmmoId, int loadedCount, int magazineSize,
            IReadOnlyList<KeyValuePair<string, int>> available)
        {
            if (magazineSize <= 0) throw new ArgumentOutOfRangeException(nameof(magazineSize));
            loadedCount = Math.Max(0, Math.Min(loadedCount, magazineSize));
            if (string.IsNullOrEmpty(loadedAmmoId)) loadedCount = 0;

            int sameTypeAvailable = 0;
            string firstOther = null;
            int firstOtherAvailable = 0;
            if (available != null)
            {
                foreach (KeyValuePair<string, int> entry in available)
                {
                    if (entry.Value <= 0 || string.IsNullOrEmpty(entry.Key)) continue;
                    if (entry.Key == loadedAmmoId) sameTypeAvailable += entry.Value;
                    else if (firstOther == null) { firstOther = entry.Key; firstOtherAvailable = entry.Value; }
                    else if (entry.Key == firstOther) firstOtherAvailable += entry.Value;
                }
            }

            // Keep the loaded type whenever more of it is available (even if the magazine is empty).
            if (!string.IsNullOrEmpty(loadedAmmoId) && sameTypeAvailable > 0)
            {
                int take = Math.Min(magazineSize - loadedCount, sameTypeAvailable);
                return new Plan(loadedAmmoId, take, null, 0, loadedCount + take);
            }

            // Otherwise switch to the first other compatible type, unloading what is in the gun.
            if (firstOther != null)
            {
                int take = Math.Min(magazineSize, firstOtherAvailable);
                bool unload = loadedCount > 0;
                return new Plan(firstOther, take, unload ? loadedAmmoId : null, unload ? loadedCount : 0, take);
            }

            // Nothing compatible to load: unchanged.
            return new Plan(loadedAmmoId, 0, null, 0, loadedCount);
        }
    }
}
