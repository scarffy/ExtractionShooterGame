using ExtractionShooter.Core.Input;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>
    /// Inventory screen for the local player, everything on one screen with no panel appearing or moving:
    /// equipment | chest rig + backpack | the container being looted (and a bag opened inside it).
    /// Click quick-moves the stack (equip / store / loot side, decided by the server), Alt+click moves one item,
    /// dragging puts an item in a chosen slot (or anywhere in a panel), right-click on a bag in the loot
    /// opens it, right-click on a medical item you carry uses it. Tab toggles; searching a container opens it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class InventoryScreen : MonoBehaviour
    {
        private const string HiddenClass = "hidden";

        [SerializeField] private ItemDatabase _itemDatabase;
        [SerializeField] private VisualTreeAsset _slotTemplate;

        private VisualElement _screen;
        private VisualElement _nestedPanel;
        private Label _rigTitle;
        private Label _backpackTitle;
        private Label _lootTitle;
        private Label _rigEmpty;
        private Label _backpackEmpty;
        private Label _lootEmpty;
        private Label _nestedTitle;
        private Label _details;
        private Button _takeAllButton;
        private Button _closeNestedButton;

        private SlotGridView _equipmentView;
        private SlotGridView _rigView;
        private SlotGridView _backpackView;
        private SlotGridView _lootView;
        private SlotGridView _nestedView;
        private InventoryDragController _drag;

        private PlayerCharacter _player;
        private bool _isOpen;

        public bool IsOpen => _isOpen;

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _screen = root.Q<VisualElement>("inventory-screen");
            if (_screen == null)
            {
                Debug.LogError($"[{nameof(InventoryScreen)}] UXML is missing 'inventory-screen'.", this);
                enabled = false;
                return;
            }

            _nestedPanel = root.Q<VisualElement>("nested-panel");
            _rigTitle = root.Q<Label>("rig-title");
            _backpackTitle = root.Q<Label>("backpack-title");
            _lootTitle = root.Q<Label>("loot-title");
            _nestedTitle = root.Q<Label>("nested-title");
            _rigEmpty = root.Q<Label>("rig-empty");
            _backpackEmpty = root.Q<Label>("backpack-empty");
            _lootEmpty = root.Q<Label>("loot-empty");
            _details = root.Q<Label>("item-details");
            _takeAllButton = root.Q<Button>("take-all-button");
            _closeNestedButton = root.Q<Button>("close-nested-button");

            _drag = new InventoryDragController(_screen);
            _drag.Dropped += OnDropped;

            _equipmentView = CreateView(root, "equipment-grid", "equipment-panel", slot => EquipmentRules.DisplayName((EquipmentSlot)slot));
            _rigView = CreateView(root, "rig-grid", "rig-panel");
            _backpackView = CreateView(root, "backpack-grid", "backpack-panel");
            _lootView = CreateView(root, "loot-grid", "loot-panel");
            _nestedView = CreateView(root, "nested-grid", "nested-panel");
            _lootView.SlotSecondaryClicked += OnLootSecondaryClicked;
            _equipmentView.SlotSecondaryClicked += OnOwnSecondaryClicked;
            _rigView.SlotSecondaryClicked += OnOwnSecondaryClicked;
            _backpackView.SlotSecondaryClicked += OnOwnSecondaryClicked;

            if (_takeAllButton != null) _takeAllButton.clicked += TakeAll;
            if (_closeNestedButton != null) _closeNestedButton.clicked += CloseNested;

            SetVisible(_screen, false);
            CursorController.LockChanged += OnCursorLockChanged;
            PlayerCharacter.LocalPlayerChanged += Bind;
            Bind(PlayerCharacter.Local);
        }

        private void OnDisable()
        {
            CursorController.LockChanged -= OnCursorLockChanged;
            PlayerCharacter.LocalPlayerChanged -= Bind;
            if (_takeAllButton != null) _takeAllButton.clicked -= TakeAll;
            if (_closeNestedButton != null) _closeNestedButton.clicked -= CloseNested;
            Bind(null);
        }

        private void Update()
        {
            if (_player != null && _player.Input.ToggleInventoryPressed)
            {
                if (_isOpen) Close();
                else Open();
            }
        }

        public void Open()
        {
            if (_isOpen || _player == null || _player.Inventory == null || !_player.Health.IsAlive) return;

            _isOpen = true;
            SetVisible(_screen, true);
            ShowDetails(ItemStack.Empty);
            RefreshAll();
            CursorController.SetLocked(false);
        }

        public void Close()
        {
            if (!_isOpen) return;

            _isOpen = false; // set first: locking the cursor below raises LockChanged
            _drag?.Cancel();
            SetVisible(_screen, false);

            if (_player != null && _player.Inventory != null && _player.Inventory.OpenContainer != null)
                _player.Inventory.RequestClose();

            if (_player != null && _player.Health.IsAlive)
                CursorController.SetLocked(true);
        }

        // ---------------- Binding ----------------

        private SlotGridView CreateView(VisualElement root, string gridName, string panelName, System.Func<int, string> caption = null)
        {
            VisualElement grid = root.Q<VisualElement>(gridName) ?? new VisualElement();
            var view = new SlotGridView(grid, _slotTemplate, _itemDatabase, caption);
            view.SlotClicked += OnSlotClicked;
            view.SlotHovered += ShowDetails;
            view.SlotPressed += _drag.Press;

            // Dropping anywhere on the panel (not only on a slot) places the item automatically.
            VisualElement panel = root.Q<VisualElement>(panelName);
            if (panel != null) panel.userData = view;
            return view;
        }

        private void Bind(PlayerCharacter player)
        {
            if (_player != null)
            {
                if (_player.Inventory != null) _player.Inventory.OpenContainersChanged -= OnOpenContainersChanged;
                if (_player.Equipment != null) _player.Equipment.Changed -= OnEquipmentChanged;
                _player.Health.Changed -= OnHealthChanged;
            }

            Close();
            _player = player;

            if (_player != null)
            {
                if (_player.Inventory != null) _player.Inventory.OpenContainersChanged += OnOpenContainersChanged;
                if (_player.Equipment != null) _player.Equipment.Changed += OnEquipmentChanged;
                _player.Health.Changed += OnHealthChanged;
            }

            RefreshAll();
        }

        private void RefreshAll()
        {
            if (_equipmentView == null) return;
            OnEquipmentChanged();
            RefreshLootPanels();
        }

        /// <summary>Equipment changed: rebind rig/backpack storage (a different bag may be worn now).</summary>
        private void OnEquipmentChanged()
        {
            PlayerEquipment equipment = _player != null ? _player.Equipment : null;

            _equipmentView.Bind(equipment != null ? equipment.Container : null);
            _equipmentView.SetHighlightedSlot(equipment != null && equipment.ActiveWeaponSlot is EquipmentSlot active ? (int)active : -1);

            BindStoragePanel(_rigView, _rigTitle, _rigEmpty, "Chest rig", equipment != null ? equipment.ChestRigStorage : null);
            BindStoragePanel(_backpackView, _backpackTitle, _backpackEmpty, "Backpack", equipment != null ? equipment.BackpackStorage : null);
        }

        private void OnOpenContainersChanged()
        {
            ItemContainer opened = _player != null && _player.Inventory != null ? _player.Inventory.OpenContainer : null;
            if (opened != null && !_isOpen) Open();
            else RefreshLootPanels();
        }

        private void RefreshLootPanels()
        {
            PlayerInventory inventory = _player != null ? _player.Inventory : null;
            ItemContainer looted = inventory != null ? inventory.OpenContainer : null;
            _lootView.Bind(looted);
            SetText(_lootTitle, looted != null ? looted.DisplayName : "Loot");
            SetVisible(_lootEmpty, looted == null);
            SetVisible(_takeAllButton, looted != null);

            // The opened bag is the only panel that comes and goes: it exists only while a bag is open.
            ItemContainer nested = inventory != null ? inventory.OpenNestedContainer : null;
            _nestedView.Bind(nested);
            SetVisible(_nestedPanel, nested != null);
            if (nested != null) SetText(_nestedTitle, nested.DisplayName);
        }

        /// <summary>Rig and backpack panels always show; without the bag they say so instead of hiding.</summary>
        private static void BindStoragePanel(SlotGridView view, Label title, Label emptyLabel, string fallbackTitle, ItemContainer storage)
        {
            view.Bind(storage);
            SetText(title, storage != null ? $"{storage.DisplayName}  ·  {storage.SlotCount}" : fallbackTitle);
            SetVisible(emptyLabel, storage == null);
        }

        // ---------------- Actions ----------------

        private void OnSlotClicked(ItemContainer source, int slot, bool single)
        {
            if (_drag.IsClickFromDrop || _player == null || _player.Inventory == null) return;

            // The click's own modifier, or the key state from the input backend (some platforms only report one).
            bool moveOne = single || _player.Input.SingleItemModifierHeld;
            _player.Inventory.RequestQuickMove(source, slot, moveOne ? 1 : 0);
        }

        private void OnDropped(ItemContainer source, int sourceSlot, ItemContainer target, int targetSlot)
        {
            if (_player != null && _player.Inventory != null)
                _player.Inventory.RequestTransfer(source, sourceSlot, target, targetSlot);
        }

        private void OnLootSecondaryClicked(ItemContainer source, int slot)
        {
            if (_player != null && _player.Inventory != null && source.GetSlot(slot).HasContents)
                _player.Inventory.RequestOpenNested(source, slot);
        }

        /// <summary>Right click on the player's own item: use it if it is a consumable.</summary>
        private void OnOwnSecondaryClicked(ItemContainer source, int slot)
        {
            if (_player == null || _player.Inventory == null || _itemDatabase == null) return;
            if (_itemDatabase.TryGet(source.GetSlot(slot).ItemId, out ItemDefinition item) && item is MedicalDefinition)
                _player.Inventory.RequestUse(source, slot);
        }

        private void CloseNested()
        {
            if (_player != null && _player.Inventory != null)
                _player.Inventory.RequestCloseNested();
        }

        /// <summary>Quick-moves everything from the bag being looted, or else from the container.</summary>
        private void TakeAll()
        {
            if (_player == null || _player.Inventory == null) return;

            // Explicit checks, not '??': Unity objects must be null-checked with their overloaded ==.
            ItemContainer source = _player.Inventory.OpenNestedContainer;
            if (source == null) source = _player.Inventory.OpenContainer;
            if (source == null) return;

            for (int i = 0; i < source.SlotCount; i++)
            {
                if (!source.GetSlot(i).IsEmpty)
                    _player.Inventory.RequestQuickMove(source, i);
            }
        }

        private void ShowDetails(ItemStack stack)
        {
            if (_details == null) return;

            if (stack.IsEmpty || _itemDatabase == null || !_itemDatabase.TryGet(stack.ItemId, out ItemDefinition item))
            {
                _details.text = string.Empty;
                return;
            }

            _details.text = ItemText.Details(item, stack, _itemDatabase);
        }

        private void OnHealthChanged(int previous, int current)
        {
            if (current <= 0) Close();
        }

        private void OnCursorLockChanged(bool locked)
        {
            // Esc (or anything else) locking the cursor closes the screen.
            if (locked && _isOpen) Close();
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null) element.EnableInClassList(HiddenClass, !visible);
        }

        private static void SetText(Label label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
