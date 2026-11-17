using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// A treatment (bandage, medkit, splint). Used from the player's own pockets or bags: one item is consumed when
    /// the treatment starts, then it heals over <see cref="UseSeconds"/> and, when it finishes, stops bleeding and/or
    /// sets a fractured leg. It is refused when it would do nothing.
    /// </summary>
    [CreateAssetMenu(fileName = "Medical", menuName = "Extraction Shooter/Items/Medical")]
    public sealed class MedicalDefinition : ItemDefinition
    {
        [Header("Medical")]
        [SerializeField, Min(0)] private int _healAmount = 20;

        [Tooltip("Seconds the treatment takes. No shooting or sprinting meanwhile, and slower movement.")]
        [SerializeField, Min(0.1f)] private float _useSeconds = 3f;

        [SerializeField] private bool _stopsBleeding;
        [SerializeField] private bool _fixesFracture;

        public int HealAmount => _healAmount;
        public float UseSeconds => _useSeconds;
        public bool StopsBleeding => _stopsBleeding;
        public bool FixesFracture => _fixesFracture;
    }
}
