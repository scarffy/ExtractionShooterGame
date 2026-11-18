using System.Collections.Generic;
using System.Text;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Weapons;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// What a character wears and carries, slot by slot (the <see cref="EquipmentSlot"/> layout), with each weapon's
    /// fitted attachments and the weapon in hand. Filled from an equipment container (players, the lobby loadout) or
    /// set directly (bots), then handed to <see cref="CharacterBody.ApplyOutfit"/>, which rebuilds only what changed.
    /// </summary>
    public sealed class Outfit
    {
        private readonly ItemDefinition[] _items = new ItemDefinition[EquipmentRules.SlotCount];
        private readonly List<AttachmentDefinition>[] _mods = new List<AttachmentDefinition>[EquipmentRules.SlotCount];
        private readonly StringBuilder _key = new StringBuilder(128);

        public Outfit()
        {
            for (int i = 0; i < _mods.Length; i++) _mods[i] = new List<AttachmentDefinition>(WeaponMods.SlotCount);
        }

        /// <summary>The weapon in hand, or null (arms free).</summary>
        public EquipmentSlot? Drawn { get; set; }

        public ItemDefinition Get(EquipmentSlot slot) => _items[(int)slot];

        public T Get<T>(EquipmentSlot slot) where T : ItemDefinition => _items[(int)slot] as T;

        /// <summary>Attachments fitted to the weapon in a slot (empty for other items).</summary>
        public IReadOnlyList<AttachmentDefinition> ModsOf(EquipmentSlot slot) => _mods[(int)slot];

        public void Clear()
        {
            for (int i = 0; i < _items.Length; i++)
            {
                _items[i] = null;
                _mods[i].Clear();
            }
            Drawn = null;
        }

        /// <summary>Puts an item in a slot, with its attachments (weapons).</summary>
        public void Set(EquipmentSlot slot, ItemDefinition item, IEnumerable<AttachmentDefinition> mods = null)
        {
            _items[(int)slot] = item;
            List<AttachmentDefinition> list = _mods[(int)slot];
            list.Clear();
            if (mods == null || !(item is WeaponDefinition)) return;
            foreach (AttachmentDefinition mod in mods)
                if (mod != null) list.Add(mod);
        }

        /// <summary>
        /// Reads an equipment container (8 slots, <see cref="EquipmentSlot"/> order). Missing or unknown items read as
        /// empty. <paramref name="drawn"/> is the weapon in hand.
        /// </summary>
        public void ReadFrom(ItemContainer container, EquipmentSlot? drawn)
        {
            Clear();
            if (container == null || container.ItemDatabase == null) return;

            ItemDatabase database = container.ItemDatabase;
            int count = Mathf.Min(EquipmentRules.SlotCount, container.SlotCount);
            for (int i = 0; i < count; i++)
            {
                ItemStack stack = container.GetSlot(i);
                if (stack.IsEmpty || !database.TryGet(stack.ItemId, out ItemDefinition item)) continue;

                _items[i] = item;
                if (!(item is WeaponDefinition) || string.IsNullOrEmpty(stack.Attachments)) continue;
                foreach (KeyValuePair<AttachmentSlot, string> fitted in WeaponMods.All(stack.Attachments))
                    if (database.TryGet(fitted.Value, out ItemDefinition found) && found is AttachmentDefinition mod) _mods[i].Add(mod);
            }
            Drawn = drawn is EquipmentSlot slot && _items[(int)slot] is WeaponDefinition ? drawn : null;
        }

        /// <summary>A key for the given slots: equal keys mean the same visible items (and attachments).</summary>
        public string KeyOf(params EquipmentSlot[] slots)
        {
            _key.Length = 0;
            foreach (EquipmentSlot slot in slots)
            {
                ItemDefinition item = _items[(int)slot];
                _key.Append(item != null ? item.Id : "-");
                foreach (AttachmentDefinition mod in _mods[(int)slot]) _key.Append('+').Append(mod.Id);
                _key.Append('|');
            }
            return _key.ToString();
        }
    }
}
