using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// Carried weight and its movement penalty (<see cref="Encumbrance"/>): heavier loads walk slower, and an overloaded
    /// player can't sprint. Weighs the equipment, chest rig, backpack and pockets a few times a second. Runs on the
    /// server (which moves the player) and on the owner (for the UI), both from the replicated containers.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter))]
    public sealed class PlayerEncumbrance : MonoBehaviour, IMovementLimiter
    {
        [Tooltip("No penalty up to this many kilograms.")]
        [SerializeField, Min(1f)] private float _lightLimit = 35f;

        [Tooltip("Speed falls to the heavy speed by this weight.")]
        [SerializeField, Min(1f)] private float _heavyLimit = 55f;

        [Tooltip("Above this the player is overloaded: no sprinting.")]
        [SerializeField, Min(1f)] private float _maxLoad = 70f;

        [SerializeField, Range(0.1f, 1f)] private float _heavySpeed = 0.85f;
        [SerializeField, Range(0.1f, 1f)] private float _maxLoadSpeed = 0.7f;
        [SerializeField, Range(0.1f, 1f)] private float _overloadedSpeed = 0.45f;

        [Tooltip("Seconds between re-weighing.")]
        [SerializeField, Min(0.05f)] private float _interval = 0.25f;

        private PlayerEquipment _equipment;
        private Encumbrance _rules;
        private float _nextWeigh;

        /// <summary>Kilograms carried right now.</summary>
        public float CarriedWeight { get; private set; }

        public float MaxLoad => _rules.MaxLoad;
        public Encumbrance Rules => _rules;
        public LoadLevel Level => _rules.LevelOf(CarriedWeight);
        public bool AllowsSprint => _rules.AllowsSprint(CarriedWeight);
        public float SpeedMultiplier => _rules.SpeedMultiplier(CarriedWeight);

        private void Awake()
        {
            _equipment = GetComponent<PlayerEquipment>();
            _rules = new Encumbrance(_lightLimit, Mathf.Max(_heavyLimit, _lightLimit + 1f), Mathf.Max(_maxLoad, _heavyLimit + 1f),
                _heavySpeed, _maxLoadSpeed, _overloadedSpeed);
        }

        private void Update()
        {
            if (Time.time < _nextWeigh || _equipment == null) return;
            _nextWeigh = Time.time + _interval;

            ItemDatabase database = _equipment.Container != null ? _equipment.Container.ItemDatabase : null;
            CarriedWeight = GearWeight.Of(_equipment.Container, database) + GearWeight.Of(_equipment.ChestRigStorage, database) +
                            GearWeight.Of(_equipment.BackpackStorage, database) + GearWeight.Of(_equipment.Pockets, database);
        }
    }
}
