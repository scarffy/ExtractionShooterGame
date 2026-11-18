using System;
using System.Collections.Generic;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>
    /// Server side: which slots changed since the last send, or that everything must be resent (resize).
    /// Only indices are kept; the values are read when the changes are sent, so a change sent after a full copy
    /// (or twice) is harmless: every message carries absolute slot values.
    /// </summary>
    public sealed class SlotChangeSet
    {
        private readonly HashSet<int> _dirty = new HashSet<int>();

        /// <summary>True when nothing waits to be sent.</summary>
        public bool IsEmpty => !NeedsFullCopy && _dirty.Count == 0;

        /// <summary>The slot count or grid changed: every holder needs a full copy instead of changes.</summary>
        public bool NeedsFullCopy { get; private set; }

        public void Mark(int index)
        {
            if (index >= 0 && !NeedsFullCopy) _dirty.Add(index);
        }

        public void MarkAll()
        {
            NeedsFullCopy = true;
            _dirty.Clear();
        }

        /// <summary>
        /// Reads the changed slots of <paramref name="store"/> (ascending, out-of-range indices dropped) and clears the set.
        /// Does nothing useful when <see cref="NeedsFullCopy"/>; use <see cref="SlotReplication.Capture"/> then.
        /// </summary>
        public void Drain(ISlotStore store, out int[] indices, out ItemStack[] stacks)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var sorted = new List<int>(_dirty.Count);
            foreach (int index in _dirty)
                if (index < store.Count) sorted.Add(index);
            sorted.Sort();

            indices = sorted.ToArray();
            stacks = new ItemStack[indices.Length];
            for (int i = 0; i < indices.Length; i++) stacks[i] = store[indices[i]];
            Clear();
        }

        public void Clear()
        {
            NeedsFullCopy = false;
            _dirty.Clear();
        }
    }

    /// <summary>Helpers shared by the server (capture) and clients (<see cref="SlotReplica"/>).</summary>
    public static class SlotReplication
    {
        /// <summary>A full copy of a store as its non-empty slots only (empty slots are implied).</summary>
        public static void Capture(ISlotStore store, out int[] indices, out ItemStack[] stacks)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            int filled = 0;
            for (int i = 0; i < store.Count; i++)
                if (!store[i].IsEmpty) filled++;

            indices = new int[filled];
            stacks = new ItemStack[filled];
            for (int i = 0, n = 0; i < store.Count; i++)
            {
                if (store[i].IsEmpty) continue;
                indices[n] = i;
                stacks[n++] = store[i];
            }
        }
    }

    /// <summary>
    /// Client side: this peer's copy of a container it is allowed to see. Empty (0 slots) until a full copy arrives;
    /// slot changes before that are ignored, and a full copy always replaces everything.
    /// Read-only as an <see cref="ISlotStore"/>: UI rules views (<see cref="SlotInventory"/>) read it, nothing writes through it.
    /// </summary>
    public sealed class SlotReplica : ISlotStore
    {
        private ItemStack[] _slots = Array.Empty<ItemStack>();

        /// <summary>True between a full copy and the next <see cref="Clear"/>.</summary>
        public bool HasCopy { get; private set; }

        /// <summary>Cells per row of the copied container, 0 for plain slots.</summary>
        public int GridWidth { get; private set; }

        public int Count => _slots.Length;

        public ItemStack this[int index]
        {
            get => index >= 0 && index < _slots.Length ? _slots[index] : ItemStack.Empty;
            set => throw new InvalidOperationException("A replicated container is read-only on clients; ask the server to change it.");
        }

        /// <summary>
        /// Replaces the copy: <paramref name="capacity"/> slots, all empty except the listed ones.
        /// Entries with a bad index are skipped. A grid width that does not divide the capacity becomes 0 (plain slots).
        /// </summary>
        public void ApplyFullCopy(int capacity, int gridWidth, int[] indices, ItemStack[] stacks)
        {
            capacity = Math.Max(0, capacity);
            _slots = capacity == 0 ? Array.Empty<ItemStack>() : new ItemStack[capacity];
            GridWidth = gridWidth > 0 && capacity % gridWidth == 0 ? gridWidth : 0;
            HasCopy = true;
            Write(indices, stacks);
        }

        /// <summary>Applies changed slots. Returns false (and changes nothing) when no full copy has arrived yet.</summary>
        public bool ApplyChanges(int[] indices, ItemStack[] stacks)
        {
            if (!HasCopy) return false;
            Write(indices, stacks);
            return true;
        }

        /// <summary>Drops the copy (no longer allowed to see the contents).</summary>
        public void Clear()
        {
            _slots = Array.Empty<ItemStack>();
            GridWidth = 0;
            HasCopy = false;
        }

        private void Write(int[] indices, ItemStack[] stacks)
        {
            if (indices == null || stacks == null) return;
            int count = Math.Min(indices.Length, stacks.Length);
            for (int i = 0; i < count; i++)
            {
                int index = indices[i];
                if (index >= 0 && index < _slots.Length) _slots[index] = stacks[i];
            }
        }
    }
}
