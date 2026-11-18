using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;

namespace ExtractionShooter.Core.Hideout
{
    /// <summary>Why a craft can or cannot start.</summary>
    public enum CraftCheck
    {
        Ok,
        InvalidRecipe,
        MissingInputs,
        NotEnoughCredits,
        QueueFull,
    }

    /// <summary>
    /// Workbench rules against the stash. Pure logic, unit-tested. Only plain stacks count as inputs: an item carrying
    /// state (a loaded or modded weapon, worn armor, a bag) is never consumed by a recipe. Every change is all or
    /// nothing: inputs are only taken if all of them are there, and an output that does not fit changes nothing.
    /// </summary>
    public static class Crafting
    {
        /// <summary>How many of an item the stash holds in plain stacks (what a recipe may use).</summary>
        public static int CountAvailable(SlotInventory stash, string itemId)
        {
            if (stash == null || string.IsNullOrEmpty(itemId)) return 0;

            int total = 0;
            for (int slot = 0; slot < stash.Capacity; slot++)
            {
                ItemStack stack = stash[slot];
                if (IsUsable(stack, itemId)) total += stack.Quantity;
            }
            return total;
        }

        /// <summary>True if the stash holds every input in the needed amount.</summary>
        public static bool HasInputs(CraftRecipe recipe, SlotInventory stash)
        {
            if (recipe == null || stash == null) return false;
            foreach (ItemAmount need in recipe.Requirements())
                if (CountAvailable(stash, need.ItemId) < need.Quantity) return false;
            return true;
        }

        /// <summary>Whether a craft could start now (inputs first, then credits, then a free queue slot).</summary>
        public static CraftCheck Check(CraftRecipe recipe, SlotInventory stash, long credits, int freeQueueSlots)
        {
            if (recipe == null || !recipe.IsValid) return CraftCheck.InvalidRecipe;
            if (!HasInputs(recipe, stash)) return CraftCheck.MissingInputs;
            if (recipe.CreditCost > credits) return CraftCheck.NotEnoughCredits;
            return freeQueueSlots > 0 ? CraftCheck.Ok : CraftCheck.QueueFull;
        }

        /// <summary>Takes every input from the stash (last slots first), or nothing if any is short.</summary>
        public static bool TryConsumeInputs(SlotInventory stash, CraftRecipe recipe)
        {
            if (recipe == null || !recipe.IsValid || !HasInputs(recipe, stash)) return false;

            var taken = new List<(int slot, ItemStack original)>();
            if (Consume(stash, recipe.Requirements(), taken)) return true;

            Restore(stash, taken);
            return false;
        }

        /// <summary>Puts the whole output in the stash, or nothing (no partial outputs).</summary>
        public static bool TryPlaceOutput(SlotInventory stash, in ItemStack output)
        {
            if (stash == null || output.IsEmpty || stash.SpaceFor(output) < output.Quantity) return false;

            stash.TryAdd(output, out int added);
            if (added == output.Quantity) return true;

            // SpaceFor and TryAdd agree, so this does not happen; keeps items conserved if it ever does.
            if (added > 0) stash.Remove(output.ItemId, added);
            return false;
        }

        /// <summary>
        /// Consumes the inputs and places the output in one step (an instant craft). If the output does not fit, the
        /// inputs are put back exactly where they were and false is returned.
        /// </summary>
        public static bool TryCraftNow(SlotInventory stash, CraftRecipe recipe, in ItemStack output)
        {
            if (recipe == null || !recipe.IsValid || output.IsEmpty || !HasInputs(recipe, stash)) return false;

            var taken = new List<(int slot, ItemStack original)>();
            if (Consume(stash, recipe.Requirements(), taken) && TryPlaceOutput(stash, output)) return true;

            Restore(stash, taken);
            return false;
        }

        private static bool IsUsable(in ItemStack stack, string itemId) =>
            !stack.IsEmpty && !stack.IsStateful && string.Equals(stack.ItemId, itemId, StringComparison.Ordinal);

        private static bool Consume(SlotInventory stash, IReadOnlyList<ItemAmount> needs, List<(int slot, ItemStack original)> taken)
        {
            foreach (ItemAmount need in needs)
            {
                int remaining = need.Quantity;
                for (int slot = stash.Capacity - 1; slot >= 0 && remaining > 0; slot--)
                {
                    ItemStack stack = stash[slot];
                    if (!IsUsable(stack, need.ItemId)) continue;

                    int count = Math.Min(remaining, stack.Quantity);
                    taken.Add((slot, stack));
                    stash.TakeFromSlot(slot, count);
                    remaining -= count;
                }
                if (remaining > 0) return false;
            }
            return true;
        }

        /// <summary>Puts taken stacks back exactly as they were (same slot, quantity and orientation).</summary>
        private static void Restore(SlotInventory stash, List<(int slot, ItemStack original)> taken)
        {
            for (int i = taken.Count - 1; i >= 0; i--)
            {
                (int slot, ItemStack original) = taken[i];
                stash.TakeFromSlot(slot, int.MaxValue);
                if (stash.TryPlace(slot, original) != InventoryResult.Success) stash.TryAdd(original.WithRotation(false), out _);
            }
        }
    }
}
