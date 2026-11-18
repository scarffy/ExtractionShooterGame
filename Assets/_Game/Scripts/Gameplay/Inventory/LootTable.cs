using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Weapons;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>One rolled result: an item type, how many, and the attachments fitted to a rolled weapon.</summary>
    public readonly struct LootDrop
    {
        public LootDrop(ItemDefinition definition, int quantity, string attachments = null)
        {
            Definition = definition;
            Quantity = quantity;
            Attachments = attachments;
        }

        public ItemDefinition Definition { get; }
        public int Quantity { get; }

        /// <summary>Attachments in item-stack form (<see cref="WeaponMods"/>), or null.</summary>
        public string Attachments { get; }
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
        public List<LootDrop> Roll(System.Random random) => Roll(random, 0);

        /// <summary>
        /// Rolls the table with <paramref name="rollBonus"/> extra rolls (negative for fewer; at least one roll is kept when
        /// the table rolls at all). Raid difficulty uses it.
        /// </summary>
        public List<LootDrop> Roll(System.Random random, int rollBonus)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));

            var results = new List<LootDrop>();
            int totalWeight = 0;
            foreach (LootEntry entry in _entries)
                if (entry.IsValid) totalWeight += entry.Weight;
            if (totalWeight <= 0) return results;

            int rolls = random.Next(_minRolls, Mathf.Max(_minRolls, _maxRolls) + 1);
            if (rolls > 0) rolls = Mathf.Max(1, rolls + rollBonus);
            for (int i = 0; i < rolls; i++)
            {
                int pick = random.Next(totalWeight);
                foreach (LootEntry entry in _entries)
                {
                    if (!entry.IsValid) continue;
                    if (pick < entry.Weight)
                    {
                        results.Add(new LootDrop(entry.Item, random.Next(entry.MinQuantity, entry.MaxQuantity + 1),
                            entry.AttachmentsText));
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

        [Tooltip("Weapons only: attachments the weapon comes fitted with (ones that don't fit are skipped).")]
        [SerializeField] private List<AttachmentDefinition> _attachments = new List<AttachmentDefinition>();

        public ItemDefinition Item => _item;
        public int Weight => _weight;
        public int MinQuantity => Mathf.Max(1, _minQuantity);
        public int MaxQuantity => Mathf.Max(MinQuantity, _maxQuantity);
        public bool IsValid => _item != null && !string.IsNullOrEmpty(_item.Id) && _weight > 0;

        /// <summary>The fitted attachments in item-stack form, or null (none, or the item is not a weapon).</summary>
        public string AttachmentsText
        {
            get
            {
                if (_attachments == null || _attachments.Count == 0 || !(_item is WeaponDefinition weapon)) return null;
                string text = null;
                foreach (AttachmentDefinition mod in _attachments)
                    if (mod != null && mod.Fits(weapon)) text = WeaponMods.Set(text, mod.Slot, mod.Id);
                return text;
            }
        }
    }
}
