using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;

namespace ExtractionShooter.Core.Persistence
{
    /// <summary>
    /// Converts between live <see cref="ItemStack"/>s and stored items, and validates stored data
    /// on load (unknown items dropped, quantities clamped to the catalog, slots kept in range and unique).
    /// Bag contents are handled by the caller, which owns the live storage containers.
    /// </summary>
    public static class ProfileCodec
    {
        public static StoredItem ToStored(int slot, in ItemStack stack, string contentsKey = null) => new StoredItem
        {
            Slot = slot,
            ItemId = stack.ItemId,
            Quantity = stack.Quantity,
            Durability = stack.Durability,
            AmmoId = stack.AmmoId,
            AmmoCount = stack.AmmoCount,
            ContentsKey = contentsKey ?? string.Empty,
        };

        /// <summary>
        /// The live stack for a stored item, without bag contents (the caller attaches a contents handle).
        /// False when the item is unknown or empty.
        /// </summary>
        public static bool TryFromStored(StoredItem stored, IItemCatalog catalog, out ItemStack stack)
        {
            stack = ItemStack.Empty;
            if (stored == null || string.IsNullOrEmpty(stored.ItemId) || catalog == null || !catalog.Contains(stored.ItemId))
                return false;

            int maxStack = catalog.GetMaxStack(stored.ItemId);
            int quantity = stored.Quantity < 1 ? 1 : stored.Quantity > maxStack ? maxStack : stored.Quantity;

            string ammoId = !string.IsNullOrEmpty(stored.AmmoId) && catalog.Contains(stored.AmmoId) ? stored.AmmoId : null;
            int ammoCount = ammoId != null && stored.AmmoCount > 0 ? stored.AmmoCount : 0;

            stack = new ItemStack(stored.ItemId, quantity, stored.Durability > 0 ? stored.Durability : 0, 0, ammoId, ammoCount);
            return true;
        }

        /// <summary>Items of a stored container that may be placed: valid slot, first occurrence of each slot.</summary>
        public static IEnumerable<StoredItem> PlaceableItems(StoredContainer container, int capacity)
        {
            if (container == null || container.Items == null) yield break;

            var used = new HashSet<int>();
            foreach (StoredItem item in container.Items)
            {
                if (item == null || item.Slot < 0 || item.Slot >= capacity || !used.Add(item.Slot)) continue;
                yield return item;
            }
        }
    }
}
