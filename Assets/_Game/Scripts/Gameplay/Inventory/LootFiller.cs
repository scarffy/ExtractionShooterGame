using System;
using ExtractionShooter.Core.Inventory;
using FishNet.Managing;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>Server only: rolls a loot table into an inventory (crates, bot bodies).</summary>
    public static class LootFiller
    {
        /// <summary>
        /// Adds one roll of the table. Items are created through <see cref="ItemFactory"/> so armor and bags
        /// get their state; stops at the first item that does not fit (its state is destroyed, never leaked).
        /// </summary>
        public static void ServerFill(NetworkManager networkManager, LootTable table, SlotInventory inventory, Random random,
            int rollBonus = 0)
        {
            if (table == null || inventory == null || random == null) return;

            foreach (LootDrop drop in table.Roll(random, rollBonus))
            {
                // Stateful items (armor, bags, weapons) are created one at a time.
                int count = drop.Definition.MaxStack == 1 ? drop.Quantity : 1;
                int quantity = drop.Definition.MaxStack == 1 ? 1 : drop.Quantity;

                for (int i = 0; i < count; i++)
                {
                    ItemStack item = ItemFactory.ServerCreate(networkManager, drop.Definition, quantity);
                    if (item.IsEmpty) continue;
                    if (!string.IsNullOrEmpty(drop.Attachments)) item = item.WithAttachments(drop.Attachments);

                    inventory.TryAdd(item, out int added);
                    if (added == 0)
                    {
                        ItemFactory.ServerDestroy(networkManager, item);
                        return;
                    }
                }
            }
        }
    }
}
