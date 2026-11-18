using ExtractionShooter.Core.Combat;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// A treatment (bandage, medkit, splint). Used from the player's own pockets or bags (or the quick-heal key): it
    /// takes <see cref="UseSeconds"/>, during which the player moves slower and can't shoot; when it completes the item is
    /// used up and it heals, stops bleeding and/or sets a fractured leg. Cancelling (or losing the item meanwhile) has no
    /// effect and costs nothing. It is refused when it would do nothing (see <see cref="TreatmentRules"/>).
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

        [Tooltip("What the player is seen holding while using it.")]
        [SerializeField] private UseItemVisual _useVisual = UseItemVisual.Bandage;

        public int HealAmount => _healAmount;
        public float UseSeconds => _useSeconds;
        public bool StopsBleeding => _stopsBleeding;
        public bool FixesFracture => _fixesFracture;
        public UseItemVisual UseVisual => _useVisual;

        public TreatmentOption ToOption() => new TreatmentOption(_healAmount, _stopsBleeding, _fixesFracture, _useSeconds);
    }
}
