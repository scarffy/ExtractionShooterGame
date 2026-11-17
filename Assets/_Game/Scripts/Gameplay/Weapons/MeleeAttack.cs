using System;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>
    /// Knife attack (V / right bumper) with the item in the melee slot. The owner asks; the server checks the player
    /// is alive, has a knife, the swing is off cooldown and the claimed eye position is close to its own, then hits the
    /// first hitbox within the knife's reach with the knife's damage and penetration (armor still applies).
    /// Everyone is told about the swing for presentation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter), typeof(PlayerEquipment))]
    public sealed class MeleeAttack : NetworkBehaviour
    {
        private const int HitBufferSize = 16;
        private const float MaxOriginError = 1.5f;

        [Tooltip("Layers a swing can hit.")]
        [SerializeField] private LayerMask _hitMask = ~(1 << 2);

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HitBufferSize];

        private PlayerCharacter _character;
        private PlayerEquipment _equipment;
        private double _nextLocalSwing;
        private double _nextServerSwing;

        /// <summary>All peers: a swing happened (origin, direction, whether it hit something that takes damage).</summary>
        public event Action<Vector3, Vector3, bool> Swung;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _equipment = GetComponent<PlayerEquipment>();
        }

        private void Update()
        {
            if (!IsOwner || !_character.Health.IsAlive || !_character.Input.MeleePressed || IsTreating) return;
            if (!_equipment.TryGetDefinition(EquipmentSlot.Melee, out MeleeDefinition knife)) return;

            double now = Time.timeAsDouble;
            if (now < _nextLocalSwing) return;
            _nextLocalSwing = now + knife.Cooldown;
            RequestSwing(_character.Look.EyePosition, _character.Look.AimDirection);
        }

        [ServerRpc]
        private void RequestSwing(Vector3 origin, Vector3 direction)
        {
            if (!_character.Health.IsAlive || IsTreating || !_equipment.TryGetDefinition(EquipmentSlot.Melee, out MeleeDefinition knife)) return;

            double now = Time.timeAsDouble;
            if (now < _nextServerSwing - 0.05) return;
            _nextServerSwing = now + knife.Cooldown;

            if (!IsFinite(direction) || direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();
            Vector3 eye = _character.Look.EyePosition;
            if (!IsFinite(origin) || Vector3.Distance(origin, eye) > MaxOriginError) origin = eye;

            bool damaged = false;
            if (HitscanQuery.TryGetFirstHit(origin, direction, knife.Range, _hitMask, transform, _hitBuffer, out RaycastHit hit, out Hitbox hitbox))
            {
                IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
                if (target != null)
                {
                    HitZone zone = hitbox != null ? hitbox.Zone : HitZone.Body;
                    float multiplier = hitbox != null ? hitbox.DamageMultiplier : 1f;
                    int damage = Mathf.Max(1, Mathf.RoundToInt(knife.Damage * multiplier));
                    target.ApplyDamage(new DamageInfo(damage, OwnerId, hit.point, direction, zone, knife.Penetration, knife.ArmorDamagePercent));
                    damaged = true;
                }
            }
            BroadcastSwing(origin, direction, damaged);
        }

        private bool IsTreating => _character.Injuries != null && _character.Injuries.IsTreating;

        [ObserversRpc]
        private void BroadcastSwing(Vector3 origin, Vector3 direction, bool hit) => Swung?.Invoke(origin, direction, hit);

        private static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
