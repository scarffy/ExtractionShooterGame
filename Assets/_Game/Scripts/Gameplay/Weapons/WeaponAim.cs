using ExtractionShooter.Core.Combat;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>
    /// Aiming down sights. The owner raises the sights while the aim button is held (and a weapon is drawn) and
    /// tells the server; both sides ease <see cref="Aim01"/> toward the target at the weapon's aim speed, so the
    /// server's shot spread matches the crosshair the owner sees. Movement widens the cone (<see cref="SpreadModel"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter), typeof(PlayerEquipment))]
    public sealed class WeaponAim : NetworkBehaviour, IMovementLimiter
    {
        [Tooltip("Horizontal speed (m/s) that counts as full movement for spread.")]
        [SerializeField, Min(0.1f)] private float _fullMoveSpeed = 5f;

        [Tooltip("Spread multiplier while crouched.")]
        [SerializeField, Range(0.3f, 1f)] private float _crouchSpreadMultiplier = 0.75f;

        [Tooltip("Recoil multiplier when fully aimed down sights (eased in as the sights come up).")]
        [SerializeField, Range(0.2f, 1f)] private float _aimRecoilMultiplier = 0.7f;

        [Tooltip("Recoil multiplier while crouched.")]
        [SerializeField, Range(0.2f, 1f)] private float _crouchRecoilMultiplier = 0.8f;

        [Tooltip("How quickly the measured movement speed follows the real one.")]
        [SerializeField, Min(0.1f)] private float _speedSmoothing = 12f;

        private readonly SyncVar<bool> _serverAiming = new SyncVar<bool>();

        private PlayerCharacter _character;
        private PlayerEquipment _equipment;
        private PlayerMotor _motor;
        private bool _ownerAiming;
        private float _aim01;
        private Vector3 _lastPosition;
        private float _speed;

        /// <summary>0 at the hip, 1 fully aimed. Valid for the owner and on the server.</summary>
        public float Aim01 => _aim01;

        /// <summary>True while the sights are (being) raised.</summary>
        public bool IsAiming => IsOwner ? _ownerAiming : _serverAiming.Value;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _equipment = GetComponent<PlayerEquipment>();
            _motor = GetComponent<PlayerMotor>();
            _lastPosition = transform.position;
        }

        /// <summary>
        /// 0 standing still, 1 at full movement speed (horizontal). Measured from the position on every peer, so the
        /// owner (whose controller does not simulate) and the server agree.
        /// </summary>
        public float Move01 => Mathf.Clamp01(_speed / _fullMoveSpeed);

        // IMovementLimiter: no sprinting with the sights up, and slower.
        bool IMovementLimiter.AllowsSprint => !IsAiming;
        float IMovementLimiter.SpeedMultiplier =>
            IsAiming && _motor != null && _motor.Settings != null ? _motor.Settings.AimingSpeedMultiplier : 1f;

        /// <summary>Current cone half-angle in degrees for a weapon (with attachments) and round, given aim and movement.</summary>
        public float CurrentSpread(in WeaponStats stats, AmmoDefinition ammo)
        {
            if (stats.Weapon == null) return 0f;
            float baseSpread = stats.Spread + (ammo != null ? ammo.ExtraSpread : 0f);
            float spread = SpreadModel.ConeHalfAngle(baseSpread, stats.HipSpreadMultiplier, _aim01, stats.MoveSpread, Move01);
            return _motor != null && _motor.IsCrouched ? spread * _crouchSpreadMultiplier : spread;
        }

        /// <summary>
        /// How strongly recoil kicks right now: the attachments' multiplier, less when aiming down sights and crouched
        /// (<see cref="RecoilPattern.Scale"/>).
        /// </summary>
        public float CurrentRecoilScale(in WeaponStats stats)
        {
            if (stats.Weapon == null) return 0f;
            bool crouched = _motor != null && _motor.IsCrouched;
            return RecoilPattern.Scale(stats.RecoilMultiplier, _aim01, crouched, _aimRecoilMultiplier, _crouchRecoilMultiplier);
        }

        private void Update()
        {
            MeasureSpeed();
            bool alive = _character.Health.IsAlive;
            WeaponDefinition weapon = _equipment.ActiveWeapon;

            if (IsOwner)
            {
                bool wantAim = alive && weapon != null && _character.Input.AimHeld;
                if (wantAim != _ownerAiming)
                {
                    _ownerAiming = wantAim;
                    ServerSetAiming(wantAim);
                }
            }

            bool aiming = IsOwner ? _ownerAiming : IsServerInitialized && _serverAiming.Value;
            if (!alive || weapon == null) aiming = false;
            float aimSeconds = _equipment.TryGetActiveWeaponStats(out WeaponStats stats) ? stats.AimSeconds : 0.2f;
            _aim01 = SpreadModel.StepAim(_aim01, aiming, aimSeconds, Time.deltaTime);
        }

        private void MeasureSpeed()
        {
            float deltaTime = Time.deltaTime;
            Vector3 position = transform.position;
            Vector3 delta = position - _lastPosition;
            _lastPosition = position;
            if (deltaTime <= 0f) return;

            delta.y = 0f;
            float instant = delta.magnitude / deltaTime;
            if (instant > 50f) instant = 0f; // a teleport, not movement
            _speed = Mathf.Lerp(_speed, instant, 1f - Mathf.Exp(-_speedSmoothing * deltaTime));
        }

        [ServerRpc]
        private void ServerSetAiming(bool aiming) => _serverAiming.Value = aiming && _character.Health.IsAlive;
    }
}
