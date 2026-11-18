using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>
    /// Renders one <see cref="ItemContainer"/> and reports clicks, hover and drag starts. Plain C# view owned by
    /// the inventory screens. Two layouts, picked from the bound container:
    /// <list type="bullet">
    /// <item>Plain slots (equipment): one tile per slot, flowing in rows, with optional empty-slot captions.</item>
    /// <item>Grid (stash, bags, crates, bodies): background cells plus one absolutely positioned tile per item,
    /// spanning its footprint (<see cref="GridMetrics"/>).</item>
    /// </list>
    /// The grid element's <c>userData</c> is this view and each item tile's <c>userData</c> is its slot (a grid item's
    /// top-left cell), so a picked element resolves back to a slot (<see cref="TryResolve"/>). Every event reports that
    /// slot, so callers never deal with covered cells.
    /// </summary>
    internal sealed class SlotGridView
    {
        private const string EmptyClass = "slot--empty";
        private const string ActiveClass = "slot--active";
        private const string BrokenClass = "slot--broken";
        private const string DraggingClass = "slot--dragging";
        private const string CategoryClassPrefix = "slot--";
        private const string GridModeClass = "slot-grid--cells";
        private const string CellClass = "grid-cell";
        private const string GridItemClass = "grid-item";
        private const string PlacementClass = "grid-placement";
        private const string PlacementValidClass = "grid-placement--valid";
        private const string PlacementInvalidClass = "grid-placement--invalid";

        private readonly VisualElement _grid;
        private readonly VisualTreeAsset _slotTemplate;
        private readonly ItemDatabase _itemDatabase;
        private readonly Func<int, string> _slotCaption;

        // Plain-slot layout: one tile per slot, in the grid element or in a host placed elsewhere in the layout.
        private readonly List<VisualElement> _slots = new List<VisualElement>();
        private readonly Dictionary<int, VisualElement> _slotHosts = new Dictionary<int, VisualElement>();

        // Grid layout: background cells, pooled item tiles, and the drop preview.
        private readonly List<VisualElement> _cells = new List<VisualElement>();
        private readonly List<VisualElement> _items = new List<VisualElement>();
        private VisualElement _placement;
        private bool _gridMode;

        private ItemContainer _container;
        private int _highlightedSlot = -1;
        private int _dragSourceSlot = -1;

        /// <param name="slotCaption">Optional placeholder text for empty plain slots (equipment slot names).</param>
        public SlotGridView(VisualElement grid, VisualTreeAsset slotTemplate, ItemDatabase itemDatabase,
            Func<int, string> slotCaption = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _grid.userData = this;
            _slotTemplate = slotTemplate;
            _itemDatabase = itemDatabase;
            _slotCaption = slotCaption;
        }

        /// <summary>Left click on an item: (container, slot, single). Single is true when Alt is held.</summary>
        public event Action<ItemContainer, int, bool> SlotClicked;

        /// <summary>Left button pressed on an item: (view, slot, pointer position, pointer id). May become a drag.</summary>
        public event Action<SlotGridView, int, Vector2, int> SlotPressed;

        /// <summary>Right click on an item: (container, slot).</summary>
        public event Action<ItemContainer, int> SlotSecondaryClicked;

        /// <summary>Stack under the pointer (empty when the pointer leaves).</summary>
        public event Action<ItemStack> SlotHovered;

        private int _hoveredSlot = -1;

        public ItemContainer Container => _container;

        /// <summary>
        /// Plain slots: draws <paramref name="slot"/> inside <paramref name="host"/> instead of the grid element, so a
        /// screen can lay equipment slots out anywhere (helmet beside the character, chest rig beside its grid...).
        /// The host resolves back to this view for clicks and drops. Call before binding.
        /// </summary>
        public void SetSlotHost(int slot, VisualElement host)
        {
            if (host == null) return;
            host.userData = this;
            _slotHosts[slot] = host;
            if (slot < _slots.Count)
            {
                _slots[slot].RemoveFromHierarchy();
                host.Add(_slots[slot]);
            }
        }

        /// <summary>True while the bound container is shown as a grid.</summary>
        public bool IsGrid => _gridMode;

        /// <summary>Rules view of the bound container (geometry and placement checks), or null.</summary>
        public SlotInventory Layout => _container != null ? _container.Layout : null;

        /// <summary>
        /// Finds the view and slot under a picked element. Slot is -1 when the element is inside the view but not
        /// on an item tile (or plain slot). False when the element belongs to no view.
        /// </summary>
        public static bool TryResolve(VisualElement picked, out SlotGridView view, out int slot)
        {
            view = null;
            slot = -1;
            for (VisualElement element = picked; element != null; element = element.parent)
            {
                if (slot < 0 && element.userData is int index) slot = index;
                if (element.userData is SlotGridView found)
                {
                    view = found;
                    int count = found._container != null ? found._container.SlotCount : 0;
                    if (slot >= count) slot = -1;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Grid cell under a panel position (pointer coordinates). False outside the grid or in plain-slot layout.
        /// </summary>
        public bool TryCellAt(Vector2 panelPosition, out int x, out int y)
        {
            x = y = -1;
            SlotInventory layout = Layout;
            if (!_gridMode || layout == null || !layout.IsGrid) return false;

            Rect bounds = _grid.worldBound;
            x = Mathf.FloorToInt((panelPosition.x - bounds.x) / GridMetrics.CellPitch);
            y = Mathf.FloorToInt((panelPosition.y - bounds.y) / GridMetrics.CellPitch);
            return x >= 0 && y >= 0 && x < layout.GridWidth && y < layout.GridHeight;
        }

        /// <summary>Shows where a dragged item would land: top-left <paramref name="anchor"/>, footprint, valid or not.</summary>
        public void ShowPlacement(int anchor, ItemSize footprint, bool valid)
        {
            SlotInventory layout = Layout;
            if (!_gridMode || layout == null || !layout.IsGrid || !layout.IsValidSlot(anchor))
            {
                HidePlacement();
                return;
            }

            EnsurePlacementElement();
            int width = layout.GridWidth;
            _placement.style.left = anchor % width * GridMetrics.CellPitch;
            _placement.style.top = anchor / width * GridMetrics.CellPitch;
            _placement.style.width = GridMetrics.Span(footprint.Width);
            _placement.style.height = GridMetrics.Span(footprint.Height);
            _placement.EnableInClassList(PlacementValidClass, valid);
            _placement.EnableInClassList(PlacementInvalidClass, !valid);
            _placement.style.display = DisplayStyle.Flex;
            _placement.BringToFront();
        }

        public void HidePlacement()
        {
            if (_placement != null) _placement.style.display = DisplayStyle.None;
        }

        /// <summary>Marks the slot being dragged. -1 clears.</summary>
        public void SetDragSource(int slot)
        {
            _dragSourceSlot = slot;
            ApplySlotClasses();
        }

        /// <summary>Display name of the item in a slot, for the drag label.</summary>
        public string ItemName(int slot)
        {
            ItemStack stack = GetStack(slot);
            if (stack.IsEmpty) return string.Empty;
            string name = _itemDatabase != null && _itemDatabase.TryGet(stack.ItemId, out ItemDefinition definition) ? definition.DisplayName : stack.ItemId;
            return stack.Quantity > 1 ? $"{name}  x{stack.Quantity}" : name;
        }

        /// <summary>The stack in a slot, or empty.</summary>
        public ItemStack GetStack(int slot) =>
            _container != null && slot >= 0 && slot < _container.SlotCount ? _container.GetSlot(slot) : ItemStack.Empty;

        public void Bind(ItemContainer container)
        {
            if (_container == container) { Refresh(); return; }

            if (_container != null) _container.Changed -= Refresh;
            _container = container;
            _hoveredSlot = -1;
            if (_container != null) _container.Changed += Refresh;

            Refresh();
        }

        /// <summary>Marks one slot (e.g. the drawn weapon). -1 clears.</summary>
        public void SetHighlightedSlot(int slot)
        {
            _highlightedSlot = slot;
            ApplySlotClasses();
        }

        public void Refresh()
        {
            SlotInventory layout = Layout;
            bool grid = _container != null && _container.IsGrid;
            if (grid != _gridMode) SwitchMode(grid);

            if (_gridMode) RefreshGrid(layout);
            else RefreshSlots();

            // The hovered item changed (fitted, unloaded, moved): keep the details line current.
            if (_hoveredSlot >= 0) SlotHovered?.Invoke(GetStack(_hoveredSlot));
        }

        // ---------------- Plain slots ----------------

        private void RefreshSlots()
        {
            int count = _container != null ? _container.SlotCount : 0;
            EnsureSlotElements(count);

            for (int i = 0; i < _slots.Count; i++)
            {
                VisualElement slot = _slots[i];
                bool visible = i < count;
                slot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible) Render(slot, i, _container.GetSlot(i));
            }
            ApplySlotClasses();
        }

        private void EnsureSlotElements(int count)
        {
            while (_slots.Count < count)
            {
                int index = _slots.Count;
                VisualElement slot = CreateTile();
                slot.userData = index;
                (_slotHosts.TryGetValue(index, out VisualElement host) ? host : _grid).Add(slot);
                _slots.Add(slot);
            }
        }

        // ---------------- Grid ----------------

        private void SwitchMode(bool grid)
        {
            // Only remove what this view made: the grid element may hold other content (slot hosts).
            foreach (VisualElement slot in _slots) slot.RemoveFromHierarchy();
            foreach (VisualElement cell in _cells) cell.RemoveFromHierarchy();
            foreach (VisualElement item in _items) item.RemoveFromHierarchy();
            _placement?.RemoveFromHierarchy();
            _slots.Clear();
            _cells.Clear();
            _items.Clear();
            _placement = null;
            _gridMode = grid;
            _grid.EnableInClassList(GridModeClass, grid);
            if (!grid)
            {
                _grid.style.width = StyleKeyword.Null;
                _grid.style.height = StyleKeyword.Null;
            }
        }

        private void RefreshGrid(SlotInventory layout)
        {
            if (layout == null || !layout.IsGrid)
            {
                // Not replicated yet: show nothing rather than a wrong layout.
                SetCellCount(0);
                SetItemCount(0);
                return;
            }

            int width = layout.GridWidth;
            int height = layout.GridHeight;
            _grid.style.width = GridMetrics.Span(width);
            _grid.style.height = GridMetrics.Span(height);

            SetCellCount(width * height);
            for (int i = 0; i < _cells.Count; i++)
            {
                VisualElement cell = _cells[i];
                cell.style.left = i % width * GridMetrics.CellPitch;
                cell.style.top = i / width * GridMetrics.CellPitch;
            }

            int used = 0;
            for (int slot = 0; slot < _container.SlotCount; slot++)
            {
                ItemStack stack = _container.GetSlot(slot);
                if (stack.IsEmpty) continue;

                SetItemCount(Math.Max(_items.Count, used + 1));
                VisualElement tile = _items[used++];
                ItemSize footprint = layout.FootprintOf(stack);
                tile.userData = slot;
                tile.style.left = slot % width * GridMetrics.CellPitch;
                tile.style.top = slot / width * GridMetrics.CellPitch;
                tile.style.width = GridMetrics.Span(footprint.Width);
                tile.style.height = GridMetrics.Span(footprint.Height);
                tile.style.display = DisplayStyle.Flex;
                Render(tile, slot, stack);
            }
            for (int i = used; i < _items.Count; i++)
            {
                _items[i].userData = -1;
                _items[i].style.display = DisplayStyle.None;
            }

            if (_placement != null) _placement.BringToFront();
            ApplySlotClasses();
        }

        private void SetCellCount(int count)
        {
            while (_cells.Count < count)
            {
                var cell = new VisualElement { pickingMode = PickingMode.Position };
                cell.AddToClassList(CellClass);
                cell.style.width = GridMetrics.CellSize;
                cell.style.height = GridMetrics.CellSize;
                _grid.Insert(0, cell); // cells stay behind items
                _cells.Add(cell);
            }
            for (int i = 0; i < _cells.Count; i++)
                _cells[i].style.display = i < count ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetItemCount(int count)
        {
            while (_items.Count < count)
            {
                VisualElement tile = CreateTile();
                tile.AddToClassList(GridItemClass);
                tile.userData = -1;
                _grid.Add(tile);
                _items.Add(tile);
            }
        }

        private void EnsurePlacementElement()
        {
            if (_placement != null) return;
            _placement = new VisualElement { pickingMode = PickingMode.Ignore };
            _placement.AddToClassList(PlacementClass);
            _placement.style.display = DisplayStyle.None;
            _grid.Add(_placement);
        }

        // ---------------- Tiles ----------------

        /// <summary>A tile from the slot template, wired to report its current slot (read from userData on each event).</summary>
        private VisualElement CreateTile()
        {
            VisualElement tile = InstantiateTemplate();
            tile.RegisterCallback<ClickEvent>(evt => OnTileClicked(tile, secondary: false, single: evt.altKey));
            tile.RegisterCallback<PointerDownEvent>(evt =>
            {
                int slot = SlotOf(tile);
                if (evt.button == 0 && IsFilled(slot))
                    SlotPressed?.Invoke(this, slot, new Vector2(evt.position.x, evt.position.y), evt.pointerId);
            });
            tile.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 1) OnTileClicked(tile, secondary: true, single: false);
            });
            tile.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _hoveredSlot = SlotOf(tile);
                SlotHovered?.Invoke(GetStack(_hoveredSlot));
            });
            tile.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _hoveredSlot = -1;
                SlotHovered?.Invoke(ItemStack.Empty);
            });
            return tile;
        }

        private VisualElement InstantiateTemplate()
        {
            if (_slotTemplate != null)
            {
                VisualElement slot = _slotTemplate.Instantiate().Q<VisualElement>("slot");
                if (slot != null) return slot;
                Debug.LogError($"[{nameof(SlotGridView)}] Slot template has no element named 'slot'.");
            }

            // Fallback keeps the UI usable if the template is missing.
            var fallback = new VisualElement { name = "slot" };
            fallback.AddToClassList("slot");
            foreach (string part in new[] { "icon", "caption", "name", "detail", "quantity" })
            {
                VisualElement element = part == "icon" ? new VisualElement() : new Label();
                element.name = part;
                element.pickingMode = PickingMode.Ignore;
                fallback.Add(element);
            }
            return fallback;
        }

        private static int SlotOf(VisualElement tile) => tile.userData is int slot ? slot : -1;

        /// <summary>Applies the dragging and highlight marks to whichever tile shows each slot.</summary>
        private void ApplySlotClasses()
        {
            List<VisualElement> tiles = _gridMode ? _items : _slots;
            int count = _container != null ? _container.SlotCount : 0;
            foreach (VisualElement tile in tiles)
            {
                int slot = SlotOf(tile);
                bool shown = slot >= 0 && slot < count;
                tile.EnableInClassList(DraggingClass, shown && slot == _dragSourceSlot);
                tile.EnableInClassList(ActiveClass, shown && slot == _highlightedSlot);
            }
        }

        private void Render(VisualElement slot, int index, ItemStack stack)
        {
            var caption = slot.Q<Label>("caption");
            var nameLabel = slot.Q<Label>("name");
            var detail = slot.Q<Label>("detail");
            var quantity = slot.Q<Label>("quantity");
            var icon = slot.Q<VisualElement>("icon");

            ClearCategoryClasses(slot);
            RarityStyle.Clear(slot);
            slot.RemoveFromClassList(BrokenClass);
            if (caption != null) caption.text = !_gridMode && _slotCaption != null ? _slotCaption(index) : string.Empty;

            if (stack.IsEmpty)
            {
                slot.AddToClassList(EmptyClass);
                SetText(nameLabel, string.Empty);
                SetText(detail, string.Empty);
                SetText(quantity, string.Empty);
                if (icon != null) icon.style.backgroundImage = StyleKeyword.None;
                return;
            }

            slot.RemoveFromClassList(EmptyClass);

            ItemDefinition definition = null;
            if (_itemDatabase != null) _itemDatabase.TryGet(stack.ItemId, out definition);

            SetText(nameLabel, definition != null ? definition.DisplayName : stack.ItemId);
            SetText(quantity, stack.Quantity > 1 ? stack.Quantity.ToString() : string.Empty);
            SetText(detail, definition != null ? ItemText.SlotDetail(definition, stack, _itemDatabase) : string.Empty);

            if (icon != null)
            {
                icon.style.backgroundImage = definition != null && definition.Icon != null
                    ? new StyleBackground(definition.Icon)
                    : new StyleBackground(StyleKeyword.None);
            }

            if (definition != null)
            {
                slot.AddToClassList(CategoryClassPrefix + definition.Category.ToString().ToLowerInvariant());
                RarityStyle.Apply(slot, definition);
            }
            if (definition is ArmorDefinition && stack.Durability <= 0)
                slot.AddToClassList(BrokenClass);
        }

        private static void SetText(Label label, string text)
        {
            if (label != null) label.text = text;
        }

        private static void ClearCategoryClasses(VisualElement slot)
        {
            foreach (ItemCategory category in (ItemCategory[])Enum.GetValues(typeof(ItemCategory)))
                slot.RemoveFromClassList(CategoryClassPrefix + category.ToString().ToLowerInvariant());
        }

        private bool IsFilled(int index) => !GetStack(index).IsEmpty;

        private void OnTileClicked(VisualElement tile, bool secondary, bool single)
        {
            int index = SlotOf(tile);
            if (!IsFilled(index)) return;

            if (secondary) SlotSecondaryClicked?.Invoke(_container, index);
            else SlotClicked?.Invoke(_container, index, single);
        }
    }
}
