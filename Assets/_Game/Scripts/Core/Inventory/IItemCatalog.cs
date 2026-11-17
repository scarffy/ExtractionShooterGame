namespace ExtractionShooter.Core.Inventory
{
    /// <summary>
    /// Read-only lookup of item rules needed by inventory logic.
    /// </summary>
    public interface IItemCatalog
    {
        /// <summary>True if the id belongs to a known item.</summary>
        bool Contains(string itemId);

        /// <summary>Maximum quantity per slot for the item (at least 1 for known items).</summary>
        int GetMaxStack(string itemId);
    }
}
