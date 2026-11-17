using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>
    /// Renders one <see cref="ItemContainer"/> as a grid of slot elements and reports clicks, hover and
    /// drag starts. Plain C# view owned by <see cref="InventoryScreen"/>.
    /// The grid element's <c>userData</c> is this view and each slot's <c>userData</c> is its index,
    /// so a picked element can be resolved back to a slot (<see cref="TryResolve"/>).
    /// </summary>
    internal sealed class SlotGridView
    {
        private const string EmptyClass = "slot--empty";
        private const string ActiveClass = "slot--active";
        private const string BrokenClass = "slot--broken";
        private const string DraggingClass = "slot--dragging";
        private const string CategoryClassPrefix = "slot--";

        private readonly VisualElement _grid;
        private readonly VisualTreeAsset _slotTemplate;
        private readonly ItemDatabase _itemDatabase;
        private readonly Func<int, string> _slotCaption;
        private readonly List<VisualElement> _slots = new List<VisualElement>();

        private ItemContainer _container;
        private int _highlightedSlot = -1;

        /// <param name="slotCaption">Optional placeholder text for empty slots (equipment slot names).</param>
        public SlotGridView(VisualElement grid, VisualTreeAsset slotTemplate, ItemDatabase itemDatabase,
            Func<int, string> slotCaption = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _grid.userData = this;
            _slotTemplate = slotTemplate;
            _itemDatabase = itemDatabase;
            _slotCaption = slotCaption;
        }

        /// <summary>Left click on a filled slot: (container, slot, single). Single is true when Alt is held.</summary>
        public event Action<ItemContainer, int, bool> SlotClicked;

        /// <summary>Left button pressed on a filled slot: (view, slot, pointer position, pointer id). May become a drag.</summary>
        public event Action<SlotGridView, int, Vector2, int> SlotPressed;

        /// <summary>Right click on a filled slot: (container, slot).</summary>
        public event Action<ItemContainer, int> SlotSecondaryClicked;

        /// <summary>Stack under the pointer (empty when the pointer leaves).</summary>
        public event Action<ItemStack> SlotHovered;

        public ItemContainer Container => _container;

        /// <summary>
        /// Finds the view and slot under a picked element. Slot is -1 when the element is inside the
        /// view's grid (or panel) but not on a slot. False when the element belongs to no view.
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
                    if (slot >= found._slots.Count) slot = -1;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Marks the slot being dragged. -1 clears.</summary>
        public void SetDragSource(int slot)
        {
            for (int i = 0; i < _slots.Count; i++)
                _slots[i].EnableInClassList(DraggingClass, i == slot);
        }

        /// <summary>Display name of the item in a slot, for the drag ghost.</summary>
        public string ItemName(int slot)
        {
            ItemStack stack = _container != null && slot >= 0 && slot < _container.SlotCount ? _container.GetSlot(slot) : ItemStack.Empty;
            if (stack.IsEmpty) return string.Empty;
            string name = _itemDatabase != null && _itemDatabase.TryGet(stack.ItemId, out ItemDefinition definition) ? definition.DisplayName : stack.ItemId;
            return stack.Quantity > 1 ? $"{name}  x{stack.Quantity}" : name;
        }

        public void Bind(ItemContainer container)
        {
            if (_container == container) { Refresh(); return; }

            if (_container != null) _container.Changed -= Refresh;
            _container = container;
            if (_container != null) _container.Changed += Refresh;

            Refresh();
        }

        /// <summary>Marks one slot (e.g. the drawn weapon). -1 clears.</summary>
        public void SetHighlightedSlot(int slot)
        {
            _highlightedSlot = slot;
            for (int i = 0; i < _slots.Count; i++)
                _slots[i].EnableInClassList(ActiveClass, i == _highlightedSlot);
        }

        public void Refresh()
        {
            int count = _container != null ? _container.SlotCount : 0;
            EnsureSlotElements(count);

            for (int i = 0; i < _slots.Count; i++)
            {
                VisualElement slot = _slots[i];
                bool visible = i < count;
                slot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible) Render(slot, i, _container.GetSlot(i));
                slot.EnableInClassList(ActiveClass, visible && i == _highlightedSlot);
            }
        }

        private void EnsureSlotElements(int count)
        {
            while (_slots.Count < count)
            {
                int index = _slots.Count;
                VisualElement slot = CreateSlotElement();
                slot.userData = index;
                slot.RegisterCallback<ClickEvent>(evt => OnSlotClicked(index, secondary: false, single: evt.altKey));
                slot.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0 && IsFilled(index))
                        SlotPressed?.Invoke(this, index, new Vector2(evt.position.x, evt.position.y), evt.pointerId);
                });
                slot.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (evt.button == 1) OnSlotClicked(index, secondary: true, single: false);
                });
                slot.RegisterCallback<PointerEnterEvent>(_ => SlotHovered?.Invoke(_container != null ? _container.GetSlot(index) : ItemStack.Empty));
                slot.RegisterCallback<PointerLeaveEvent>(_ => SlotHovered?.Invoke(ItemStack.Empty));
                _grid.Add(slot);
                _slots.Add(slot);
            }
        }

        private VisualElement CreateSlotElement()
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

        private void Render(VisualElement slot, int index, ItemStack stack)
        {
            var caption = slot.Q<Label>("caption");
            var nameLabel = slot.Q<Label>("name");
            var detail = slot.Q<Label>("detail");
            var quantity = slot.Q<Label>("quantity");
            var icon = slot.Q<VisualElement>("icon");

            ClearCategoryClasses(slot);
            slot.RemoveFromClassList(BrokenClass);
            if (caption != null) caption.text = _slotCaption != null ? _slotCaption(index) : string.Empty;

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
            SetText(detail, definition != null ? ItemText.SlotDetail(definition, stack) : string.Empty);

            if (icon != null)
            {
                icon.style.backgroundImage = definition != null && definition.Icon != null
                    ? new StyleBackground(definition.Icon)
                    : new StyleBackground(StyleKeyword.None);
            }

            if (definition != null)
                slot.AddToClassList(CategoryClassPrefix + definition.Category.ToString().ToLowerInvariant());
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

        private bool IsFilled(int index) =>
            _container != null && index < _container.SlotCount && !_container.GetSlot(index).IsEmpty;

        private void OnSlotClicked(int index, bool secondary, bool single)
        {
            if (!IsFilled(index)) return;

            if (secondary) SlotSecondaryClicked?.Invoke(_container, index);
            else SlotClicked?.Invoke(_container, index, single);
        }
    }
}
