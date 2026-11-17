using System;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>
    /// Slot-based inventory rules: stacking, adding, removing, moving and transferring
    /// between inventories, with optional per-slot filters (equipment slot types).
    /// Pure C#, no engine or networking dependencies, so it is unit-testable and runs
    /// identically on the server and offline.
    /// Every operation either fully succeeds, partially succeeds (reported), or changes nothing.
    /// </summary>
    public sealed class SlotInventory
    {
        private readonly ISlotStore _store;
        private readonly IItemCatalog _catalog;
        private readonly ISlotFilter _filter;

        public SlotInventory(ISlotStore store, IItemCatalog catalog, ISlotFilter filter = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _filter = filter ?? AcceptAllSlotFilter.Instance;
        }

        public int Capacity => _store.Count;

        public ItemStack this[int slot] => _store[slot];

        public bool IsValidSlot(int slot) => slot >= 0 && slot < _store.Count;

        public bool CanAccept(int slot, in ItemStack stack) => IsValidSlot(slot) && _filter.CanAccept(slot, stack);

        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < _store.Count; i++)
                    if (!_store[i].IsEmpty) return false;
                return true;
            }
        }

        public int CountOf(string itemId)
        {
            int total = 0;
            for (int i = 0; i < _store.Count; i++)
            {
                ItemStack stack = _store[i];
                if (!stack.IsEmpty && stack.ItemId == itemId) total += stack.Quantity;
            }
            return total;
        }

        /// <summary>How many of the given stack's item could be added right now (respecting filters).</summary>
        public int SpaceFor(in ItemStack stack)
        {
            if (stack.IsEmpty || !_catalog.Contains(stack.ItemId)) return 0;

            int maxStack = MaxStack(stack.ItemId);
            bool stackable = !stack.IsStateful && maxStack > 1;
            int space = 0;
            for (int i = 0; i < _store.Count; i++)
            {
                if (!_filter.CanAccept(i, stack)) continue;

                ItemStack existing = _store[i];
                if (existing.IsEmpty) space += stackable ? maxStack : 1;
                else if (stackable && existing.CanStackWith(stack)) space += Math.Max(0, maxStack - existing.Quantity);
            }
            return space;
        }

        public int SpaceFor(string itemId) => SpaceFor(new ItemStack(itemId, 1));

        /// <summary>
        /// Adds up to the stack's quantity. Plain items top up existing stacks first, then fill empty slots.
        /// Stateful items (durability, contents) must have quantity 1 and go into one empty slot.
        /// </summary>
        public InventoryResult TryAdd(in ItemStack stack, out int added)
        {
            added = 0;
            if (stack.Quantity <= 0 || (stack.IsStateful && stack.Quantity != 1)) return InventoryResult.InvalidQuantity;
            if (!_catalog.Contains(stack.ItemId)) return InventoryResult.UnknownItem;

            int maxStack = MaxStack(stack.ItemId);
            int remaining = stack.Quantity;

            if (!stack.IsStateful)
            {
                for (int i = 0; i < _store.Count && remaining > 0; i++)
                {
                    ItemStack existing = _store[i];
                    if (!existing.CanStackWith(stack) || existing.Quantity >= maxStack || !_filter.CanAccept(i, stack)) continue;

                    int moved = Math.Min(remaining, maxStack - existing.Quantity);
                    _store[i] = existing.WithQuantity(existing.Quantity + moved);
                    remaining -= moved;
                }
            }

            for (int i = 0; i < _store.Count && remaining > 0; i++)
            {
                if (!_store[i].IsEmpty || !_filter.CanAccept(i, stack)) continue;

                int moved = Math.Min(remaining, maxStack);
                _store[i] = stack.WithQuantity(moved);
                remaining -= moved;
            }

            added = stack.Quantity - remaining;
            return added == stack.Quantity ? InventoryResult.Success
                 : added > 0 ? InventoryResult.Partial
                 : InventoryResult.NoSpace;
        }

        public InventoryResult TryAdd(string itemId, int quantity, out int added) =>
            TryAdd(new ItemStack(itemId, quantity), out added);

        /// <summary>Puts a stack into one specific slot if it is empty and accepts it.</summary>
        public InventoryResult TryPlace(int slot, in ItemStack stack)
        {
            if (!IsValidSlot(slot)) return InventoryResult.InvalidSlot;
            if (stack.IsEmpty || (stack.IsStateful && stack.Quantity != 1)) return InventoryResult.InvalidQuantity;
            if (!_catalog.Contains(stack.ItemId)) return InventoryResult.UnknownItem;
            if (!_store[slot].IsEmpty || !_filter.CanAccept(slot, stack) || stack.Quantity > MaxStack(stack.ItemId))
                return InventoryResult.NoSpace;

            _store[slot] = stack;
            return InventoryResult.Success;
        }

        /// <summary>Removes up to <paramref name="quantity"/> of the item from any slots, last slot first.</summary>
        public int Remove(string itemId, int quantity)
        {
            if (quantity <= 0) return 0;

            int remaining = quantity;
            for (int i = _store.Count - 1; i >= 0 && remaining > 0; i--)
            {
                ItemStack stack = _store[i];
                if (stack.IsEmpty || stack.ItemId != itemId) continue;

                int taken = Math.Min(remaining, stack.Quantity);
                _store[i] = stack.WithQuantity(stack.Quantity - taken);
                remaining -= taken;
            }
            return quantity - remaining;
        }

        /// <summary>Removes up to <paramref name="quantity"/> from one slot. Returns what was taken.</summary>
        public ItemStack TakeFromSlot(int slot, int quantity)
        {
            if (!IsValidSlot(slot) || quantity <= 0) return ItemStack.Empty;

            ItemStack stack = _store[slot];
            if (stack.IsEmpty) return ItemStack.Empty;

            int taken = Math.Min(quantity, stack.Quantity);
            _store[slot] = stack.WithQuantity(stack.Quantity - taken);
            return stack.WithQuantity(taken);
        }

        /// <summary>Replaces the durability of the item in a slot (armor wear).</summary>
        public void SetDurability(int slot, int durability)
        {
            if (!IsValidSlot(slot) || _store[slot].IsEmpty) return;
            _store[slot] = _store[slot].WithDurability(durability);
        }

        /// <summary>Replaces the loaded ammo of the weapon in a slot.</summary>
        public void SetLoadedAmmo(int slot, string ammoId, int ammoCount)
        {
            if (!IsValidSlot(slot) || _store[slot].IsEmpty) return;
            _store[slot] = _store[slot].WithLoadedAmmo(ammoId, ammoCount);
        }

        public void Clear()
        {
            for (int i = 0; i < _store.Count; i++)
                if (!_store[i].IsEmpty) _store[i] = ItemStack.Empty;
        }

        /// <summary>
        /// Moves items from a slot in this inventory to <paramref name="target"/> (which may be this inventory).
        /// <paramref name="targetSlot"/> -1 places automatically (stacks first, then empty slots).
        /// With a specific slot: empty → move, same plain item → merge up to max stack,
        /// different item → swap (whole stacks only, and only if both slots accept the other item).
        /// <paramref name="quantity"/> 0 or less means the whole stack. Stateful items always move whole.
        /// </summary>
        public InventoryResult TransferTo(SlotInventory target, int sourceSlot, int targetSlot = -1, int quantity = 0)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!IsValidSlot(sourceSlot)) return InventoryResult.InvalidSlot;
            if (targetSlot != -1 && !target.IsValidSlot(targetSlot)) return InventoryResult.InvalidSlot;

            ItemStack source = _store[sourceSlot];
            if (source.IsEmpty) return InventoryResult.EmptySource;
            if (!target._catalog.Contains(source.ItemId)) return InventoryResult.UnknownItem;

            int amount = quantity <= 0 ? source.Quantity : Math.Min(quantity, source.Quantity);
            if (source.IsStateful && amount != source.Quantity) return InventoryResult.InvalidQuantity;

            ItemStack moving = source.WithQuantity(amount);
            bool sameInventory = ReferenceEquals(this, target);

            if (targetSlot == -1)
                return AutoTransfer(target, sourceSlot, source, moving, sameInventory);

            if (sameInventory && targetSlot == sourceSlot) return InventoryResult.Success;

            ItemStack destination = target._store[targetSlot];

            if (destination.IsEmpty)
            {
                if (!target._filter.CanAccept(targetSlot, moving)) return InventoryResult.NoSpace;

                int moved = Math.Min(amount, target.MaxStack(source.ItemId));
                _store[sourceSlot] = source.WithQuantity(source.Quantity - moved);
                target._store[targetSlot] = source.WithQuantity(moved);
                return moved == amount ? InventoryResult.Success : InventoryResult.Partial;
            }

            if (destination.CanStackWith(source))
            {
                int room = target.MaxStack(source.ItemId) - destination.Quantity;
                if (room <= 0) return InventoryResult.NoSpace;

                int moved = Math.Min(amount, room);
                _store[sourceSlot] = source.WithQuantity(source.Quantity - moved);
                target._store[targetSlot] = destination.WithQuantity(destination.Quantity + moved);
                return moved == amount ? InventoryResult.Success : InventoryResult.Partial;
            }

            // Different items: swap whole stacks only, and only if each slot accepts the other's item.
            if (amount != source.Quantity) return InventoryResult.NoSpace;
            if (!_catalog.Contains(destination.ItemId)) return InventoryResult.UnknownItem;
            if (source.Quantity > target.MaxStack(source.ItemId) || destination.Quantity > MaxStack(destination.ItemId))
                return InventoryResult.NoSpace;
            if (!target._filter.CanAccept(targetSlot, source) || !_filter.CanAccept(sourceSlot, destination))
                return InventoryResult.NoSpace;

            _store[sourceSlot] = destination;
            target._store[targetSlot] = source;
            return InventoryResult.Success;
        }

        /// <summary>Moves every item into <paramref name="target"/>. Returns true if everything fit.</summary>
        public bool TransferAllTo(SlotInventory target)
        {
            bool allMoved = true;
            for (int i = 0; i < _store.Count; i++)
            {
                if (_store[i].IsEmpty) continue;
                if (TransferTo(target, i) != InventoryResult.Success) allMoved = false;
            }
            return allMoved;
        }

        private InventoryResult AutoTransfer(SlotInventory target, int sourceSlot, ItemStack source, ItemStack moving, bool sameInventory)
        {
            int amount = moving.Quantity;

            if (sameInventory)
            {
                // Within one inventory "auto" means: merge into other stacks of the same item.
                // The source slot is freed first and already accepted this item, so everything always fits back.
                _store[sourceSlot] = source.WithQuantity(source.Quantity - amount);
                TryAdd(moving, out _);
                return InventoryResult.Success;
            }

            int space = target.SpaceFor(moving);
            if (space <= 0) return InventoryResult.NoSpace;

            int toMove = Math.Min(amount, space);
            target.TryAdd(moving.WithQuantity(toMove), out int added);
            _store[sourceSlot] = source.WithQuantity(source.Quantity - added);
            return added == amount ? InventoryResult.Success : InventoryResult.Partial;
        }

        private int MaxStack(string itemId) => Math.Max(1, _catalog.GetMaxStack(itemId));
    }
}
