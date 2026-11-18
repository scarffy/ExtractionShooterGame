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

        /// <summary>Bag storage: anything except bags with items in them (an empty bag can be carried in another).</summary>
        Storage = 2,

        /// <summary>Pockets: small (1x1) items only, no bags.</summary>
        Pockets = 3,

        /// <summary>The safe box (kept on death): anything except bags, even empty ones.</summary>
        SafeBox = 4,
    }

    internal static class SlotFilters
    {
        public static ISlotFilter Create(ContainerSlotRules rules, ItemDatabase database, ItemContainer owner = null)
        {
            switch (rules)
            {
                case ContainerSlotRules.Equipment: return new EquipmentSlotFilter(database);
                case ContainerSlotRules.Storage: return new StorageSlotFilter(owner);
                case ContainerSlotRules.SafeBox: return NoBagsSlotFilter.Instance;
                case ContainerSlotRules.Pockets: return new PocketSlotFilter(database);
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

        /// <summary>Only items a single cell big, and never bags.</summary>
        private sealed class PocketSlotFilter : ISlotFilter
        {
            private readonly ItemDatabase _database;

            public PocketSlotFilter(ItemDatabase database) => _database = database;

            public bool CanAccept(int slot, in ItemStack stack) =>
                !stack.HasContents && _database != null && _database.GetSize(stack.ItemId).Area == 1;
        }

        /// <summary>
        /// Bag storage: a bag may come in only while it is empty and is not this storage's own bag (checked on the
        /// server; a client-side layout has no owner and refuses bags). Nothing can be put into a bag lying in
        /// another bag (only bags in the stash or the world can be opened), so a carried bag stays empty and bags
        /// can never contain themselves.
        /// </summary>
        private sealed class StorageSlotFilter : ISlotFilter
        {
            private readonly ItemContainer _owner;

            public StorageSlotFilter(ItemContainer owner) => _owner = owner;

            public bool CanAccept(int slot, in ItemStack stack) =>
                !stack.HasContents || _owner != null && _owner.ServerCanHoldBag(stack);
        }

        /// <summary>No bags at all, not even empty ones.</summary>
        private sealed class NoBagsSlotFilter : ISlotFilter
        {
            public static readonly NoBagsSlotFilter Instance = new NoBagsSlotFilter();

            public bool CanAccept(int slot, in ItemStack stack) => !stack.HasContents;
        }
    }
}
