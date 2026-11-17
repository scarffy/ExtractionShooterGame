namespace ExtractionShooter.Core.Inventory
{
    /// <summary>Outcome of an inventory operation.</summary>
    public enum InventoryResult
    {
        /// <summary>Everything requested was done.</summary>
        Success,

        /// <summary>Only part of the quantity fit; the rest stayed where it was.</summary>
        Partial,

        /// <summary>Nothing changed because there was no room.</summary>
        NoSpace,

        InvalidSlot,
        EmptySource,
        InvalidQuantity,
        UnknownItem,
    }
}
