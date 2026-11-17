using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>One rolled result: an item type and how many.</summary>
    public readonly struct LootDrop
    {
        public LootDrop(ItemDefinition definition, int quantity)
        {
            Definition = definition;
            Quantity = quantity;
        }

        public ItemDefinition Definition { get; }
        public int Quantity { get; }
    }

    /// <summary>
    /// Weighted random loot. Each roll picks one entry by weight and a quantity in its range.
    /// </summary>
    [CreateAssetMenu(fileName = "LootTable", menuName = "Extraction Shooter/Loot Table")]
    public sealed class LootTable : ScriptableObject
    {
        [SerializeField, Min(0)] private int _minRolls = 2;
        [SerializeField, Min(0)] private int _maxRolls = 4;
        [SerializeField] private List<LootEntry> _entries = new List<LootEntry>();

        public IReadOnlyList<LootEntry> Entries => _entries;

        /// <summary>Rolls the table. Results may contain the same item more than once.</summary>
        public List<LootDrop> Roll(System.Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));

            var results = new List<LootDrop>();
            int totalWeight = 0;
            foreach (LootEntry entry in _entries)
                if (entry.IsValid) totalWeight += entry.Weight;
            if (totalWeight <= 0) return results;

            int rolls = random.Next(_minRolls, Mathf.Max(_minRolls, _maxRolls) + 1);
            for (int i = 0; i < rolls; i++)
            {
                int pick = random.Next(totalWeight);
                foreach (LootEntry entry in _entries)
                {
                    if (!entry.IsValid) continue;
                    if (pick < entry.Weight)
                    {
                        results.Add(new LootDrop(entry.Item, random.Next(entry.MinQuantity, entry.MaxQuantity + 1)));
                        break;
                    }
                    pick -= entry.Weight;
                }
            }
            return results;
        }

        private void OnValidate()
        {
            if (_maxRolls < _minRolls) _maxRolls = _minRolls;
        }
    }

    [Serializable]
    public sealed class LootEntry
    {
        [SerializeField] private ItemDefinition _item;
        [SerializeField, Min(1)] private int _weight = 1;
        [SerializeField, Min(1)] private int _minQuantity = 1;
        [SerializeField, Min(1)] private int _maxQuantity = 1;

        public ItemDefinition Item => _item;
        public int Weight => _weight;
        public int MinQuantity => Mathf.Max(1, _minQuantity);
        public int MaxQuantity => Mathf.Max(MinQuantity, _maxQuantity);
        public bool IsValid => _item != null && !string.IsNullOrEmpty(_item.Id) && _weight > 0;
    }
}
