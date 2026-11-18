using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;

namespace ExtractionShooter.Core.Hideout
{
    /// <summary>
    /// How a preset would be built from the stash and the trader: which owned parts are used, what must be bought and
    /// what that costs. Produced by <see cref="PresetAssembly.Plan"/>.
    /// </summary>
    public sealed class PresetPlan
    {
        /// <summary>Stash slot of an owned weapon without attachments that will be used, or -1 to buy one.</summary>
        public int OwnedWeaponSlot = -1;

        /// <summary>Stash slots of loose attachments that will be fitted.</summary>
        public readonly List<int> OwnedAttachmentSlots = new List<int>();

        /// <summary>Item ids to buy from the trader, one each.</summary>
        public readonly List<string> ToBuy = new List<string>();

        /// <summary>Total price of <see cref="ToBuy"/>.</summary>
        public long Cost;

        /// <summary>The first needed item the trader does not sell, or null.</summary>
        public string Unavailable;

        public bool CanBuy => Unavailable == null;
    }

    /// <summary>
    /// "Buy preset": builds a saved weapon preset into the stash, using owned parts first and buying the rest.
    /// Pure logic, unit-tested; the caller checks and charges credits around <see cref="TryAssemble"/>.
    /// </summary>
    public static class PresetAssembly
    {
        /// <summary>
        /// Works out the purchase. Owned parts: the first weapon of the preset's type with no attachments fitted
        /// (loaded rounds are kept), and loose attachments. <paramref name="buyPrice"/> returns the trader's price for one
        /// item, or a negative number when the trader does not sell it.
        /// </summary>
        public static PresetPlan Plan(in WeaponPreset preset, SlotInventory stash, Func<string, long> buyPrice)
        {
            if (buyPrice == null) throw new ArgumentNullException(nameof(buyPrice));
            var plan = new PresetPlan();
            if (string.IsNullOrEmpty(preset.WeaponId))
            {
                plan.Unavailable = string.Empty;
                return plan;
            }

            plan.OwnedWeaponSlot = FindBareWeapon(stash, preset.WeaponId);
            if (plan.OwnedWeaponSlot < 0) AddPurchase(plan, preset.WeaponId, buyPrice);

            var reserved = new HashSet<int>();
            foreach (KeyValuePair<AttachmentSlot, string> fitted in WeaponMods.All(preset.Attachments))
            {
                int slot = FindLoose(stash, fitted.Value, reserved);
                if (slot >= 0)
                {
                    reserved.Add(slot);
                    plan.OwnedAttachmentSlots.Add(slot);
                }
                else AddPurchase(plan, fitted.Value, buyPrice);
            }
            return plan;
        }

        /// <summary>
        /// Takes the planned owned parts out of the stash and puts the assembled weapon in it. All or nothing: if the
        /// stash changed since the plan or the weapon does not fit, the stash is left exactly as it was.
        /// </summary>
        public static bool TryAssemble(SlotInventory stash, in WeaponPreset preset, PresetPlan plan)
        {
            if (stash == null || plan == null || !plan.CanBuy || string.IsNullOrEmpty(preset.WeaponId)) return false;
            if (plan.OwnedWeaponSlot >= 0 && !IsBareWeapon(stash, plan.OwnedWeaponSlot, preset.WeaponId)) return false;

            var expected = new List<string>();
            foreach (KeyValuePair<AttachmentSlot, string> fitted in WeaponMods.All(preset.Attachments)) expected.Add(fitted.Value);
            foreach (int slot in plan.OwnedAttachmentSlots)
            {
                if (!stash.IsValidSlot(slot) || stash[slot].IsEmpty || stash[slot].IsStateful || !expected.Remove(stash[slot].ItemId)) return false;
            }

            var taken = new List<(int slot, ItemStack original)>();
            ItemStack weapon = new ItemStack(preset.WeaponId, 1);
            if (plan.OwnedWeaponSlot >= 0)
            {
                ItemStack original = stash[plan.OwnedWeaponSlot];
                taken.Add((plan.OwnedWeaponSlot, original));
                weapon = stash.TakeFromSlot(plan.OwnedWeaponSlot, 1);
            }
            foreach (int slot in plan.OwnedAttachmentSlots)
            {
                taken.Add((slot, stash[slot]));
                stash.TakeFromSlot(slot, 1);
            }

            ItemStack assembled = weapon.WithAttachments(WeaponMods.Normalize(preset.Attachments));
            stash.TryAdd(assembled, out int added);
            if (added == 1) return true;

            for (int i = taken.Count - 1; i >= 0; i--)
            {
                (int slot, ItemStack original) = taken[i];
                stash.TakeFromSlot(slot, int.MaxValue);
                if (stash.TryPlace(slot, original) != InventoryResult.Success) stash.TryAdd(original.WithRotation(false), out _);
            }
            return false;
        }

        private static void AddPurchase(PresetPlan plan, string itemId, Func<string, long> buyPrice)
        {
            long price = buyPrice(itemId);
            if (price < 0)
            {
                if (plan.Unavailable == null) plan.Unavailable = itemId;
                return;
            }
            plan.ToBuy.Add(itemId);
            plan.Cost += price;
        }

        private static int FindBareWeapon(SlotInventory stash, string weaponId)
        {
            if (stash == null) return -1;
            for (int slot = 0; slot < stash.Capacity; slot++)
                if (IsBareWeapon(stash, slot, weaponId)) return slot;
            return -1;
        }

        private static bool IsBareWeapon(SlotInventory stash, int slot, string weaponId)
        {
            if (!stash.IsValidSlot(slot)) return false;
            ItemStack stack = stash[slot];
            return !stack.IsEmpty && string.Equals(stack.ItemId, weaponId, StringComparison.Ordinal) && string.IsNullOrEmpty(stack.Attachments) &&
                   !stack.HasContents && stack.Durability == 0;
        }

        private static int FindLoose(SlotInventory stash, string itemId, HashSet<int> reserved)
        {
            if (stash == null) return -1;
            for (int slot = 0; slot < stash.Capacity; slot++)
            {
                ItemStack stack = stash[slot];
                if (!stack.IsEmpty && !stack.IsStateful && !reserved.Contains(slot) &&
                    string.Equals(stack.ItemId, itemId, StringComparison.Ordinal))
                    return slot;
            }
            return -1;
        }
    }
}
