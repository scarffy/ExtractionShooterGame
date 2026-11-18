using ExtractionShooter.Gameplay.AI;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Weapons;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// Client-side presentation of a bot: a <see cref="CharacterBody"/> in the bot faction's profile, wearing the
    /// outfit set here and carrying its loadout variant's weapon (with attachments), walking with its movement,
    /// shouldering the weapon while it fights, flashing the muzzle on every shot, hidden once dead (the corpse takes
    /// over). Dedicated servers never build the body.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BotBrain), typeof(Health))]
    public sealed class BotVisuals : NetworkBehaviour
    {
        [Tooltip("The third-person body (child object).")]
        [SerializeField] private CharacterBody _body;

        [Header("Outfit (all optional)")]
        [SerializeField] private ArmorDefinition _helmet;
        [SerializeField] private ArmorDefinition _bodyArmor;
        [SerializeField] private ContainerItemDefinition _chestRig;
        [SerializeField] private ContainerItemDefinition _backpack;
        [SerializeField] private MeleeDefinition _melee;

        [Tooltip("Seconds the weapon stays shouldered after a shot.")]
        [SerializeField, Min(0f)] private float _shoulderAfterShotSeconds = 2f;

        private readonly Outfit _outfit = new Outfit();

        private BotBrain _brain;
        private Health _health;
        private BotSettings _dressedFor;
        private Vector3 _lastPosition;
        private float _lastShotTime = float.NegativeInfinity;
        private bool _active;

        private void Awake()
        {
            _brain = GetComponent<BotBrain>();
            _health = GetComponent<Health>();
            if (_body == null)
                Debug.LogError($"[{nameof(BotVisuals)}] Body is not assigned on {name}.", this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (_body == null) return;

            _body.Build();
            _active = _body.IsBuilt;
            _health.Changed += OnHealthChanged;
            _brain.ShotConfirmed += OnShotConfirmed;
            _lastPosition = transform.position;
            _dressedFor = null;
            Dress();
            OnHealthChanged(0, 0);
            _body.Snap();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (_body == null) return;
            _health.Changed -= OnHealthChanged;
            _brain.ShotConfirmed -= OnShotConfirmed;
            _active = false;
        }

        private void OnHealthChanged(int previous, int current)
        {
            if (_active) _body.SetRenderMode(_health.IsAlive ? BodyRenderMode.Visible : BodyRenderMode.Hidden);
        }

        private void OnShotConfirmed(Vector3 origin, Vector3 end)
        {
            _lastShotTime = Time.time;
            _body.Flash();
        }

        /// <summary>Puts on the outfit and the current loadout variant's weapon (the variant replicates after spawn).</summary>
        private void Dress()
        {
            BotSettings settings = _brain.Settings;
            if (settings == _dressedFor) return;
            _dressedFor = settings;

            _outfit.Clear();
            _outfit.Set(EquipmentSlot.Helmet, _helmet);
            _outfit.Set(EquipmentSlot.BodyArmor, _bodyArmor);
            _outfit.Set(EquipmentSlot.ChestRig, _chestRig);
            _outfit.Set(EquipmentSlot.Backpack, _backpack);
            _outfit.Set(EquipmentSlot.Melee, _melee);
            WeaponDefinition weapon = settings != null ? settings.Weapon : null;
            if (weapon != null)
            {
                EquipmentSlot slot = weapon.WeaponClass == WeaponClass.Pistol ? EquipmentSlot.Pistol : EquipmentSlot.Primary1;
                _outfit.Set(slot, weapon, settings.Attachments);
                _outfit.Drawn = slot;
            }
            _body.ApplyOutfit(_outfit);
        }

        private void Update()
        {
            if (!_active) return;
            Dress();

            float deltaTime = Time.deltaTime;
            Vector3 position = transform.position;
            Vector3 velocity = deltaTime > 0.0001f ? (position - _lastPosition) / deltaTime : Vector3.zero;
            _lastPosition = position;
            if (velocity.sqrMagnitude > 400f) velocity = Vector3.zero; // a teleport, not movement

            _body.SetPose(new BodyPose
            {
                LocalVelocity = transform.InverseTransformDirection(velocity),
                Aim01 = Time.time - _lastShotTime < _shoulderAfterShotSeconds ? 1f : 0f,
            });
        }
    }
}
