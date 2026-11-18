using ExtractionShooter.Core.Combat;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Visuals;
using ExtractionShooter.Gameplay.Weapons;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// Client-side presentation of the third-person body (<see cref="CharacterBody"/>): dressed in the replicated
    /// equipment (rebuilt on <see cref="PlayerEquipment.Changed"/>, so helmets, armor, bags and weapons show on observers),
    /// posed from the replicated movement, stance, aim, reload and look pitch, with a muzzle flash on every confirmed shot.
    /// <list type="bullet">
    /// <item>Owner: the body only casts a shadow (the first-person view is <see cref="FirstPersonViewmodel"/>).</item>
    /// <item>Dead: hidden for everyone (the corpse takes over).</item>
    /// </list>
    /// Look pitch is cosmetic: the owner sends it a few times a second, the server clamps it and replicates it so
    /// observers see where the weapon points. Dedicated servers never build the body.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVisuals : NetworkBehaviour
    {
        private const float PitchOffset = 90f;
        private const float TypicalUseSeconds = 4f;

        [Tooltip("The third-person body (child object).")]
        [SerializeField] private CharacterBody _body;

        [Tooltip("The owner's own body still casts a shadow in first person.")]
        [SerializeField] private bool _ownerCastsShadow = true;

        [Tooltip("Seconds between look pitch updates from the owner (observers smooth between them).")]
        [SerializeField, Min(0.02f)] private float _pitchSendInterval = 0.1f;

        [Tooltip("Smallest pitch change in degrees worth sending.")]
        [SerializeField, Min(0f)] private float _pitchSendThreshold = 1.5f;

        [Tooltip("Seconds the weapon stays shouldered after a shot when not aiming.")]
        [SerializeField, Min(0f)] private float _shoulderAfterShotSeconds = 1.2f;

        // Look pitch + 90 (0..180), so it fits a byte.
        private readonly SyncVar<byte> _pitch = new SyncVar<byte>((byte)PitchOffset);

        private readonly Outfit _outfit = new Outfit();

        private Health _health;
        private PlayerMotor _motor;
        private PlayerLook _look;
        private PlayerStance _stance;
        private PlayerEquipment _equipment;
        private WeaponAim _aim;
        private WeaponReloader _reloader;
        private PlayerInjuries _injuries;
        private HitscanWeapon _weapon;

        private Vector3 _lastPosition;
        private float _lastShotTime = float.NegativeInfinity;
        private float _nextPitchSend;
        private byte _sentPitch = (byte)PitchOffset;
        private bool _active;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _motor = GetComponent<PlayerMotor>();
            _look = GetComponent<PlayerLook>();
            _stance = GetComponent<PlayerStance>();
            _equipment = GetComponent<PlayerEquipment>();
            _aim = GetComponent<WeaponAim>();
            _reloader = GetComponent<WeaponReloader>();
            _injuries = GetComponent<PlayerInjuries>();
            _weapon = GetComponent<HitscanWeapon>();

            if (_body == null)
                Debug.LogError($"[{nameof(PlayerVisuals)}] Body is not assigned on {name}.", this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (_body == null) return;

            _body.Build();
            _active = _body.IsBuilt;
            _health.Changed += OnHealthChanged;
            if (_equipment != null) _equipment.Changed += RefreshOutfit;
            if (_weapon != null) _weapon.ShotConfirmed += OnShotConfirmed;
            _lastPosition = transform.position;
            RefreshOutfit();
            RefreshMode();
            _body.Snap();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (_body == null) return;

            _health.Changed -= OnHealthChanged;
            if (_equipment != null) _equipment.Changed -= RefreshOutfit;
            if (_weapon != null) _weapon.ShotConfirmed -= OnShotConfirmed;
            _active = false;
        }

        private void OnHealthChanged(int previous, int current) => RefreshMode();

        private void RefreshMode()
        {
            if (!_active) return;
            BodyRenderMode mode = !_health.IsAlive ? BodyRenderMode.Hidden
                : !IsOwner ? BodyRenderMode.Visible
                : _ownerCastsShadow ? BodyRenderMode.ShadowsOnly : BodyRenderMode.Hidden;
            _body.SetRenderMode(mode);
        }

        private void RefreshOutfit()
        {
            if (!_active || _equipment == null) return;
            _outfit.ReadFrom(_equipment.Container, _equipment.ActiveWeaponSlot);
            _body.ApplyOutfit(_outfit);
        }

        private void OnShotConfirmed(Vector3 origin, Vector3 end)
        {
            _lastShotTime = Time.time;
            _body.Flash();
        }

        private void Update()
        {
            if (!_active) return;

            float pitch = IsOwner ? OwnerPitch() : _pitch.Value - PitchOffset;
            if (IsOwner) SendPitch(pitch);

            float deltaTime = Time.deltaTime;
            Vector3 position = transform.position;
            Vector3 velocity = deltaTime > 0.0001f ? (position - _lastPosition) / deltaTime : Vector3.zero;
            _lastPosition = position;
            if (velocity.sqrMagnitude > 400f) velocity = Vector3.zero; // a teleport, not movement

            bool aiming = (_aim != null && _aim.IsAiming) || Time.time - _lastShotTime < _shoulderAfterShotSeconds;
            float useSeconds = _injuries != null && _injuries.IsTreating ? Time.time - _injuries.UseStartedAt : 0f;
            _body.SetPose(new BodyPose
            {
                LocalVelocity = transform.InverseTransformDirection(velocity),
                Crouch01 = _motor != null && _motor.IsCrouched ? 1f : 0f,
                CrouchHeadRatio = _stance != null ? _stance.CrouchHeightRatio : 0f,
                Pitch = pitch,
                Aim01 = aiming ? 1f : 0f,
                Sprinting = _motor != null && _motor.IsSprinting,
                Lowered = (_reloader != null && _reloader.IsReloading) || (_injuries != null && _injuries.IsTreating),
                UseItem = _injuries != null ? _injuries.UseVisual : UseItemVisual.None,
                UseSeconds = useSeconds,
                // Only the owner knows the item's use time; others see a typical one.
                UseProgress = IsOwner && _injuries != null ? _injuries.TreatmentProgress : Mathf.Clamp01(useSeconds / TypicalUseSeconds),
            });
        }

        /// <summary>Owner: the look pitch in degrees (positive up), from the aim direction.</summary>
        private float OwnerPitch()
        {
            if (_look == null) return 0f;
            Vector3 aim = _look.AimDirection;
            return Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private void SendPitch(float pitch)
        {
            if (Time.time < _nextPitchSend || !IsClientInitialized) return;
            byte encoded = (byte)Mathf.Clamp(Mathf.RoundToInt(pitch + PitchOffset), 0, 180);
            if (Mathf.Abs(encoded - _sentPitch) < _pitchSendThreshold) return;

            _nextPitchSend = Time.time + _pitchSendInterval;
            _sentPitch = encoded;
            ServerSetPitch(encoded);
        }

        [ServerRpc]
        private void ServerSetPitch(byte encoded) => _pitch.Value = (byte)Mathf.Min(encoded, 180);
    }
}
