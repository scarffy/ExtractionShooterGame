namespace ExtractionShooter.Core.Inventory
{
    /// <summary>
    /// Fixed-size slot storage that inventory logic reads and writes.
    /// Lets the same rules run on a plain array (tests, offline) or a replicated list (server).
    /// </summary>
    public interface ISlotStore
    {
        int Count { get; }

        ItemStack this[int index] { get; set; }
    }

    /// <summary>Array-backed <see cref="ISlotStore"/>.</summary>
    public sealed class ArraySlotStore : ISlotStore
    {
        private readonly ItemStack[] _slots;

        public ArraySlotStore(int capacity) => _slots = new ItemStack[capacity];

        public int Count => _slots.Length;

        public ItemStack this[int index]
        {
            get => _slots[index];
            set => _slots[index] = value;
        }
    }
}
