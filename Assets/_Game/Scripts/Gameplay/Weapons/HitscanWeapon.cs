using System;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>
    /// Fires the player's drawn weapon. The owner only requests a shot; the server validates
    /// it (alive, not reloading, a round is loaded, fire rate, origin, direction), spends the round,
    /// applies spread per projectile (hip fire is wider than aiming down sights, moving adds more: <see cref="WeaponAim"/>),
    /// traces each bullet along its drop curve (<see cref="BallisticTrace"/>, resolved instantly) against hitboxes, applies
    /// damage with the round's penetration and broadcasts the result for presentation.
    /// Recoil is the owner's: after each shot request the view kicks by the weapon's spray pattern
    /// (<see cref="RecoilPattern"/>, scaled by attachments, aiming and crouching). The next shot's direction is the
    /// kicked view the owner sends, so the server needs no recoil state of its own.
    /// Pulling the trigger on an empty magazine asks for a reload.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter), typeof(PlayerEquipment), typeof(WeaponReloader))]
    [RequireComponent(typeof(WeaponAim))]
    public sealed class HitscanWeapon : NetworkBehaviour, IShotSource
    {
        private const int HitBufferSize = 16;
        private const float MaxRecoveryDelay = 0.2f;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HitBufferSize];

        private PlayerCharacter _character;
        private PlayerEquipment _equipment;
        private WeaponReloader _reloader;
        private WeaponAim _aim;
        private PlayerMotor _motor;
        private double _nextLocalShotTime;
        private double _nextServerShotTime;

        // Shots sent but not yet reflected in the replicated round count.
        private int _unconfirmedShots;

        // Owner: position in the current recoil spray, when it was fired, and the spray's jitter seed.
        private int _recoilShot = -1;
        private double _lastRecoilShotTime;
        private int _recoilJitterSeed;

        /// <summary>Client side. Raised for every confirmed projectile (origin, end point).</summary>
        public event Action<Vector3, Vector3> ShotConfirmed;

        /// <summary>Owner only: raised the moment a shot is requested (before the server confirms it), for first-person effects.</summary>
        public event Action OwnerShotFired;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _equipment = GetComponent<PlayerEquipment>();
            _reloader = GetComponent<WeaponReloader>();
            _aim = GetComponent<WeaponAim>();
            _motor = GetComponent<PlayerMotor>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _equipment.Changed += ResetPrediction;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _equipment.Changed -= ResetPrediction;
        }

        private void Update()
        {
            if (!IsOwner || !_character.Health.IsAlive || IsBusy) return;
            if (!_equipment.TryGetActiveWeapon(out EquipmentSlot _, out WeaponDefinition weapon, out ItemStack item)) return;

            bool triggerPulled = _character.Input.FirePressed;
            bool firing = weapon.FireMode == FireMode.FullAuto ? _character.Input.FireHeld : triggerPulled;
            if (!firing || _reloader.IsReloading) return;

            if (item.AmmoCount - _unconfirmedShots <= 0)
            {
                if (triggerPulled) _reloader.Request();
                return;
            }

            double now = Time.timeAsDouble;
            if (now < _nextLocalShotTime) return;

            _nextLocalShotTime = now + weapon.SecondsBetweenShots;
            _unconfirmedShots++;
            RequestShot(_character.Look.EyePosition, _character.Look.AimDirection);
            KickView(weapon, item, now);
            OwnerShotFired?.Invoke();
        }

        /// <summary>Owner: kicks the camera by the next step of the weapon's recoil pattern.</summary>
        private void KickView(WeaponDefinition weapon, in ItemStack item, double now)
        {
            int shot = RecoilPattern.NextShotIndex(_recoilShot, _lastRecoilShotTime, now, weapon.RecoilResetSeconds);
            if (shot == 0) _recoilJitterSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            _recoilShot = shot;
            _lastRecoilShotTime = now;

            WeaponStats stats = WeaponStats.Of(weapon, item, _equipment.Container.ItemDatabase);
            RecoilKick kick = RecoilPattern.KickFor(shot, weapon.Recoil, _recoilJitterSeed) * _aim.CurrentRecoilScale(stats);
            // Recovery waits out the next shot of a full-auto spray, so it settles once the trigger is released;
            // capped so slow semi-auto and bolt-action weapons settle between shots.
            _character.Look.AddRecoil(kick, weapon.RecoilRecovery, Mathf.Min(weapon.SecondsBetweenShots * 1.25f, MaxRecoveryDelay));
        }

        [ServerRpc]
        private void RequestShot(Vector3 origin, Vector3 direction)
        {
            if (!_character.Health.IsAlive || _reloader.IsReloading || IsBusy) return;
            if (!_equipment.TryGetActiveWeapon(out EquipmentSlot slot, out WeaponDefinition weapon, out ItemStack item)) return;
            if (item.AmmoCount <= 0 || !_equipment.TryGetAmmo(item.AmmoId, out AmmoDefinition ammo) || !weapon.Accepts(ammo)) return;

            double now = Time.timeAsDouble;
            if (now < _nextServerShotTime - weapon.FireRateTolerance) return;

            if (!IsFinite(direction) || direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();

            Vector3 eye = _character.Look.EyePosition;
            if (!IsFinite(origin) || Vector3.Distance(origin, eye) > weapon.MaxOriginError)
                origin = eye;

            _nextServerShotTime = now + weapon.SecondsBetweenShots;
            _equipment.ServerSetLoadedAmmo(slot, item.AmmoId, item.AmmoCount - 1);

            // The server's own aim and movement state, the same model the owner's crosshair shows.
            WeaponStats stats = WeaponStats.Of(weapon, item, _equipment.Container.ItemDatabase);
            float spread = _aim.CurrentSpread(stats, ammo);
            var ends = new Vector3[ammo.Projectiles];
            for (int i = 0; i < ends.Length; i++)
                ends[i] = ServerFireProjectile(weapon, ammo, origin, HitscanQuery.ApplySpread(direction, spread));

            BroadcastShot(origin, ends);
            ShotNoise.ServerReport(origin,
                Audio.ProceduralAudio.GunRange(Audio.ProceduralAudio.ClassOf(weapon)) * 0.4f * stats.SoundRangeMultiplier, OwnerId);
        }

        /// <summary>Server: one projectile along its drop curve. Returns where it ended.</summary>
        private Vector3 ServerFireProjectile(WeaponDefinition weapon, AmmoDefinition ammo, Vector3 origin, Vector3 direction)
        {
            if (!BallisticTrace.TryTrace(origin, direction, weapon.MuzzleVelocityWith(ammo), weapon.Range, weapon.HitMask, transform,
                    _hitBuffer, out RaycastHit hit, out Hitbox hitbox, out Vector3 end, out Vector3 heading))
                return end;

            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                HitZone zone = hitbox != null ? hitbox.Zone : HitZone.Body;
                float multiplier = hitbox != null ? hitbox.DamageMultiplier : 1f;
                int damage = Mathf.Max(1, Mathf.RoundToInt(ammo.Damage * multiplier));
                target.ApplyDamage(new DamageInfo(damage, OwnerId, hit.point, heading, zone,
                    ammo.Penetration, ammo.ArmorDamagePercent));
            }
            return hit.point;
        }

        [ObserversRpc]
        private void BroadcastShot(Vector3 origin, Vector3[] ends)
        {
            if (ends == null) return;
            foreach (Vector3 end in ends)
                ShotConfirmed?.Invoke(origin, end);
        }

        private void ResetPrediction() => _unconfirmedShots = 0;

        public WeaponDefinition FiringWeapon => _equipment.ActiveWeapon;

        public float FiringSoundMultiplier => _equipment.TryGetActiveWeaponStats(out WeaponStats stats) ? stats.SoundRangeMultiplier : 1f;

        /// <summary>Hands are busy: treating a wound or sprinting with the weapon lowered.</summary>
        private bool IsBusy =>
            (_character.Injuries != null && _character.Injuries.IsTreating) || (_motor != null && _motor.IsSprinting);

        private static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
