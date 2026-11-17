using System;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Core.Throwables;
using ExtractionShooter.Gameplay.Input;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Throwables
{
    /// <summary>
    /// Grenades for a player. B cycles the selected type (frag, smoke, flash) among those carried; G throws one.
    /// The owner asks with its eye position and aim; the server checks the player is alive, not treating and off
    /// cooldown, that the claimed eye is close to its own, then uses up one grenade of that type from the rig,
    /// pockets or backpack and spawns a <see cref="ThrowableProjectile"/>. The server also tells this player's owner
    /// when a flash goes off in view (<see cref="Flashed"/>), and the owner works out how hard it hits from where it
    /// is actually looking.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter), typeof(PlayerEquipment))]
    public sealed class PlayerThrowables : NetworkBehaviour
    {
        private const float MaxOriginError = 1.5f;
        private const int HitBufferSize = 8;

        [Tooltip("Networked grenade spawned for every throw (must be in the spawnable prefabs).")]
        [SerializeField] private ThrowableProjectile _projectilePrefab;

        [Tooltip("Seconds between throws.")]
        [SerializeField, Min(0.1f)] private float _cooldown = 1f;

        [Tooltip("Distance in front of the eyes where the grenade appears (metres).")]
        [SerializeField, Min(0.1f)] private float _spawnDistance = 0.6f;

        [Tooltip("Extra upward speed so throws arc (m/s).")]
        [SerializeField, Min(0f)] private float _upwardSpeed = 2.5f;

        [Tooltip("Layers that stop a grenade appearing inside a wall in front of the player.")]
        [SerializeField] private LayerMask _blockMask = ~(1 << 2);

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HitBufferSize];
        private readonly int[] _counts = new int[ThrowableMath.KindCount];

        private PlayerCharacter _character;
        private PlayerEquipment _equipment;
        private ThrowableKind _selected;
        private double _nextLocalThrow;
        private double _nextServerThrow;

        /// <summary>Owner: a flash went off in view (white-out seconds, intensity 0..1).</summary>
        public event Action<float, float> Flashed;

        /// <summary>Owner: the grenade type G throws.</summary>
        public ThrowableKind Selected => _selected;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _equipment = GetComponent<PlayerEquipment>();

            if (_projectilePrefab == null)
                Debug.LogError($"[{nameof(PlayerThrowables)}] Projectile prefab is not assigned on {name}.", this);
        }

        private void Update()
        {
            if (!IsOwner || !_character.Health.IsAlive) return;
            IPlayerInput input = _character.Input;

            if (input.CycleThrowablePressed)
            {
                CountCarried(_counts);
                _selected = ThrowableMath.NextKind(_selected, _counts);
            }

            if (!input.ThrowPressed || IsTreating) return;
            double now = Time.timeAsDouble;
            if (now < _nextLocalThrow || CountCarried(_selected) == 0) return;

            _nextLocalThrow = now + _cooldown;
            ServerThrow((byte)_selected, _character.Look.EyePosition, _character.Look.AimDirection);
        }

        /// <summary>How many grenades of a type the player carries (rig, pockets, backpack). Works on every peer.</summary>
        public int CountCarried(ThrowableKind kind)
        {
            CountCarried(_counts);
            return _counts[(int)kind];
        }

        /// <summary>Fills <paramref name="counts"/> (indexed by <see cref="ThrowableKind"/>) with the grenades carried.</summary>
        public void CountCarried(int[] counts)
        {
            Array.Clear(counts, 0, counts.Length);
            ItemDatabase database = _equipment.Container != null ? _equipment.Container.ItemDatabase : null;
            if (database == null) return;

            foreach (ItemContainer storage in _equipment.CarriedStorage)
            {
                if (storage == null) continue;
                for (int i = 0; i < storage.SlotCount; i++)
                {
                    ItemStack stack = storage.GetSlot(i);
                    if (stack.IsEmpty || !database.TryGet(stack.ItemId, out ItemDefinition definition)) continue;
                    if (definition is ThrowableDefinition throwable && (int)throwable.Kind < counts.Length)
                        counts[(int)throwable.Kind] += stack.Quantity;
                }
            }
        }

        // ---------------- Server ----------------

        [ServerRpc]
        private void ServerThrow(byte kind, Vector3 origin, Vector3 direction)
        {
            if (!_character.Health.IsAlive || IsTreating || _projectilePrefab == null || kind >= ThrowableMath.KindCount) return;

            double now = Time.timeAsDouble;
            if (now < _nextServerThrow - 0.05) return;
            if (!IsFinite(direction) || direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();

            Vector3 eye = _character.Look.EyePosition;
            if (!IsFinite(origin) || Vector3.Distance(origin, eye) > MaxOriginError) origin = eye;

            if (!ServerTakeOne((ThrowableKind)kind, out ThrowableDefinition definition)) return;
            _nextServerThrow = now + _cooldown;

            Vector3 velocity = direction * definition.ThrowSpeed + Vector3.up * _upwardSpeed;
            ThrowableProjectile projectile = Instantiate(_projectilePrefab, SpawnPoint(origin, direction), Quaternion.LookRotation(direction));
            projectile.ServerPrepare(definition, OwnerId, velocity);
            Spawn(projectile.NetworkObject);
        }

        /// <summary>Server: a flash went off within reach and in sight of this player; the owner works out the rest.</summary>
        public void ServerFlash(Vector3 position, ThrowableDefinition flash)
        {
            if (!IsServerInitialized || flash == null || !_character.Health.IsAlive || Owner == null || !Owner.IsValid) return;
            TargetFlash(Owner, position, flash.Id);
        }

        [TargetRpc]
        private void TargetFlash(NetworkConnection connection, Vector3 position, string flashId)
        {
            ItemDatabase database = _equipment.Container != null ? _equipment.Container.ItemDatabase : null;
            if (database == null || !database.TryGet(flashId, out ItemDefinition found) || !(found is ThrowableDefinition flash)) return;

            Vector3 eye = _character.Look.EyePosition;
            Vector3 toFlash = position - eye;
            float intensity = flash.FlashIntensity(toFlash.magnitude, Vector3.Angle(_character.Look.AimDirection, toFlash));
            float seconds = flash.FlashSeconds(intensity);
            if (seconds > 0f) Flashed?.Invoke(seconds, intensity);
        }

        /// <summary>Server: removes one grenade of the kind from the rig, pockets, then backpack.</summary>
        private bool ServerTakeOne(ThrowableKind kind, out ThrowableDefinition definition)
        {
            definition = null;
            ItemDatabase database = _equipment.Container.ItemDatabase;
            if (database == null) return false;

            foreach (ItemContainer storage in _equipment.CarriedStorage)
            {
                SlotInventory inventory = storage != null ? storage.ServerInventory : null;
                if (inventory == null) continue;

                for (int i = 0; i < inventory.Capacity; i++)
                {
                    ItemStack stack = inventory[i];
                    if (stack.IsEmpty || !database.TryGet(stack.ItemId, out ItemDefinition found)) continue;
                    if (!(found is ThrowableDefinition throwable) || throwable.Kind != kind) continue;
                    if (inventory.TakeFromSlot(i, 1).IsEmpty) continue;

                    definition = throwable;
                    return true;
                }
            }
            return false;
        }

        /// <summary>In front of the eyes, pulled back when a wall is closer than that.</summary>
        private Vector3 SpawnPoint(Vector3 origin, Vector3 direction)
        {
            float distance = _spawnDistance;
            int count = Physics.RaycastNonAlloc(origin, direction, _hitBuffer, _spawnDistance + 0.15f, _blockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hitBuffer[i];
                if (hit.collider.transform.IsChildOf(transform)) continue;
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.15f));
            }
            return origin + direction * distance;
        }

        private bool IsTreating => _character.Injuries != null && _character.Injuries.IsTreating;

        private static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
