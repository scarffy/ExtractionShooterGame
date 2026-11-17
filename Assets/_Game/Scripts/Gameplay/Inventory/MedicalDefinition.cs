using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// A consumable that restores health (bandage, medkit). Used from the player's own equipment or bags;
    /// one item is consumed per use, and never when the player is already at full health.
    /// </summary>
    [CreateAssetMenu(fileName = "Medical", menuName = "Extraction Shooter/Items/Medical")]
    public sealed class MedicalDefinition : ItemDefinition
    {
        [Header("Medical")]
        [SerializeField, Min(1)] private int _healAmount = 20;

        public int HealAmount => _healAmount;
    }
}
