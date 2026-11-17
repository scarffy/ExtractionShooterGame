using ExtractionShooter.Core.Inventory;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// Static data for one item type. Inventories only store the <see cref="Id"/> (plus per-instance
    /// state); everything else is looked up here. Subclasses add data for weapons, armor and bags.
    /// </summary>
    [CreateAssetMenu(fileName = "Item", menuName = "Extraction Shooter/Items/Item")]
    public class ItemDefinition : ScriptableObject
    {
        [Tooltip("Stable unique id. Saved in stashes and sent over the network; never change it once shipped.")]
        [SerializeField] private string _id;

        [SerializeField] private string _displayName;
        [SerializeField, TextArea] private string _description;
        [SerializeField] private Sprite _icon;
        [SerializeField] private ItemCategory _category;

        [Tooltip("Maximum quantity in one inventory slot.")]
        [SerializeField, Min(1)] private int _maxStack = 1;

        [Tooltip("Value per unit, used for scoring and trading.")]
        [SerializeField, Min(0)] private int _value;

        [Header("Grid size (cells, unrotated)")]
        [SerializeField, Min(1)] private int _width = 1;
        [SerializeField, Min(1)] private int _height = 1;

        public string Id => _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public ItemCategory Category => _category;
        /// <summary>Max quantity per slot. Items with per-instance state always return 1.</summary>
        public virtual int MaxStack => Mathf.Max(1, _maxStack);
        public int Value => _value;

        /// <summary>Cells the item covers in a grid inventory, unrotated.</summary>
        public ItemSize Size => new ItemSize(_width, _height);

        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id))
                Debug.LogWarning($"[{nameof(ItemDefinition)}] '{name}' has no id.", this);
        }
    }
}
