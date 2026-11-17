using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Core.Trading;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Trading
{
    /// <summary>
    /// The lobby trader: what it sells (unlimited stock) and how it prices buying and selling.
    /// Every item with a value can be sold to it; only the listed offers can be bought.
    /// </summary>
    [CreateAssetMenu(fileName = "Trader", menuName = "Extraction Shooter/Trader")]
    public sealed class TraderDefinition : ScriptableObject
    {
        [SerializeField] private string _displayName = "Trader";

        [Tooltip("Share of an item's value the trader pays.")]
        [SerializeField, Range(0f, 1f)] private float _sellRatio = 0.6f;

        [Tooltip("Multiplier on an item's value when buying. Keep above the sell ratio, or buying and reselling earns money.")]
        [SerializeField, Min(0f)] private float _buyMarkup = 1.25f;

        [SerializeField] private List<TraderOffer> _offers = new List<TraderOffer>();

        [Tooltip("Safe box sizes in upgrade order. The first is the free box every player starts with (its price is ignored).")]
        [SerializeField] private List<SafeBoxTier> _safeBoxTiers = new List<SafeBoxTier> { SafeBoxTier.Default };

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public float SellRatio => _sellRatio;
        public float BuyMarkup => _buyMarkup;
        public IReadOnlyList<TraderOffer> Offers => _offers;
        public IReadOnlyList<SafeBoxTier> SafeBoxTiers => _safeBoxTiers;
        internal SafeBoxTier[] SafeBoxTiersArray => _safeBoxTiers.ToArray();

        public bool TryGetOffer(int index, out TraderOffer offer)
        {
            offer = index >= 0 && index < _offers.Count ? _offers[index] : null;
            return offer != null && offer.Item != null;
        }

        /// <summary>Price of one purchase of an offer.</summary>
        public long BuyPrice(TraderOffer offer) =>
            offer != null && offer.Item != null ? TradeMath.BuyPrice(offer.Item.Value, offer.Quantity, _buyMarkup) : 0;

        /// <summary>
        /// What the trader pays for <paramref name="quantity"/> of a stack: its value scaled by condition (armor),
        /// plus the rounds loaded in a weapon. 0 when the item is unknown or worthless.
        /// </summary>
        public long SellPrice(in ItemStack stack, int quantity, ItemDatabase database)
        {
            if (stack.IsEmpty || database == null || !database.TryGet(stack.ItemId, out ItemDefinition item))
                return 0;

            int count = Mathf.Clamp(quantity, 0, stack.Quantity);
            float condition = item is ArmorDefinition armor ? TradeMath.Condition(stack.Durability, armor.MaxDurability) : 1f;
            long price = TradeMath.SellPrice(item.Value, count, _sellRatio, condition);

            if (stack.AmmoCount > 0 && count > 0 && database.TryGet(stack.AmmoId, out ItemDefinition ammo))
                price += TradeMath.SellPrice(ammo.Value, stack.AmmoCount, _sellRatio);
            return price;
        }

        private void OnValidate()
        {
            if (_buyMarkup <= _sellRatio)
                Debug.LogWarning($"[{nameof(TraderDefinition)}] '{name}': buy markup must be above the sell ratio.", this);

            foreach (TraderOffer offer in _offers)
                if (offer != null && offer.Item != null && offer.Quantity > offer.Item.MaxStack)
                    Debug.LogWarning($"[{nameof(TraderDefinition)}] '{name}': {offer.Item.name} is sold in packs above its max stack.", this);
        }
    }

    /// <summary>One safe box size and what upgrading to it costs.</summary>
    [Serializable]
    public struct SafeBoxTier
    {
        [SerializeField] private string _name;
        [SerializeField, Min(1)] private int _width;
        [SerializeField, Min(1)] private int _height;
        [SerializeField, Min(0)] private long _price;

        public SafeBoxTier(string name, int width, int height, long price)
        {
            _name = name;
            _width = width;
            _height = height;
            _price = price;
        }

        /// <summary>The free box: two cells side by side.</summary>
        public static SafeBoxTier Default => new SafeBoxTier("Safe Box", 2, 1, 0);

        public string Name => string.IsNullOrEmpty(_name) ? $"Safe Box {Width}x{Height}" : _name;
        public int Width => Mathf.Max(1, _width);
        public int Height => Mathf.Max(1, _height);
        public long Price => _price;
    }

    /// <summary>One thing the trader sells, in packs of <see cref="Quantity"/> (ammo by the box).</summary>
    [Serializable]
    public sealed class TraderOffer
    {
        [SerializeField] private ItemDefinition _item;
        [SerializeField, Min(1)] private int _quantity = 1;

        public ItemDefinition Item => _item;

        /// <summary>Items per purchase, never above the item's max stack.</summary>
        public int Quantity => _item != null ? Mathf.Clamp(_quantity, 1, _item.MaxStack) : 1;
    }
}
