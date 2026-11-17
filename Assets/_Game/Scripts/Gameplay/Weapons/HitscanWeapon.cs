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
    /// Fires the player's drawn weapon as a hitscan. The owner only requests a shot; the server validates
    /// it (alive, not reloading, a round is loaded, fire rate, origin, direction), spends the round,
    /// applies spread per projectile, raycasts against hitboxes, applies damage with the round's
    /// penetration and broadcasts the result for presentation.
    /// Pulling the trigger on an empty magazine asks for a reload.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter), typeof(PlayerEquipment), typeof(WeaponReloader))]
    public sealed class HitscanWeapon : NetworkBehaviour, IShotSource
    {
        private const int HitBufferSize = 16;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HitBufferSize];

        private PlayerCharacter _character;
        private PlayerEquipment _equipment;
        private WeaponReloader _reloader;
        private double _nextLocalShotTime;
        private double _nextServerShotTime;

        // Shots sent but not yet reflected in the replicated round count.
        private int _unconfirmedShots;

        /// <summary>Client side. Raised for every confirmed projectile (origin, end point).</summary>
        public event Action<Vector3, Vector3> ShotConfirmed;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _equipment = GetComponent<PlayerEquipment>();
            _reloader = GetComponent<WeaponReloader>();
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
            if (!IsOwner || !_character.Health.IsAlive) return;
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
        }

        [ServerRpc]
        private void RequestShot(Vector3 origin, Vector3 direction)
        {
            if (!_character.Health.IsAlive || _reloader.IsReloading) return;
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

            float spread = weapon.Spread + ammo.ExtraSpread;
            var ends = new Vector3[ammo.Projectiles];
            for (int i = 0; i < ends.Length; i++)
                ends[i] = ServerFireProjectile(weapon, ammo, origin, HitscanQuery.ApplySpread(direction, spread));

            BroadcastShot(origin, ends);
        }

        /// <summary>Server: one projectile. Returns where it ended.</summary>
        private Vector3 ServerFireProjectile(WeaponDefinition weapon, AmmoDefinition ammo, Vector3 origin, Vector3 direction)
        {
            if (!HitscanQuery.TryGetFirstHit(origin, direction, weapon.Range, weapon.HitMask, transform, _hitBuffer, out RaycastHit hit, out Hitbox hitbox))
                return origin + direction * weapon.Range;

            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                HitZone zone = hitbox != null ? hitbox.Zone : HitZone.Body;
                float multiplier = hitbox != null ? hitbox.DamageMultiplier : 1f;
                int damage = Mathf.Max(1, Mathf.RoundToInt(ammo.Damage * multiplier));
                target.ApplyDamage(new DamageInfo(damage, OwnerId, hit.point, direction, zone,
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

        private static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
