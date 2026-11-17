using System.Collections.Generic;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>What modding needs to know about items. Implemented by the gameplay item database.</summary>
    public interface IModCatalog
    {
        /// <summary>True if the item is an attachment, with the slot it goes in.</summary>
        bool TryGetAttachmentSlot(string itemId, out AttachmentSlot slot);

        /// <summary>True if the attachment can go on the weapon.</summary>
        bool Fits(string attachmentId, string weaponId);

        /// <summary>The weapon's magazine size with these attachments, or 0 if it is not a weapon.</summary>
        int MagazineSize(string weaponId, string attachments);
    }

    public enum ModResult
    {
        Fitted,
        NotApplicable,   // not an attachment on a weapon: treat as a normal move
        DoesNotFit,
        NoRoom,          // nowhere to put the replaced attachment or unloaded rounds
    }

    /// <summary>
    /// Fitting and removing weapon attachments inside slot inventories. Item conservation: whatever leaves a weapon
    /// (a replaced attachment, rounds over a smaller magazine) is put back into an inventory or the change is refused.
    /// </summary>
    public static class WeaponModding
    {
        /// <summary>
        /// Fits the attachment at <paramref name="modSlot"/> of <paramref name="from"/> onto the weapon covering
        /// <paramref name="weaponCell"/> of <paramref name="to"/>. A replaced attachment and any rounds over the new
        /// magazine size go to <paramref name="from"/> (then <paramref name="to"/>).
        /// </summary>
        public static ModResult TryFit(SlotInventory from, int modSlot, SlotInventory to, int weaponCell, IModCatalog catalog)
        {
            if (from == null || to == null || catalog == null || !from.IsValidSlot(modSlot)) return ModResult.NotApplicable;
            int weaponSlot = to.AnchorAt(weaponCell);
            if (weaponSlot < 0) return ModResult.NotApplicable;

            ItemStack mod = from[modSlot], weapon = to[weaponSlot];
            if (mod.IsEmpty || weapon.IsEmpty || ReferenceEquals(from, to) && modSlot == weaponSlot) return ModResult.NotApplicable;
            if (!catalog.TryGetAttachmentSlot(mod.ItemId, out AttachmentSlot slot)) return ModResult.NotApplicable;
            if (catalog.MagazineSize(weapon.ItemId, weapon.Attachments) <= 0) return ModResult.NotApplicable;
            if (!catalog.Fits(mod.ItemId, weapon.ItemId)) return ModResult.DoesNotFit;

            string replaced = WeaponMods.Get(weapon.Attachments, slot);
            string attachments = WeaponMods.Set(weapon.Attachments, slot, mod.ItemId);
            int overflow = Overflow(weapon, attachments, catalog);

            // Take the attachment first so its cells are free for what comes back out.
            ItemStack taken = from.TakeFromSlot(modSlot, 1);
            var returned = new List<ItemStack>(2);
            if (replaced != null) returned.Add(new ItemStack(replaced, 1));
            if (overflow > 0) returned.Add(new ItemStack(weapon.AmmoId, overflow));

            if (!TryStoreAll(returned, from, to))
            {
                Restore(from, modSlot, taken);
                return ModResult.NoRoom;
            }

            weaponSlot = to.AnchorAt(weaponCell);
            to.SetAttachments(weaponSlot, attachments);
            if (overflow > 0) to.SetLoadedAmmo(weaponSlot, weapon.AmmoId, weapon.AmmoCount - overflow);
            return ModResult.Fitted;
        }

        /// <summary>
        /// Removes the attachment in <paramref name="slot"/> from the weapon at <paramref name="weaponSlot"/> into the
        /// first of <paramref name="destinations"/> with room (rounds over a smaller magazine go the same way).
        /// </summary>
        public static ModResult TryDetach(SlotInventory inventory, int weaponSlot, AttachmentSlot slot, IModCatalog catalog,
            params SlotInventory[] destinations)
        {
            if (inventory == null || catalog == null || !inventory.IsValidSlot(weaponSlot)) return ModResult.NotApplicable;
            ItemStack weapon = inventory[weaponSlot];
            string fitted = weapon.IsEmpty ? null : WeaponMods.Get(weapon.Attachments, slot);
            if (fitted == null) return ModResult.NotApplicable;

            string attachments = WeaponMods.Set(weapon.Attachments, slot, null);
            int overflow = Overflow(weapon, attachments, catalog);

            var returned = new List<ItemStack>(2) { new ItemStack(fitted, 1) };
            if (overflow > 0) returned.Add(new ItemStack(weapon.AmmoId, overflow));
            if (!TryStoreAll(returned, destinations)) return ModResult.NoRoom;

            inventory.SetAttachments(weaponSlot, attachments);
            if (overflow > 0) inventory.SetLoadedAmmo(weaponSlot, weapon.AmmoId, weapon.AmmoCount - overflow);
            return ModResult.Fitted;
        }

        private static int Overflow(in ItemStack weapon, string attachments, IModCatalog catalog)
        {
            if (weapon.AmmoCount <= 0 || string.IsNullOrEmpty(weapon.AmmoId)) return 0;
            int capacity = catalog.MagazineSize(weapon.ItemId, attachments);
            return capacity > 0 && weapon.AmmoCount > capacity ? weapon.AmmoCount - capacity : 0;
        }

        /// <summary>All or nothing: stores every stack somewhere in the destinations, or changes nothing.</summary>
        private static bool TryStoreAll(List<ItemStack> stacks, params SlotInventory[] destinations)
        {
            var undo = new List<(SlotInventory inventory, ItemStack stack, int added)>();
            foreach (ItemStack stack in stacks)
            {
                int remaining = stack.Quantity;
                foreach (SlotInventory destination in destinations)
                {
                    if (destination == null || remaining <= 0) continue;
                    destination.TryAdd(stack.WithQuantity(remaining), out int added);
                    if (added > 0) undo.Add((destination, stack, added));
                    remaining -= added;
                }
                if (remaining > 0)
                {
                    foreach (var (inventory, item, added) in undo) inventory.Remove(item.ItemId, added);
                    return false;
                }
            }
            return true;
        }

        private static void Restore(SlotInventory inventory, int slot, in ItemStack stack)
        {
            if (inventory.TryPlace(slot, stack) != InventoryResult.Success) inventory.TryAdd(stack, out _);
        }
    }
}
