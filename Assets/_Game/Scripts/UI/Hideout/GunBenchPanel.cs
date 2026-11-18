using System;
using System.Collections.Generic;
using System.Text;
using ExtractionShooter.Core.Hideout;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Session;
using ExtractionShooter.Gameplay.Weapons;
using ExtractionShooter.UI.Inventory;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hideout
{
    /// <summary>
    /// The Workbench tab's gun bench. Pick a weapon (a loadout weapon button, or click a weapon in the stash) to see
    /// its stats bare and as built, take attachments off, and fit attachments from the stash (click one in the stash,
    /// or its button under the slot). Save the build as a named preset; "Buy" builds a preset into the stash from
    /// owned parts plus whatever the trader sells. Modding reuses the session's transfer and detach requests.
    /// Plain C#: owned by <see cref="WorkbenchPanel"/>.
    /// </summary>
    public sealed class GunBenchPanel : IDisposable
    {
        private const string HiddenClass = "hidden";
        private const string PickActiveClass = "trader-filter--active";
        private const string BetterClass = "gunbench-stat--better";
        private const string WorseClass = "gunbench-stat--worse";

        private static readonly AttachmentSlot[] Slots = { AttachmentSlot.Optic, AttachmentSlot.Muzzle, AttachmentSlot.Grip, AttachmentSlot.Magazine };

        private readonly Action _beforeAction;
        private readonly ItemDatabase _itemDatabase;
        private readonly ScrollView _weaponView;
        private readonly ScrollView _presetList;
        private readonly Label _presetsTitle;
        private readonly TextField _presetName;
        private readonly Button _saveButton;

        private PlayerSession _session;
        private PlayerHideout _hideout;
        private readonly List<ItemContainer> _watched = new List<ItemContainer>();
        private ItemContainer _selectedContainer;
        private int _selectedSlot = -1;
        private string _selectedWeaponId;
        private bool _visible;

        /// <summary>Hovered item details, or empty when the pointer leaves.</summary>
        public event Action<string> DetailsChanged;

        public GunBenchPanel(VisualElement root, ItemDatabase itemDatabase, Action beforeAction)
        {
            _beforeAction = beforeAction;
            _itemDatabase = itemDatabase;
            _weaponView = root.Q<ScrollView>("gunbench-weapon");
            _presetList = root.Q<ScrollView>("gunbench-presets");
            _presetsTitle = root.Q<Label>("gunbench-presets-title");
            _presetName = root.Q<TextField>("gunbench-preset-name");
            _saveButton = root.Q<Button>("gunbench-save-button");
            if (_saveButton != null) _saveButton.clicked += SavePreset;
        }

        public void Bind(PlayerSession session)
        {
            if (_hideout != null) _hideout.Changed -= Refresh;
            if (_session != null && _session.Trading != null) _session.Trading.MoneyChanged -= Refresh;
            Unwatch();

            _session = session;
            _hideout = session != null ? session.Hideout : null;
            ClearSelection();

            if (_hideout != null) _hideout.Changed += Refresh;
            if (_session != null && _session.Trading != null) _session.Trading.MoneyChanged += Refresh;
            if (_session != null)
            {
                Watch(_session.Stash);
                Watch(_session.Loadout);
            }
            Refresh();
        }

        public void SetVisible(bool visible)
        {
            bool wasVisible = _visible;
            _visible = visible;
            if (visible && !wasVisible) Refresh();
        }

        public void Refresh()
        {
            if (!_visible) return;
            ValidateSelection();
            RebuildWeapon();
            RebuildPresets();
        }

        /// <summary>A click on an item in the stash: a weapon is selected, an attachment is fitted to the selected weapon.</summary>
        public bool HandleClick(ItemContainer source, int slot)
        {
            if (_session == null || source == null || _itemDatabase == null) return false;
            ItemStack stack = source.GetSlot(slot);
            if (stack.IsEmpty || !_itemDatabase.TryGet(stack.ItemId, out ItemDefinition item)) return false;

            if (item is WeaponDefinition && (source == _session.Stash || source == _session.Loadout))
            {
                _beforeAction?.Invoke();
                Select(source, slot);
                return true;
            }
            if (item is AttachmentDefinition && HasSelection)
            {
                Fit(source, slot);
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            Bind(null);
            if (_saveButton != null) _saveButton.clicked -= SavePreset;
        }

        // ---------------- Selection ----------------

        private bool HasSelection => _selectedContainer != null && _selectedSlot >= 0;

        private void Select(ItemContainer container, int slot)
        {
            _selectedContainer = container;
            _selectedSlot = slot;
            _selectedWeaponId = container.GetSlot(slot).ItemId;
            Refresh();
        }

        private void ClearSelection()
        {
            _selectedContainer = null;
            _selectedSlot = -1;
            _selectedWeaponId = null;
        }

        /// <summary>The selected weapon moved or left: forget it.</summary>
        private void ValidateSelection()
        {
            if (!HasSelection) return;
            bool stillThere = _session != null && (_selectedContainer == _session.Stash || _selectedContainer == _session.Loadout) &&
                              _selectedSlot < _selectedContainer.SlotCount && _selectedContainer.GetSlot(_selectedSlot).ItemId == _selectedWeaponId;
            if (!stillThere) ClearSelection();
        }

        private bool TryGetSelected(out ItemStack stack, out WeaponDefinition weapon)
        {
            stack = default;
            weapon = null;
            if (!HasSelection || _itemDatabase == null) return false;
            stack = _selectedContainer.GetSlot(_selectedSlot);
            return _itemDatabase.TryGet(stack.ItemId, out ItemDefinition item) && (weapon = item as WeaponDefinition) != null;
        }

        // ---------------- Weapon ----------------

        private void RebuildWeapon()
        {
            if (_weaponView == null) return;
            _weaponView.Clear();
            if (_session == null) return;

            _weaponView.Add(CreateLoadoutPicker());
            SetEnabled(_saveButton, HasSelection);

            if (!TryGetSelected(out ItemStack stack, out WeaponDefinition weapon))
            {
                _weaponView.Add(Note("Pick a loadout weapon above, or click a weapon in the stash."));
                return;
            }

            string where = _selectedContainer == _session.Loadout ? EquipmentRules.DisplayName((EquipmentSlot)_selectedSlot) : "Stash";
            var title = new Label($"{weapon.DisplayName}   ·   {where}") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("gunbench-title");
            _weaponView.Add(title);
            _weaponView.Add(CreateStats(weapon, stack));

            var slots = new VisualElement();
            slots.AddToClassList("gunbench-slots");
            List<(int slot, AttachmentDefinition mod)> spare = SpareAttachments(weapon);
            foreach (AttachmentSlot slot in Slots) slots.Add(CreateSlotRow(slot, stack, spare));
            _weaponView.Add(slots);
            _weaponView.Add(Note("Click an attachment in the stash to fit it. A replaced part goes back to the stash."));
        }

        private VisualElement CreateLoadoutPicker()
        {
            var row = new VisualElement();
            row.AddToClassList("gunbench-picker");
            row.Add(Note("Loadout"));
            foreach (EquipmentSlot slot in EquipmentRules.WeaponSlots)
            {
                ItemStack stack = _session.Loadout != null ? _session.Loadout.GetSlot((int)slot) : default;
                string label = !stack.IsEmpty && _itemDatabase != null && _itemDatabase.TryGet(stack.ItemId, out ItemDefinition item)
                    ? item.DisplayName
                    : $"{EquipmentRules.DisplayName(slot)}: empty";
                int index = (int)slot;
                var button = new Button(() => Select(_session.Loadout, index)) { text = label };
                button.AddToClassList("trader-filter");
                button.SetEnabled(!stack.IsEmpty);
                button.EnableInClassList(PickActiveClass, _selectedContainer == _session.Loadout && _selectedSlot == index);
                row.Add(button);
            }
            return row;
        }

        private VisualElement CreateStats(WeaponDefinition weapon, in ItemStack stack)
        {
            WeaponStats bare = WeaponStats.Of(weapon, (IEnumerable<AttachmentDefinition>)null);
            WeaponStats built = WeaponStats.Of(weapon, stack, _itemDatabase);

            var table = new VisualElement();
            table.AddToClassList("gunbench-stats");
            table.Add(StatRow("Magazine", bare.MagazineSize, built.MagazineSize, v => $"{v:0}", higherIsBetter: true));
            table.Add(StatRow("Spread", bare.Spread, built.Spread, v => $"{v:0.00}°", higherIsBetter: false));
            table.Add(StatRow("Hip fire", bare.HipSpreadMultiplier, built.HipSpreadMultiplier, v => $"x{v:0.00}", higherIsBetter: false));
            table.Add(StatRow("Aim time", bare.AimSeconds, built.AimSeconds, v => $"{v:0.00} s", higherIsBetter: false));
            table.Add(StatRow("Recoil", bare.RecoilMultiplier, built.RecoilMultiplier, v => $"{v * 100f:0}%", higherIsBetter: false));
            table.Add(StatRow("Loudness", bare.SoundRangeMultiplier, built.SoundRangeMultiplier, v => $"{v * 100f:0}%", higherIsBetter: false));
            table.Add(StatRow("Aim zoom", bare.AimFieldOfView, built.AimFieldOfView, v => $"{v:0}° FOV", higherIsBetter: null));
            return table;
        }

        /// <param name="higherIsBetter">Null when neither direction is better (colours nothing).</param>
        private static VisualElement StatRow(string name, float bare, float built, Func<float, string> format, bool? higherIsBetter)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("gunbench-stat");
            row.Add(Cell(name, "gunbench-stat__name"));
            row.Add(Cell(format(bare), "gunbench-stat__bare"));
            Label now = Cell(format(built), "gunbench-stat__now");
            if (higherIsBetter.HasValue && Math.Abs(built - bare) > 1e-4f)
            {
                bool better = higherIsBetter.Value ? built > bare : built < bare;
                now.AddToClassList(better ? BetterClass : WorseClass);
            }
            row.Add(now);
            return row;
        }

        private VisualElement CreateSlotRow(AttachmentSlot slot, in ItemStack weapon, List<(int slot, AttachmentDefinition mod)> spare)
        {
            var row = new VisualElement();
            row.AddToClassList("gunbench-slot");

            var header = new VisualElement();
            header.AddToClassList("gunbench-slot__header");
            header.Add(Cell(ItemText.SlotName(slot), "gunbench-slot__name"));

            string fitted = WeaponMods.Get(weapon.Attachments, slot);
            bool hasFitted = fitted != null;
            string fittedName = hasFitted && _itemDatabase.TryGet(fitted, out ItemDefinition item) ? item.DisplayName : fitted;
            Label fittedLabel = Cell(hasFitted ? fittedName : "Empty", "gunbench-slot__fitted");
            fittedLabel.EnableInClassList("gunbench-slot__fitted--empty", !hasFitted);
            header.Add(fittedLabel);

            if (hasFitted)
            {
                AttachmentSlot target = slot;
                var remove = new Button(() => Detach(target)) { text = "Remove" };
                remove.AddToClassList("panel-button");
                remove.AddToClassList("panel-button--small");
                remove.AddToClassList("gunbench-slot__remove");
                header.Add(remove);
            }
            row.Add(header);

            var options = new VisualElement();
            options.AddToClassList("workbench-chips");
            foreach ((int stashSlot, AttachmentDefinition mod) in spare)
            {
                if (mod.Slot != slot) continue;
                int from = stashSlot;
                var fit = new Button(() => Fit(_session.Stash, from)) { text = $"+ {mod.DisplayName}" };
                fit.AddToClassList("trader-filter");
                fit.AddToClassList("gunbench-fit");
                AttachmentDefinition hovered = mod;
                fit.RegisterCallback<PointerEnterEvent>(_ =>
                    DetailsChanged?.Invoke(ItemText.Details(hovered, new ItemStack(hovered.Id, 1), _itemDatabase)));
                fit.RegisterCallback<PointerLeaveEvent>(_ => DetailsChanged?.Invoke(string.Empty));
                options.Add(fit);
            }
            if (options.childCount > 0) row.Add(options);
            return row;
        }

        /// <summary>Loose attachments in the stash that fit the weapon, one entry per distinct item.</summary>
        private List<(int slot, AttachmentDefinition mod)> SpareAttachments(WeaponDefinition weapon)
        {
            var spare = new List<(int, AttachmentDefinition)>();
            ItemContainer stash = _session.Stash;
            if (stash == null) return spare;

            var seen = new HashSet<string>();
            for (int slot = 0; slot < stash.SlotCount; slot++)
            {
                ItemStack stack = stash.GetSlot(slot);
                if (stack.IsEmpty || stack.IsStateful || seen.Contains(stack.ItemId)) continue;
                if (!_itemDatabase.TryGet(stack.ItemId, out ItemDefinition item) || !(item is AttachmentDefinition mod) || !mod.Fits(weapon)) continue;
                seen.Add(stack.ItemId);
                spare.Add((slot, mod));
            }
            return spare;
        }

        private void Fit(ItemContainer source, int slot)
        {
            if (!HasSelection || _session == null) return;
            _beforeAction?.Invoke();
            _session.RequestTransfer(source, slot, _selectedContainer, _selectedSlot);
        }

        private void Detach(AttachmentSlot slot)
        {
            if (!HasSelection || _session == null) return;
            _beforeAction?.Invoke();
            _session.RequestDetach(_selectedContainer, _selectedSlot, slot);
        }

        // ---------------- Presets ----------------

        private void RebuildPresets()
        {
            if (_presetList == null) return;
            _presetList.Clear();
            if (_hideout == null) return;

            IReadOnlyList<WeaponPreset> presets = _hideout.Presets;
            SetText(_presetsTitle, $"Presets  {presets.Count}/{_hideout.MaxPresets}");
            if (presets.Count == 0)
            {
                _presetList.Add(Note("No presets yet. Pick a weapon, fit it out, name it and press Save preset."));
                return;
            }

            long credits = _session.Trading != null ? _session.Trading.Money : 0;
            for (int i = 0; i < presets.Count; i++) _presetList.Add(CreatePresetRow(presets[i], i, credits));
        }

        private VisualElement CreatePresetRow(WeaponPreset preset, int index, long credits)
        {
            var row = new VisualElement();
            row.AddToClassList("gunbench-preset");

            var info = new VisualElement();
            info.AddToClassList("gunbench-preset__info");
            var name = new Label(preset.Name);
            name.AddToClassList("gunbench-preset__name");
            // Clicking the name puts it in the name field, so saving again overwrites this preset.
            name.RegisterCallback<ClickEvent>(_ =>
            {
                if (_presetName != null) _presetName.value = preset.Name;
            });
            info.Add(name);
            info.Add(Cell(Summary(preset), "gunbench-preset__parts"));
            row.Add(info);

            PresetPlan plan = _hideout.PlanPreset(index);
            var buy = new Button(() => Request(() => _hideout.RequestBuyPreset(index)));
            buy.AddToClassList("panel-button");
            buy.AddToClassList("gunbench-preset__buy");
            if (plan == null || !plan.CanBuy)
            {
                buy.text = "Not sold";
                buy.SetEnabled(false);
            }
            else
            {
                buy.text = plan.Cost > 0 ? $"Buy  {PlayerTrading.FormatCredits(plan.Cost)}" : "Build";
                buy.SetEnabled(plan.Cost <= credits);
            }
            buy.RegisterCallback<PointerEnterEvent>(_ => DetailsChanged?.Invoke(PlanDetails(plan)));
            buy.RegisterCallback<PointerLeaveEvent>(_ => DetailsChanged?.Invoke(string.Empty));
            row.Add(buy);

            var delete = new Button(() => Request(() => _hideout.RequestDeletePreset(index))) { text = "Delete" };
            delete.AddToClassList("panel-button");
            delete.AddToClassList("squad-button--neutral");
            delete.AddToClassList("gunbench-preset__delete");
            row.Add(delete);
            return row;
        }

        private string Summary(in WeaponPreset preset)
        {
            var text = new StringBuilder(NameOf(preset.WeaponId));
            bool first = true;
            foreach (KeyValuePair<AttachmentSlot, string> fitted in WeaponMods.All(preset.Attachments))
            {
                text.Append(first ? "   ·   " : ", ").Append(NameOf(fitted.Value));
                first = false;
            }
            if (first) text.Append("   ·   no attachments");
            return text.ToString();
        }

        private string PlanDetails(PresetPlan plan)
        {
            if (plan == null) return string.Empty;
            if (!plan.CanBuy) return $"The trader doesn't sell {NameOf(plan.Unavailable)}.";

            var owned = new List<string>();
            if (plan.OwnedWeaponSlot >= 0) owned.Add("the weapon");
            if (plan.OwnedAttachmentSlots.Count > 0) owned.Add($"{plan.OwnedAttachmentSlots.Count} attachment(s)");
            var bought = new List<string>();
            foreach (string id in plan.ToBuy) bought.Add(NameOf(id));

            string uses = owned.Count > 0 ? $"Uses from your stash: {string.Join(" and ", owned)}" : "Nothing owned is used";
            string buys = bought.Count > 0 ? $"Buys: {string.Join(", ", bought)} ({PlayerTrading.FormatCredits(plan.Cost)})" : "Nothing to buy";
            return $"{uses}   ·   {buys}   ·   The built weapon goes to the stash";
        }

        private void SavePreset()
        {
            if (!HasSelection || _hideout == null) return;
            string presetName = _presetName != null ? _presetName.value : string.Empty;
            Request(() => _hideout.RequestSavePreset(_selectedContainer, _selectedSlot, presetName));
        }

        private void Request(Action request)
        {
            _beforeAction?.Invoke();
            request();
        }

        // ---------------- Helpers ----------------

        private string NameOf(string itemId) =>
            _itemDatabase != null && _itemDatabase.TryGet(itemId, out ItemDefinition item) ? item.DisplayName : itemId ?? "?";

        private void Watch(ItemContainer container)
        {
            if (container == null) return;
            container.Changed += Refresh;
            _watched.Add(container);
        }

        private void Unwatch()
        {
            foreach (ItemContainer container in _watched)
                if (container != null) container.Changed -= Refresh;
            _watched.Clear();
        }

        private static Label Note(string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("gunbench-note");
            return label;
        }

        private static Label Cell(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }

        private static void SetEnabled(VisualElement element, bool enabled) => element?.SetEnabled(enabled);

        private static void SetText(Label label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
