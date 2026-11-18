using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Session;
using ExtractionShooter.Gameplay.Trading;
using ExtractionShooter.UI.Inventory;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hideout
{
    /// <summary>
    /// The trader's offers as a grid of cells (item, pack size, price), with category filters.
    /// Clicking an offer buys one pack. Offers the player cannot afford are dimmed.
    /// Plain C#: owned by <see cref="StashScreen"/>, which binds the local player's <see cref="PlayerTrading"/>.
    /// </summary>
    public sealed class TraderPanel
    {
        private const string OfferClass = "slot--offer";
        private const string UnaffordableClass = "slot--unaffordable";
        private const string CategoryClassPrefix = "slot--";
        private const string FilterActiveClass = "trader-filter--active";

        /// <summary>Offer categories shown as filter buttons.</summary>
        private enum Filter { All, Weapons, Ammo, Gear, Medical }

        private readonly VisualElement _grid;
        private readonly VisualElement _filterBar;
        private readonly Label _title;
        private readonly VisualTreeAsset _slotTemplate;
        private readonly ItemDatabase _itemDatabase;
        private readonly List<VisualElement> _cells = new List<VisualElement>();
        private readonly Dictionary<Filter, Button> _filterButtons = new Dictionary<Filter, Button>();

        private PlayerTrading _trading;
        private Filter _filter = Filter.All;

        /// <summary>Hovered offer's details text, or empty when the pointer leaves.</summary>
        public event Action<string> DetailsChanged;

        public TraderPanel(VisualElement grid, VisualElement filterBar, Label title, VisualTreeAsset slotTemplate, ItemDatabase itemDatabase)
        {
            _grid = grid ?? new VisualElement();
            _filterBar = filterBar;
            _title = title;
            _slotTemplate = slotTemplate;
            _itemDatabase = itemDatabase;
            BuildFilters();
        }

        public void Bind(PlayerTrading trading)
        {
            if (_trading == trading)
            {
                Refresh();
                return;
            }
            _trading = trading;
            RebuildCells();
        }

        /// <summary>Re-applies affordability (call when credits change).</summary>
        public void Refresh()
        {
            TraderDefinition trader = _trading != null ? _trading.Trader : null;
            for (int i = 0; i < _cells.Count; i++)
            {
                VisualElement cell = _cells[i];
                TraderOffer offer = null;
                bool known = trader != null && trader.TryGetOffer(i, out offer);
                cell.style.display = known && Matches(offer.Item, _filter) ? DisplayStyle.Flex : DisplayStyle.None;
                if (known) cell.EnableInClassList(UnaffordableClass, trader.BuyPrice(offer) > _trading.Money);
            }
        }

        // ---------------- Cells ----------------

        private void RebuildCells()
        {
            _grid.Clear();
            _cells.Clear();

            TraderDefinition trader = _trading != null ? _trading.Trader : null;
            if (_title != null) _title.text = trader != null ? trader.DisplayName : "Trader";
            if (trader == null) return;

            for (int i = 0; i < trader.Offers.Count; i++)
            {
                VisualElement cell = CreateCell();
                int index = i;
                cell.RegisterCallback<ClickEvent>(_ => _trading?.RequestBuy(index));
                cell.RegisterCallback<PointerEnterEvent>(_ => DetailsChanged?.Invoke(OfferDetails(index)));
                cell.RegisterCallback<PointerLeaveEvent>(_ => DetailsChanged?.Invoke(string.Empty));
                Render(cell, trader, trader.Offers[i]);
                _grid.Add(cell);
                _cells.Add(cell);
            }
            Refresh();
        }

        private VisualElement CreateCell()
        {
            VisualElement cell = _slotTemplate != null ? _slotTemplate.Instantiate().Q<VisualElement>("slot") : null;
            if (cell == null)
            {
                cell = new VisualElement { name = "slot" };
                cell.AddToClassList("slot");
                foreach (string part in new[] { "name", "detail", "quantity" })
                    cell.Add(new Label { name = part, pickingMode = PickingMode.Ignore });
            }
            cell.RemoveFromClassList("slot--empty");
            cell.AddToClassList(OfferClass);
            return cell;
        }

        private static void Render(VisualElement cell, TraderDefinition trader, TraderOffer offer)
        {
            ItemDefinition item = offer.Item;
            SetText(cell.Q<Label>("name"), item != null ? item.DisplayName : "?");
            SetText(cell.Q<Label>("detail"), PlayerTrading.FormatCredits(trader.BuyPrice(offer)));
            SetText(cell.Q<Label>("quantity"), offer.Quantity > 1 ? $"x{offer.Quantity}" : string.Empty);

            VisualElement icon = cell.Q<VisualElement>("icon");
            if (icon != null)
                icon.style.backgroundImage = item != null && item.Icon != null ? new StyleBackground(item.Icon) : new StyleBackground(StyleKeyword.None);
            if (item != null) cell.AddToClassList(CategoryClassPrefix + item.Category.ToString().ToLowerInvariant());
            RarityStyle.Apply(cell, item);
        }

        private string OfferDetails(int index)
        {
            TraderDefinition trader = _trading != null ? _trading.Trader : null;
            if (trader == null || !trader.TryGetOffer(index, out TraderOffer offer)) return string.Empty;

            ItemDefinition item = offer.Item;
            // What arrives in the stash: new armor at full durability, keys with every use, weapons unloaded.
            int durability = item is ArmorDefinition armor ? armor.MaxDurability : item is KeyDefinition key ? key.MaxUses : 0;
            string stats = ItemText.Details(item, new ItemStack(item.Id, offer.Quantity, durability), _itemDatabase);
            return $"{stats}   ·   Buy {PlayerTrading.FormatCredits(trader.BuyPrice(offer))} (click)";
        }

        // ---------------- Filters ----------------

        private void BuildFilters()
        {
            if (_filterBar == null) return;
            _filterBar.Clear();
            foreach (Filter filter in (Filter[])Enum.GetValues(typeof(Filter)))
            {
                Filter value = filter;
                var button = new Button(() => SetFilter(value)) { text = filter.ToString() };
                button.AddToClassList("trader-filter");
                _filterBar.Add(button);
                _filterButtons[filter] = button;
            }
            SetFilter(Filter.All);
        }

        private void SetFilter(Filter filter)
        {
            _filter = filter;
            foreach (KeyValuePair<Filter, Button> pair in _filterButtons)
                pair.Value.EnableInClassList(FilterActiveClass, pair.Key == filter);
            Refresh();
        }

        private static bool Matches(ItemDefinition item, Filter filter)
        {
            if (item == null) return false;
            switch (filter)
            {
                case Filter.Weapons: return item.Category == ItemCategory.Weapon || item.Category == ItemCategory.Attachment;
                case Filter.Ammo: return item.Category == ItemCategory.Ammo;
                case Filter.Gear: return item.Category == ItemCategory.Armor || item.Category == ItemCategory.Throwable ||
                                         item.Category == ItemCategory.Key || item is ContainerItemDefinition;
                case Filter.Medical: return item.Category == ItemCategory.Medical;
                default: return true;
            }
        }

        private static void SetText(Label label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
