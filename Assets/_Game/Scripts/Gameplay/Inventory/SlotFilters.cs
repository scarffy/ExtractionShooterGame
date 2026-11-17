using ExtractionShooter.Core.Inventory;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>Which slot rules a container enforces.</summary>
    public enum ContainerSlotRules
    {
        /// <summary>Anything goes (crates, corpses).</summary>
        Any = 0,

        /// <summary>Player equipment: one typed slot per <see cref="EquipmentSlot"/>.</summary>
        Equipment = 1,

        /// <summary>Bag storage: anything except other bags.</summary>
        Storage = 2,
    }

    internal static class SlotFilters
    {
        public static ISlotFilter Create(ContainerSlotRules rules, ItemDatabase database)
        {
            switch (rules)
            {
                case ContainerSlotRules.Equipment: return new EquipmentSlotFilter(database);
                case ContainerSlotRules.Storage: return StorageSlotFilter.Instance;
                default: return AcceptAllSlotFilter.Instance;
            }
        }

        private sealed class EquipmentSlotFilter : ISlotFilter
        {
            private readonly ItemDatabase _database;

            public EquipmentSlotFilter(ItemDatabase database) => _database = database;

            public bool CanAccept(int slot, in ItemStack stack) =>
                slot >= 0 && slot < EquipmentRules.SlotCount &&
                _database != null && _database.TryGet(stack.ItemId, out ItemDefinition item) &&
                EquipmentRules.Accepts((EquipmentSlot)slot, item);
        }

        /// <summary>No bags inside bags: keeps storage one level deep and makes cycles impossible.</summary>
        private sealed class StorageSlotFilter : ISlotFilter
        {
            public static readonly StorageSlotFilter Instance = new StorageSlotFilter();

            public bool CanAccept(int slot, in ItemStack stack) => !stack.HasContents;
        }
    }
}
