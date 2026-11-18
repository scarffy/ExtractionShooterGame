using ExtractionShooter.Core.Inventory;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>Kilograms of a container's items, including loaded rounds and fitted attachments (bag contents are their own containers).</summary>
    public static class GearWeight
    {
        public static float Of(ItemContainer container, ItemDatabase database)
        {
            if (container == null || database == null) return 0f;
            float total = 0f;
            for (int i = 0; i < container.SlotCount; i++) total += Of(container.GetSlot(i), database);
            return total;
        }

        public static float Of(in ItemStack stack, ItemDatabase database)
        {
            if (stack.IsEmpty || database == null) return 0f;
            float total = WeightOf(stack.ItemId, database) * stack.Quantity;
            if (stack.AmmoCount > 0) total += WeightOf(stack.AmmoId, database) * stack.AmmoCount;
            foreach (var fitted in WeaponMods.All(stack.Attachments)) total += WeightOf(fitted.Value, database);
            return total;
        }

        /// <summary>"17.3 / 70 kg".</summary>
        public static string Format(float kilograms, float maxLoad) => $"{kilograms:0.0} / {maxLoad:0} kg";

        private static float WeightOf(string itemId, ItemDatabase database) =>
            database.TryGet(itemId, out ItemDefinition definition) ? definition.Weight : 0f;
    }
}
