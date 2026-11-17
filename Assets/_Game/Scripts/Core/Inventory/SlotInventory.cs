using System;
using System.Collections.Generic;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>
    /// Inventory rules: stacking, adding, removing, moving and transferring between inventories,
    /// with optional per-slot filters (equipment slot types, pocket size limits).
    /// Two layouts:
    /// <list type="bullet">
    /// <item>Slots (grid width 0): every slot holds one stack regardless of item size (equipment).</item>
    /// <item>Grid (grid width &gt; 0): slots are cells, row by row. A stack lives in its top-left cell and covers
    /// its footprint (the item's size, swapped when <see cref="ItemStack.Rotated"/>); covered cells hold nothing.</item>
    /// </list>
    /// Pure C#, no engine or networking dependencies, so it is unit-testable and runs identically on the
    /// server and offline. Every operation either fully succeeds, partially succeeds (reported), or changes nothing.
    /// </summary>
    public sealed class SlotInventory
    {
        private const int Free = -1;

        private readonly ISlotStore _store;
        private readonly IItemCatalog _catalog;
        private readonly ISlotFilter _filter;
        private readonly int _gridWidth;
        private int[] _owners;

        /// <param name="gridWidth">0 for plain slots; otherwise cells per row (capacity must be a multiple of it).</param>
        public SlotInventory(ISlotStore store, IItemCatalog catalog, ISlotFilter filter = null, int gridWidth = 0)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _filter = filter ?? AcceptAllSlotFilter.Instance;
            if (gridWidth < 0 || (gridWidth > 0 && store.Count % gridWidth != 0))
                throw new ArgumentException($"Capacity {store.Count} is not a whole number of rows of {gridWidth}.", nameof(gridWidth));
            _gridWidth = gridWidth;
        }

        public int Capacity => _store.Count;

        public ItemStack this[int slot] => _store[slot];

        public bool IsGrid => _gridWidth > 0;

        /// <summary>Cells per row; 0 for plain slots.</summary>
        public int GridWidth => _gridWidth;

        /// <summary>Rows; 0 for plain slots.</summary>
        public int GridHeight => IsGrid ? _store.Count / _gridWidth : 0;

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

        // ---------------- Grid geometry ----------------

        /// <summary>Cells the stack covers here: its size (rotated if flagged) in a grid, 1x1 in plain slots.</summary>
        public ItemSize FootprintOf(in ItemStack stack) =>
            IsGrid && !stack.IsEmpty ? _catalog.GetSize(stack.ItemId).Oriented(stack.Rotated) : ItemSize.One;

        /// <summary>The slot holding the stack that covers <paramref name="cell"/>, or -1 when the cell is free.</summary>
        public int AnchorAt(int cell)
        {
            if (!IsValidSlot(cell)) return Free;
            if (!IsGrid) return _store[cell].IsEmpty ? Free : cell;
            return Owners()[cell];
        }

        /// <summary>
        /// True if the stack could be put with its top-left at <paramref name="anchor"/> as it is oriented:
        /// inside the grid, on free cells (cells of <paramref name="ignoreAnchor"/> count as free), and allowed by the filter.
        /// Does not consider merging into an existing stack.
        /// </summary>
        public bool CanPlaceAt(int anchor, in ItemStack stack, int ignoreAnchor = Free) =>
            !stack.IsEmpty && IsValidSlot(anchor) && _filter.CanAccept(anchor, stack) && FitsAt(Owners(), anchor, FootprintOf(stack), ignoreAnchor);

        // ---------------- Queries ----------------

        /// <summary>How many of the given stack's item could be added right now (respecting filters and room).</summary>
        public int SpaceFor(in ItemStack stack)
        {
            if (stack.IsEmpty || !_catalog.Contains(stack.ItemId)) return 0;

            int maxStack = MaxStack(stack.ItemId);
            bool stackable = !stack.IsStateful && maxStack > 1;
            int space = 0;

            if (stackable)
            {
                for (int i = 0; i < _store.Count; i++)
                {
                    ItemStack existing = _store[i];
                    if (existing.CanStackWith(stack) && _filter.CanAccept(i, stack)) space += Math.Max(0, maxStack - existing.Quantity);
                }
            }

            // Simulates the same first-fit placement TryAdd uses, so the two always agree.
            int[] owners = (int[])Owners().Clone();
            int perPlacement = stackable ? maxStack : 1;
            while (TryFindPlacement(owners, stack, out int anchor, out bool rotated))
            {
                Occupy(owners, anchor, FootprintOf(stack.WithRotation(rotated)), anchor);
                space += perPlacement;
            }
            return space;
        }

        public int SpaceFor(string itemId) => SpaceFor(new ItemStack(itemId, 1));

        // ---------------- Changes ----------------

        /// <summary>
        /// Adds up to the stack's quantity. Plain items top up existing stacks first, then take the first free
        /// place (row by row, as given, then rotated). Stateful items (durability, contents) must have quantity 1.
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
                    Write(i, existing.WithQuantity(existing.Quantity + moved));
                    remaining -= moved;
                }
            }

            while (remaining > 0 && TryFindPlacement(Owners(), stack, out int anchor, out bool rotated))
            {
                int moved = Math.Min(remaining, maxStack);
                Write(anchor, stack.WithQuantity(moved).WithRotation(rotated));
                remaining -= moved;
            }

            added = stack.Quantity - remaining;
            return added == stack.Quantity ? InventoryResult.Success
                 : added > 0 ? InventoryResult.Partial
                 : InventoryResult.NoSpace;
        }

        public InventoryResult TryAdd(string itemId, int quantity, out int added) =>
            TryAdd(new ItemStack(itemId, quantity), out added);

        /// <summary>Puts a stack at one specific slot (its top-left cell in a grid) if the room is free and accepts it.</summary>
        public InventoryResult TryPlace(int slot, in ItemStack stack)
        {
            if (!IsValidSlot(slot)) return InventoryResult.InvalidSlot;
            if (stack.IsEmpty || (stack.IsStateful && stack.Quantity != 1)) return InventoryResult.InvalidQuantity;
            if (!_catalog.Contains(stack.ItemId)) return InventoryResult.UnknownItem;
            if (stack.Quantity > MaxStack(stack.ItemId) || !CanPlaceAt(slot, stack)) return InventoryResult.NoSpace;

            Write(slot, stack);
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
                Write(i, stack.WithQuantity(stack.Quantity - taken));
                remaining -= taken;
            }
            return quantity - remaining;
        }

        /// <summary>Removes up to <paramref name="quantity"/> from one slot. Returns what was taken (unrotated).</summary>
        public ItemStack TakeFromSlot(int slot, int quantity)
        {
            if (!IsValidSlot(slot) || quantity <= 0) return ItemStack.Empty;

            ItemStack stack = _store[slot];
            if (stack.IsEmpty) return ItemStack.Empty;

            int taken = Math.Min(quantity, stack.Quantity);
            Write(slot, stack.WithQuantity(stack.Quantity - taken));
            return stack.WithQuantity(taken).WithRotation(false);
        }

        /// <summary>Replaces the durability of the item in a slot (armor wear).</summary>
        public void SetDurability(int slot, int durability)
        {
            if (!IsValidSlot(slot) || _store[slot].IsEmpty) return;
            Write(slot, _store[slot].WithDurability(durability));
        }

        /// <summary>Replaces the loaded ammo of the weapon in a slot.</summary>
        public void SetLoadedAmmo(int slot, string ammoId, int ammoCount)
        {
            if (!IsValidSlot(slot) || _store[slot].IsEmpty) return;
            Write(slot, _store[slot].WithLoadedAmmo(ammoId, ammoCount));
        }

        /// <summary>Replaces the fitted attachments of the weapon in a slot.</summary>
        public void SetAttachments(int slot, string attachments)
        {
            if (!IsValidSlot(slot) || _store[slot].IsEmpty) return;
            Write(slot, _store[slot].WithAttachments(attachments));
        }

        /// <summary>
        /// Re-packs every item in <paramref name="order"/> (first-fit, row by row; partial stacks of the same item
        /// merge). All or nothing: if the sorted items would not all fit, the inventory is left exactly as it was.
        /// </summary>
        public bool Sort(Comparison<ItemStack> order)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));

            var before = new ItemStack[_store.Count];
            var items = new List<ItemStack>();
            for (int i = 0; i < _store.Count; i++)
            {
                before[i] = _store[i];
                if (!_store[i].IsEmpty) items.Add(_store[i].WithRotation(false));
            }
            items.Sort(order);

            Clear();
            foreach (ItemStack stack in items)
            {
                TryAdd(stack, out int added);
                if (added == stack.Quantity) continue;

                for (int i = 0; i < _store.Count; i++)
                    if (_store[i] != before[i]) Write(i, before[i]);
                return false;
            }
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < _store.Count; i++)
                if (!_store[i].IsEmpty) Write(i, ItemStack.Empty);
        }

        /// <summary>
        /// Moves items from a slot in this inventory to <paramref name="target"/> (which may be this inventory).
        /// <list type="bullet">
        /// <item><paramref name="targetSlot"/> -1 places automatically (stacks first, then the first free place).</item>
        /// <item>A specific slot: onto the same plain item (anywhere on it in a grid) → merge up to max stack;
        /// onto free room → move there, top-left at that slot, oriented by <paramref name="rotated"/> in a grid;
        /// onto a different item in plain slots → swap whole stacks if both sides accept and fit the other.</item>
        /// </list>
        /// <paramref name="quantity"/> 0 or less means the whole stack. Stateful items always move whole.
        /// <paramref name="rotated"/> null keeps the item's current orientation.
        /// </summary>
        public InventoryResult TransferTo(SlotInventory target, int sourceSlot, int targetSlot = -1, int quantity = 0, bool? rotated = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!IsValidSlot(sourceSlot)) return InventoryResult.InvalidSlot;
            if (targetSlot != -1 && !target.IsValidSlot(targetSlot)) return InventoryResult.InvalidSlot;

            ItemStack source = _store[sourceSlot];
            if (source.IsEmpty) return InventoryResult.EmptySource;
            if (!target._catalog.Contains(source.ItemId)) return InventoryResult.UnknownItem;

            int amount = quantity <= 0 ? source.Quantity : Math.Min(quantity, source.Quantity);
            if (source.IsStateful && amount != source.Quantity) return InventoryResult.InvalidQuantity;

            bool sameInventory = ReferenceEquals(this, target);
            ItemStack moving = source.WithQuantity(amount).WithRotation(rotated ?? source.Rotated);

            if (targetSlot == -1)
                return AutoTransfer(target, sourceSlot, source, moving, sameInventory);

            int destinationAnchor = target.AnchorAt(targetSlot);

            if (sameInventory && destinationAnchor == sourceSlot)
            {
                // Dropped onto itself: in a grid this may be a re-placement (moved or rotated within its own footprint).
                if (!IsGrid || targetSlot == sourceSlot && moving.Rotated == source.Rotated) return InventoryResult.Success;
                return MoveIntoFreeRoom(target, sourceSlot, source, moving, targetSlot, sameInventory);
            }

            if (destinationAnchor != Free)
            {
                ItemStack destination = target._store[destinationAnchor];
                if (destination.CanStackWith(source))
                {
                    int room = target.MaxStack(source.ItemId) - destination.Quantity;
                    if (room <= 0) return InventoryResult.NoSpace;

                    int moved = Math.Min(amount, room);
                    Write(sourceSlot, source.WithQuantity(source.Quantity - moved));
                    target.Write(destinationAnchor, destination.WithQuantity(destination.Quantity + moved));
                    return moved == amount ? InventoryResult.Success : InventoryResult.Partial;
                }

                // Dropped onto a different item: grids refuse (no reshuffling); plain slots swap.
                if (target.IsGrid) return InventoryResult.NoSpace;
                return Swap(target, sourceSlot, source, destinationAnchor, destination, amount);
            }

            return MoveIntoFreeRoom(target, sourceSlot, source, moving, targetSlot, sameInventory);
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

        // ---------------- Transfer helpers ----------------

        private InventoryResult MoveIntoFreeRoom(SlotInventory target, int sourceSlot, ItemStack source, ItemStack moving,
            int targetSlot, bool sameInventory)
        {
            // The item's own cells count as free only when all of it moves (a split leaves the rest where it is).
            int ignore = sameInventory && moving.Quantity == source.Quantity ? sourceSlot : Free;
            if (!target._filter.CanAccept(targetSlot, moving) || !target.FitsAt(target.Owners(), targetSlot, target.FootprintOf(moving), ignore))
                return InventoryResult.NoSpace;

            int moved = Math.Min(moving.Quantity, target.MaxStack(source.ItemId));
            Write(sourceSlot, source.WithQuantity(source.Quantity - moved));
            target.Write(targetSlot, moving.WithQuantity(moved));
            return moved == moving.Quantity ? InventoryResult.Success : InventoryResult.Partial;
        }

        /// <summary>Plain-slot target: whole stacks trade places if each side accepts (and, in a grid source, fits) the other.</summary>
        private InventoryResult Swap(SlotInventory target, int sourceSlot, ItemStack source, int targetSlot, ItemStack destination, int amount)
        {
            if (amount != source.Quantity) return InventoryResult.NoSpace;
            if (!_catalog.Contains(destination.ItemId)) return InventoryResult.UnknownItem;
            if (source.Quantity > target.MaxStack(source.ItemId) || destination.Quantity > MaxStack(destination.ItemId))
                return InventoryResult.NoSpace;

            ItemStack incoming = destination.WithRotation(false);
            ItemStack outgoing = source.WithRotation(false);
            if (!target._filter.CanAccept(targetSlot, outgoing)) return InventoryResult.NoSpace;

            if (IsGrid)
            {
                // The item coming back takes the leaving item's place: try both orientations there.
                int[] owners = Owners();
                if (!_filter.CanAccept(sourceSlot, incoming)) return InventoryResult.NoSpace;
                if (FitsAt(owners, sourceSlot, FootprintOf(incoming), sourceSlot)) { }
                else if (FitsAt(owners, sourceSlot, FootprintOf(incoming.WithRotation(true)), sourceSlot)) incoming = incoming.WithRotation(true);
                else return InventoryResult.NoSpace;
            }
            else if (!_filter.CanAccept(sourceSlot, incoming)) return InventoryResult.NoSpace;

            Write(sourceSlot, incoming);
            target.Write(targetSlot, outgoing);
            return InventoryResult.Success;
        }

        private InventoryResult AutoTransfer(SlotInventory target, int sourceSlot, ItemStack source, ItemStack moving, bool sameInventory)
        {
            int amount = moving.Quantity;

            if (sameInventory)
            {
                // Within one inventory "auto" means: merge into other stacks of the same item, then the first free place.
                // The source room is freed first and already accepted this item, so everything always fits back.
                Write(sourceSlot, source.WithQuantity(source.Quantity - amount));
                TryAdd(moving.WithRotation(false), out int readded);
                if (readded < amount) Write(sourceSlot, source); // cannot happen; keeps items safe if it ever does
                return InventoryResult.Success;
            }

            ItemStack plain = moving.WithRotation(false);
            int space = target.SpaceFor(plain);
            if (space <= 0) return InventoryResult.NoSpace;

            int toMove = Math.Min(amount, space);
            target.TryAdd(plain.WithQuantity(toMove), out int added);
            Write(sourceSlot, source.WithQuantity(source.Quantity - added));
            return added == amount ? InventoryResult.Success : InventoryResult.Partial;
        }

        // ---------------- Occupancy ----------------

        /// <summary>
        /// For each cell, the slot of the stack covering it, or -1. Plain slots: each filled slot covers itself.
        /// Rebuilt on every call (into a reused buffer): the store may change from outside (replication), and
        /// grids are small. Callers must not hold the result across a write.
        /// </summary>
        private int[] Owners()
        {
            if (_owners == null || _owners.Length != _store.Count) _owners = new int[_store.Count];
            for (int i = 0; i < _owners.Length; i++) _owners[i] = Free;
            for (int i = 0; i < _store.Count; i++)
            {
                ItemStack stack = _store[i];
                if (stack.IsEmpty) continue;
                Occupy(_owners, i, FootprintOf(stack), i);
            }
            return _owners;
        }

        private void Write(int slot, in ItemStack stack) => _store[slot] = stack;

        private void Occupy(int[] owners, int anchor, ItemSize size, int owner)
        {
            if (!IsGrid)
            {
                owners[anchor] = owner;
                return;
            }
            int x0 = anchor % _gridWidth, y0 = anchor / _gridWidth;
            for (int y = y0; y < y0 + size.Height && y < GridHeight; y++)
                for (int x = x0; x < x0 + size.Width && x < _gridWidth; x++)
                    owners[y * _gridWidth + x] = owner;
        }

        private bool FitsAt(int[] owners, int anchor, ItemSize size, int ignoreAnchor)
        {
            if (!IsValidSlot(anchor)) return false;
            if (!IsGrid) return owners[anchor] == Free || owners[anchor] == ignoreAnchor;

            int x0 = anchor % _gridWidth, y0 = anchor / _gridWidth;
            if (x0 + size.Width > _gridWidth || y0 + size.Height > GridHeight) return false;

            for (int y = y0; y < y0 + size.Height; y++)
                for (int x = x0; x < x0 + size.Width; x++)
                {
                    int owner = owners[y * _gridWidth + x];
                    if (owner != Free && owner != ignoreAnchor) return false;
                }
            return true;
        }

        /// <summary>First place, row by row, where the stack fits: as given first, then turned (non-square grid items).</summary>
        private bool TryFindPlacement(int[] owners, in ItemStack stack, out int anchor, out bool rotated)
        {
            ItemSize size = IsGrid ? _catalog.GetSize(stack.ItemId) : ItemSize.One;
            bool preferred = IsGrid && stack.Rotated;
            for (int pass = 0; pass < 2; pass++)
            {
                bool turn = pass == 0 ? preferred : !preferred;
                if (pass == 1 && (!IsGrid || size.IsSquare)) break;

                ItemSize oriented = size.Oriented(turn);
                for (int i = 0; i < _store.Count; i++)
                {
                    if (owners[i] != Free || !_filter.CanAccept(i, stack)) continue;
                    if (!FitsAt(owners, i, oriented, Free)) continue;
                    anchor = i;
                    rotated = turn;
                    return true;
                }
            }
            anchor = Free;
            rotated = false;
            return false;
        }

        private int MaxStack(string itemId) => Math.Max(1, _catalog.GetMaxStack(itemId));
    }
}
