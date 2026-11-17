using ExtractionShooter.Core.Inventory;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>What a container's items are worth at their listed value, including fitted attachments and loaded rounds.</summary>
    public static class GearValue
    {
        public static long Of(ItemContainer container, ItemDatabase database)
        {
            if (container == null || database == null) return 0;
            long total = 0;
            for (int i = 0; i < container.SlotCount; i++) total += Of(container.GetSlot(i), database);
            return total;
        }

        public static long Of(in ItemStack stack, ItemDatabase database)
        {
            if (stack.IsEmpty || database == null) return 0;
            long total = ValueOf(stack.ItemId, database) * stack.Quantity;
            if (stack.AmmoCount > 0) total += ValueOf(stack.AmmoId, database) * stack.AmmoCount;
            foreach (var fitted in WeaponMods.All(stack.Attachments)) total += ValueOf(fitted.Value, database);
            return total;
        }

        private static long ValueOf(string itemId, ItemDatabase database) =>
            database.TryGet(itemId, out ItemDefinition definition) ? definition.Value : 0;
    }
}
