namespace ExtractionShooter.Core.Inventory
{
    /// <summary>
    /// Decides which items a slot may hold (equipment slot types, "no bags inside bags").
    /// Checked for every item entering a slot, including the item coming back in a swap.
    /// </summary>
    public interface ISlotFilter
    {
        bool CanAccept(int slot, in ItemStack stack);
    }

    /// <summary>Filter that accepts everything.</summary>
    public sealed class AcceptAllSlotFilter : ISlotFilter
    {
        public static readonly AcceptAllSlotFilter Instance = new AcceptAllSlotFilter();

        private AcceptAllSlotFilter() { }

        public bool CanAccept(int slot, in ItemStack stack) => true;
    }
}
