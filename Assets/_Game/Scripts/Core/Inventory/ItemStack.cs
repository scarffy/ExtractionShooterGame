using System;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>
    /// An item (or a stack of identical plain items) occupying one inventory slot.
    /// Public fields so it can be replicated by the networking layer without a custom serializer.
    /// </summary>
    /// <remarks>
    /// Items that carry per-instance state (durability, their own storage, loaded rounds) are "stateful":
    /// they always have a quantity of 1 and never merge with other stacks.
    /// </remarks>
    [Serializable]
    public struct ItemStack : IEquatable<ItemStack>
    {
        /// <summary>Stable item id (matches an item definition). Null or empty means an empty slot.</summary>
        public string ItemId;

        /// <summary>Number of items in the stack. Zero or less means an empty slot.</summary>
        public int Quantity;

        /// <summary>Current durability for items that wear (armor). 0 when the item has no durability.</summary>
        public int Durability;

        /// <summary>
        /// Handle of the storage this item carries (backpacks, rigs): the storage's network object id + 1.
        /// 0 means no storage. Use <see cref="HasContents"/> and <see cref="ContentsObjectId"/>.
        /// </summary>
        public int ContentsHandle;

        /// <summary>Weapons: id of the ammo type loaded in the magazine. Null when nothing has been loaded.</summary>
        public string AmmoId;

        /// <summary>Weapons: rounds currently loaded.</summary>
        public int AmmoCount;

        public ItemStack(string itemId, int quantity, int durability = 0, int contentsHandle = 0, string ammoId = null, int ammoCount = 0)
        {
            ItemId = itemId;
            Quantity = quantity;
            Durability = durability;
            ContentsHandle = contentsHandle;
            AmmoId = string.IsNullOrEmpty(ammoId) ? null : ammoId;
            AmmoCount = ammoCount;
        }

        public static ItemStack Empty => default;

        public readonly bool IsEmpty => string.IsNullOrEmpty(ItemId) || Quantity <= 0;

        /// <summary>True if the item carries per-instance state and must never be merged or split.</summary>
        public readonly bool IsStateful => Durability != 0 || ContentsHandle != 0 || AmmoCount != 0 || !string.IsNullOrEmpty(AmmoId);

        public readonly bool HasContents => ContentsHandle != 0;

        /// <summary>Network object id of the carried storage. Only valid when <see cref="HasContents"/>.</summary>
        public readonly int ContentsObjectId => ContentsHandle - 1;

        public static int ToContentsHandle(int objectId) => objectId + 1;

        /// <summary>True if both are the same plain item and may share one slot.</summary>
        public readonly bool CanStackWith(in ItemStack other) =>
            !IsEmpty && !other.IsEmpty && !IsStateful && !other.IsStateful &&
            string.Equals(ItemId, other.ItemId, StringComparison.Ordinal);

        public readonly ItemStack WithQuantity(int quantity) =>
            quantity > 0 ? new ItemStack(ItemId, quantity, Durability, ContentsHandle, AmmoId, AmmoCount) : Empty;

        public readonly ItemStack WithDurability(int durability) =>
            IsEmpty ? Empty : new ItemStack(ItemId, Quantity, Math.Max(0, durability), ContentsHandle, AmmoId, AmmoCount);

        public readonly ItemStack WithLoadedAmmo(string ammoId, int ammoCount) =>
            IsEmpty ? Empty : new ItemStack(ItemId, Quantity, Durability, ContentsHandle, ammoId, Math.Max(0, ammoCount));

        public readonly bool Equals(ItemStack other) =>
            (IsEmpty && other.IsEmpty) ||
            (Quantity == other.Quantity && Durability == other.Durability && ContentsHandle == other.ContentsHandle &&
             AmmoCount == other.AmmoCount &&
             string.Equals(ItemId, other.ItemId, StringComparison.Ordinal) &&
             string.Equals(NullIfEmpty(AmmoId), NullIfEmpty(other.AmmoId), StringComparison.Ordinal));

        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

        public override readonly bool Equals(object obj) => obj is ItemStack other && Equals(other);

        public override readonly int GetHashCode() =>
            IsEmpty ? 0 : HashCode.Combine(ItemId, Quantity, Durability, ContentsHandle, NullIfEmpty(AmmoId), AmmoCount);

        public override readonly string ToString()
        {
            if (IsEmpty) return "(empty)";
            string state = Durability != 0 ? $" dur {Durability}" : string.Empty;
            if (HasContents) state += $" bag #{ContentsObjectId}";
            if (!string.IsNullOrEmpty(AmmoId) || AmmoCount != 0) state += $" {AmmoId} x{AmmoCount} loaded";
            return $"{ItemId} x{Quantity}{state}";
        }

        public static bool operator ==(ItemStack left, ItemStack right) => left.Equals(right);

        public static bool operator !=(ItemStack left, ItemStack right) => !left.Equals(right);
    }
}
