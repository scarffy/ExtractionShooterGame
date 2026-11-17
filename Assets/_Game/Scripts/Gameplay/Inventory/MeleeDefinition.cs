using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// A melee weapon (knife) for the melee equipment slot. Melee items are never lost: the melee slot stays
    /// with the player through death, and its item is not dropped on the corpse.
    /// </summary>
    [CreateAssetMenu(fileName = "Melee", menuName = "Extraction Shooter/Items/Melee")]
    public sealed class MeleeDefinition : ItemDefinition
    {
        [Header("Melee")]
        [SerializeField, Min(1)] private int _damage = 35;

        [SerializeField, Min(0.5f)] private float _range = 1.6f;

        [Tooltip("Seconds between swings.")]
        [SerializeField, Min(0.1f)] private float _cooldown = 0.7f;

        [Tooltip("Penetration against armor class x 10 (a knife barely gets through plates).")]
        [SerializeField, Min(0f)] private float _penetration = 15f;

        [Tooltip("Armor damage, percent of the hit.")]
        [SerializeField, Min(0f)] private float _armorDamagePercent = 20f;

        public int Damage => _damage;
        public float Range => _range;
        public float Cooldown => _cooldown;
        public float Penetration => _penetration;
        public float ArmorDamagePercent => _armorDamagePercent;

        /// <summary>One knife per slot.</summary>
        public override int MaxStack => 1;
    }
}
