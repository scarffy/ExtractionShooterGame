using System;
using System.Collections.Generic;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>
    /// Uses left on a key. Stored in the key's <see cref="ItemStack.Durability"/>, so it replicates and persists in the profile
    /// like armor durability; a key whose last use is spent is removed. A stack with no durability recorded (0) is a fresh key.
    /// </summary>
    public static class KeyUses
    {
        /// <summary>Uses left: the stored count, capped at <paramref name="maxUses"/>; a fresh key (0 stored) has them all.</summary>
        public static int Remaining(int durability, int maxUses)
        {
            int max = Math.Max(1, maxUses);
            return durability > 0 ? Math.Min(durability, max) : max;
        }

        /// <summary>Uses left after one more use. 0 means the key breaks and is removed.</summary>
        public static int AfterUse(int durability, int maxUses) => Math.Max(0, Remaining(durability, maxUses) - 1);

        /// <summary>Share of uses left, 0..1, for pricing a used key.</summary>
        public static float Condition(int durability, int maxUses) => Remaining(durability, maxUses) / (float)Math.Max(1, maxUses);

        /// <summary>
        /// Spends one use of the key in <paramref name="slot"/>: lowers its uses left, or removes it when that was the last.
        /// Returns the uses left afterwards (0: the key broke and is gone), or -1 if the slot holds nothing.
        /// </summary>
        public static int UseOne(SlotInventory inventory, int slot, int maxUses)
        {
            if (inventory == null || !inventory.IsValidSlot(slot) || inventory[slot].IsEmpty) return -1;

            int after = AfterUse(inventory[slot].Durability, maxUses);
            if (after <= 0) inventory.TakeFromSlot(slot, 1);
            else inventory.SetDurability(slot, after);
            return after;
        }

        /// <summary>
        /// Which of several matching keys to use: the one with the fewest uses left (the first on a tie), so worn keys are
        /// used up before fresh ones. -1 when there are none.
        /// </summary>
        public static int PickKey(IReadOnlyList<int> usesLeft)
        {
            if (usesLeft == null) return -1;
            int best = -1;
            for (int i = 0; i < usesLeft.Count; i++)
            {
                if (usesLeft[i] <= 0) continue;
                if (best < 0 || usesLeft[i] < usesLeft[best]) best = i;
            }
            return best;
        }
    }
}
