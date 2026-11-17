using ExtractionShooter.Core.Combat;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>Which equipment slot a piece of armor occupies.</summary>
    public enum ArmorSlot
    {
        Helmet = 0,
        BodyArmor = 1,
    }

    /// <summary>
    /// Helmet or body armor rated class 1–6. Rounds whose penetration beats the armor's protection
    /// (class x 10, lower as it wears) get through; weaker rounds are stopped but still deal blunt damage.
    /// Each hit wears the armor by the round's armor damage. Durability is stored per item instance.
    /// See <see cref="ArmorMath"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Armor", menuName = "Extraction Shooter/Items/Armor")]
    public sealed class ArmorDefinition : ItemDefinition
    {
        [Header("Armor")]
        [SerializeField] private ArmorSlot _armorSlot = ArmorSlot.BodyArmor;

        [SerializeField, Range(1, 6)] private int _armorClass = 3;

        [SerializeField, Min(1)] private int _maxDurability = 60;

        [Tooltip("Fraction of a stopped round's damage that still hurts (blunt trauma).")]
        [SerializeField, Range(0f, 1f)] private float _bluntThroughput = 0.2f;

        [Tooltip("Scales durability loss. Ceramic plates wear faster than steel (above 1).")]
        [SerializeField, Min(0f)] private float _wearMultiplier = 1f;

        public ArmorSlot ArmorSlot => _armorSlot;
        public HitZone ProtectedZone => _armorSlot == ArmorSlot.Helmet ? HitZone.Head : HitZone.Body;
        public int ArmorClass => _armorClass;
        public int MaxDurability => _maxDurability;
        public float BluntThroughput => _bluntThroughput;
        public float WearMultiplier => _wearMultiplier;

        /// <summary>Each piece has its own durability, so armor never stacks.</summary>
        public override int MaxStack => 1;
    }
}
