using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Weapons;
using UnityEngine;

namespace ExtractionShooter.Gameplay.AI
{
    /// <summary>Tuning for one kind of bot: senses, movement, shooting and what it drops.</summary>
    [CreateAssetMenu(fileName = "BotSettings", menuName = "Extraction Shooter/Bot Settings")]
    public sealed class BotSettings : ScriptableObject
    {
        [Header("Weapon")]
        [SerializeField] private WeaponDefinition _weapon;
        [SerializeField] private AmmoDefinition _ammo;

        [Tooltip("Cone half-angle in degrees added to the weapon's own spread. Bots are not perfect shots.")]
        [SerializeField, Range(0f, 15f)] private float _aimError = 2.5f;

        [Tooltip("Aim error is multiplied by this on the first shot at a target and settles to 1 over the settle time.")]
        [SerializeField, Min(1f)] private float _firstShotErrorMultiplier = 3f;

        [SerializeField, Min(0.1f)] private float _aimSettleSeconds = 2.5f;

        [Tooltip("Vertical share of the aim error. Below 1, misses pass beside the target instead of over it.")]
        [SerializeField, Range(0f, 1f)] private float _verticalErrorScale = 0.4f;

        [SerializeField, Range(0f, 1f)] private float _headshotChance = 0.1f;
        [SerializeField, Min(1)] private int _burstMin = 2;
        [SerializeField, Min(1)] private int _burstMax = 4;
        [SerializeField, Min(0f)] private float _burstPauseMin = 0.35f;
        [SerializeField, Min(0f)] private float _burstPauseMax = 0.9f;

        [Header("Senses")]
        [SerializeField, Min(1f)] private float _sightRange = 40f;
        [SerializeField, Range(10f, 360f)] private float _fieldOfView = 140f;

        [Tooltip("Players this close are noticed even behind the bot.")]
        [SerializeField, Min(0f)] private float _hearingRange = 6f;

        [Tooltip("Seconds from first seeing a player to the first shot.")]
        [SerializeField, Min(0f)] private float _reactionSeconds = 0.6f;

        [Tooltip("Seconds after a player deploys during which bots ignore them, unless that player attacks first.")]
        [SerializeField, Min(0f)] private float _insertionGraceSeconds = 10f;

        [SerializeField, Min(0.05f)] private float _thinkInterval = 0.2f;
        [SerializeField, Min(0f)] private float _loseSightSeconds = 1.5f;
        [SerializeField, Min(0f)] private float _searchSeconds = 6f;

        [Header("Movement")]
        [SerializeField, Min(0.5f)] private float _patrolSpeed = 2.2f;
        [SerializeField, Min(0.5f)] private float _chaseSpeed = 4f;
        [SerializeField, Min(1f)] private float _patrolRadius = 15f;
        [SerializeField, Min(1f)] private float _engageDistance = 22f;
        [SerializeField, Min(10f)] private float _turnSpeed = 360f;

        [Header("Aim points (height above the target's feet)")]
        [SerializeField] private float _chestHeight = 1.25f;
        [SerializeField] private float _headHeight = 1.65f;

        [Header("Drops")]
        [SerializeField] private LootTable _lootTable;
        [SerializeField, Min(0)] private int _spareRoundsMin = 10;
        [SerializeField, Min(0)] private int _spareRoundsMax = 30;

        public WeaponDefinition Weapon => _weapon;
        public AmmoDefinition Ammo => _ammo;
        public float AimError => _aimError;
        public float FirstShotErrorMultiplier => _firstShotErrorMultiplier;
        public float AimSettleSeconds => _aimSettleSeconds;
        public float VerticalErrorScale => _verticalErrorScale;
        public float HeadshotChance => _headshotChance;
        public int BurstMin => Mathf.Max(1, _burstMin);
        public int BurstMax => Mathf.Max(BurstMin, _burstMax);
        public float BurstPauseMin => _burstPauseMin;
        public float BurstPauseMax => Mathf.Max(_burstPauseMin, _burstPauseMax);
        public float SightRange => _sightRange;
        public float FieldOfView => _fieldOfView;
        public float HearingRange => _hearingRange;
        public float ReactionSeconds => _reactionSeconds;
        public float InsertionGraceSeconds => _insertionGraceSeconds;
        public float ThinkInterval => _thinkInterval;
        public float LoseSightSeconds => _loseSightSeconds;
        public float SearchSeconds => _searchSeconds;
        public float PatrolSpeed => _patrolSpeed;
        public float ChaseSpeed => _chaseSpeed;
        public float PatrolRadius => _patrolRadius;
        public float EngageDistance => _engageDistance;
        public float TurnSpeed => _turnSpeed;
        public float ChestHeight => _chestHeight;
        public float HeadHeight => _headHeight;
        public LootTable LootTable => _lootTable;
        public int SpareRoundsMin => _spareRoundsMin;
        public int SpareRoundsMax => Mathf.Max(_spareRoundsMin, _spareRoundsMax);

        private void OnValidate()
        {
            if (_weapon != null && _ammo != null && !_weapon.Accepts(_ammo))
                Debug.LogWarning($"[{nameof(BotSettings)}] '{name}': {_ammo.name} does not fit {_weapon.name}.", this);
        }
    }
}
