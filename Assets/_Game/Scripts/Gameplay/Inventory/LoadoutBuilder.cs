using ExtractionShooter.Core.Inventory;
using FishNet.Managing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// Server only: fills an equipment container (a player's worn gear, or a stash loadout) and its bags.
    /// Equippables go to their slot; everything else goes into the chest rig, then the backpack.
    /// </summary>
    public static class LoadoutBuilder
    {
        /// <summary>Gives a kit. Anything that does not fit is destroyed.</summary>
        public static void ServerGive(NetworkManager networkManager, PlayerLoadout kit, ItemContainer equipment)
        {
            if (kit == null || equipment == null) return;

            SlotInventory gear = equipment.ServerInventory;
            if (gear == null) return;

            foreach (LoadoutEntry entry in kit.Items)
            {
                if (entry.Item == null) continue;

                // Stateful items (armor, bags, weapons) are created one at a time.
                int count = entry.Item.MaxStack == 1 ? entry.Quantity : 1;
                int quantity = entry.Item.MaxStack == 1 ? 1 : entry.Quantity;
                for (int i = 0; i < count; i++)
                {
                    ItemStack item = ItemFactory.ServerCreate(networkManager, entry.Item, quantity);
                    if (item.IsEmpty) continue;

                    int added = 0;
                    if (gear.SpaceFor(item) > 0) gear.TryAdd(item, out added);
                    if (added == 0) added = ServerAddToStorage(networkManager, equipment, item);
                    if (added == 0)
                    {
                        Debug.LogWarning($"[{nameof(LoadoutBuilder)}] No room for {entry.Item.name} in kit '{kit.name}'.");
                        ItemFactory.ServerDestroy(networkManager, item);
                    }
                }
            }
        }

        /// <summary>Puts an item into the chest rig, then the backpack, of an equipment container. Returns how many were added.</summary>
        public static int ServerAddToStorage(NetworkManager networkManager, ItemContainer equipment, in ItemStack item)
        {
            int total = 0;
            ItemStack remaining = item;
            foreach (EquipmentSlot slot in new[] { EquipmentSlot.ChestRig, EquipmentSlot.Backpack })
            {
                ItemContainer storage = ResolveStorage(networkManager, equipment, slot);
                SlotInventory inventory = storage != null ? storage.ServerInventory : null;
                if (inventory == null || remaining.IsEmpty) continue;

                inventory.TryAdd(remaining, out int added);
                total += added;
                remaining = remaining.WithQuantity(remaining.Quantity - added);
            }
            return total;
        }

        /// <summary>The storage of the bag worn in an equipment slot, or null.</summary>
        public static ItemContainer ResolveStorage(NetworkManager networkManager, ItemContainer equipment, EquipmentSlot slot)
        {
            if (equipment == null) return null;
            return ItemContainer.TryResolveContents(networkManager, equipment.GetSlot((int)slot), out ItemContainer storage) ? storage : null;
        }

        /// <summary>
        /// Moves every item between two equipment containers slot for slot (bags keep their contents).
        /// Items that cannot take the same slot are placed anywhere they fit. Returns true if everything moved.
        /// </summary>
        public static bool ServerMoveAll(ItemContainer from, ItemContainer to)
        {
            SlotInventory source = from != null ? from.ServerInventory : null;
            SlotInventory target = to != null ? to.ServerInventory : null;
            if (source == null || target == null) return false;

            bool allMoved = true;
            for (int i = 0; i < source.Capacity; i++)
            {
                if (source[i].IsEmpty) continue;
                if (target.IsValidSlot(i) && source.TransferTo(target, i, i) == InventoryResult.Success) continue;
                if (source.TransferTo(target, i) != InventoryResult.Success) allMoved = false;
            }
            return allMoved;
        }
    }
}
