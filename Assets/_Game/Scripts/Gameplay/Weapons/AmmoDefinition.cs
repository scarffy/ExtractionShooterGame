using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>
    /// One type of round (for example 5.56x45mm M855). Stackable. The round decides damage and
    /// penetration; the weapon decides fire rate, magazine size, spread and range.
    /// </summary>
    [CreateAssetMenu(fileName = "Ammo", menuName = "Extraction Shooter/Items/Ammo")]
    public sealed class AmmoDefinition : ItemDefinition
    {
        [Header("Ammo")]
        [SerializeField] private Caliber _caliber = Caliber.Mm9x19;

        [Tooltip("Damage per projectile before hit zone multipliers and armor.")]
        [SerializeField, Min(1)] private int _damage = 30;

        [Tooltip("Penetration power. Armor protects with class x 10: equal penetration lets half through, +10 all of it.")]
        [SerializeField, Range(0f, 100f)] private float _penetration = 20f;

        [Tooltip("Armor durability lost per point of damage, in percent.")]
        [SerializeField, Range(0f, 200f)] private float _armorDamagePercent = 40f;

        [Tooltip("Projectiles per round. Shotgun shells fire several pellets.")]
        [SerializeField, Range(1, 16)] private int _projectiles = 1;

        [Tooltip("Extra spread in degrees added to the weapon's spread (shot pellets).")]
        [SerializeField, Range(0f, 15f)] private float _extraSpread;

        public Caliber Caliber => _caliber;
        public int Damage => _damage;
        public float Penetration => _penetration;
        public float ArmorDamagePercent => _armorDamagePercent;
        public int Projectiles => _projectiles;
        public float ExtraSpread => _extraSpread;
    }
}
