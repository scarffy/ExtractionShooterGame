using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Weapons;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// All item definitions in the game, looked up by id. Server and clients must use the same asset.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Extraction Shooter/Item Database")]
    public sealed class ItemDatabase : ScriptableObject, IItemCatalog, IModCatalog
    {
        [SerializeField] private List<ItemDefinition> _items = new List<ItemDefinition>();

        private Dictionary<string, ItemDefinition> _byId;

        public IReadOnlyList<ItemDefinition> Items => _items;

        public bool TryGet(string itemId, out ItemDefinition definition)
        {
            definition = null;
            return !string.IsNullOrEmpty(itemId) && Lookup.TryGetValue(itemId, out definition);
        }

        public bool Contains(string itemId) => TryGet(itemId, out _);

        public int GetMaxStack(string itemId) => TryGet(itemId, out ItemDefinition definition) ? definition.MaxStack : 0;

        public ItemSize GetSize(string itemId) => TryGet(itemId, out ItemDefinition definition) ? definition.Size : ItemSize.One;

        public bool TryGetAttachmentSlot(string itemId, out AttachmentSlot slot)
        {
            slot = default;
            if (!TryGet(itemId, out ItemDefinition definition) || !(definition is AttachmentDefinition mod)) return false;
            slot = mod.Slot;
            return true;
        }

        public bool Fits(string attachmentId, string weaponId) =>
            TryGet(attachmentId, out ItemDefinition a) && a is AttachmentDefinition mod &&
            TryGet(weaponId, out ItemDefinition w) && w is WeaponDefinition weapon && mod.Fits(weapon);

        public int MagazineSize(string weaponId, string attachments) =>
            TryGet(weaponId, out ItemDefinition definition) && definition is WeaponDefinition weapon
                ? WeaponStats.Of(weapon, new ItemStack(weaponId, 1, attachments: attachments), this).MagazineSize
                : 0;

        private Dictionary<string, ItemDefinition> Lookup
        {
            get
            {
                if (_byId == null) Rebuild();
                return _byId;
            }
        }

        private void OnEnable() => _byId = null;

        private void OnValidate()
        {
            _byId = null;
            var seen = new HashSet<string>();
            foreach (ItemDefinition item in _items)
            {
                if (item == null || string.IsNullOrEmpty(item.Id)) continue;
                if (!seen.Add(item.Id))
                    Debug.LogError($"[{nameof(ItemDatabase)}] Duplicate item id '{item.Id}'.", this);
            }
        }

        private void Rebuild()
        {
            _byId = new Dictionary<string, ItemDefinition>();
            foreach (ItemDefinition item in _items)
            {
                if (item == null || string.IsNullOrEmpty(item.Id)) continue;
                _byId[item.Id] = item;
            }
        }
    }
}
