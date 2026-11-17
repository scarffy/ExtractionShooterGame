using System;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>
    /// Timed reload of the drawn weapon (R). The server starts the reload only when it would change
    /// something, waits the weapon's reload time, then loads from the rig and backpack through
    /// <see cref="PlayerEquipment.ServerTryReload"/>. Switching weapon, moving the weapon or dying cancels it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter), typeof(PlayerEquipment))]
    public sealed class WeaponReloader : NetworkBehaviour
    {
        private const byte NotReloading = byte.MaxValue;

        private readonly SyncVar<byte> _reloadingSlot = new SyncVar<byte>(NotReloading);

        private PlayerCharacter _character;
        private PlayerEquipment _equipment;

        private double _serverCompleteTime;
        private string _serverWeaponId;

        private float _clientStartTime;
        private float _clientDuration;

        /// <summary>Client side. Raised when a reload starts or ends.</summary>
        public event Action ReloadStateChanged;

        public bool IsReloading => _reloadingSlot.Value != NotReloading;

        /// <summary>Client side: 0..1 progress of the current reload (1 when not reloading).</summary>
        public float Progress =>
            !IsReloading || _clientDuration <= 0f ? 1f : Mathf.Clamp01((Time.time - _clientStartTime) / _clientDuration);

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _equipment = GetComponent<PlayerEquipment>();
            _reloadingSlot.OnChange += OnReloadingSlotChanged;
        }

        private void OnDestroy()
        {
            _reloadingSlot.OnChange -= OnReloadingSlotChanged;
        }

        private void Update()
        {
            if (IsOwner && _character.Input.ReloadPressed) Request();
            if (IsServerInitialized && IsReloading) ServerTick();
        }

        /// <summary>Owner: asks the server to reload the drawn weapon.</summary>
        public void Request()
        {
            if (!IsOwner || IsReloading || !_character.Health.IsAlive) return;
            ServerRequestReload();
        }

        [ServerRpc]
        private void ServerRequestReload()
        {
            if (IsReloading || !_character.Health.IsAlive) return;
            if (!_equipment.TryGetActiveWeapon(out EquipmentSlot slot, out WeaponDefinition weapon, out ItemStack item)) return;
            if (!_equipment.ServerCanReload(slot)) return;

            _serverWeaponId = item.ItemId;
            _serverCompleteTime = Time.timeAsDouble + weapon.ReloadSeconds;
            _reloadingSlot.Value = (byte)slot;
        }

        private void ServerTick()
        {
            var slot = (EquipmentSlot)_reloadingSlot.Value;
            bool stillDrawn = _character.Health.IsAlive &&
                              _equipment.ActiveWeaponSlot == slot &&
                              _equipment.GetItem(slot).ItemId == _serverWeaponId;

            if (stillDrawn && Time.timeAsDouble < _serverCompleteTime) return;

            if (stillDrawn) _equipment.ServerTryReload(slot);
            _reloadingSlot.Value = NotReloading;
            _serverWeaponId = null;
        }

        private void OnReloadingSlotChanged(byte previous, byte current, bool asServer)
        {
            if (asServer) return;

            if (current != NotReloading && _equipment.TryGetDefinition((EquipmentSlot)current, out WeaponDefinition weapon))
            {
                _clientStartTime = Time.time;
                _clientDuration = weapon.ReloadSeconds;
            }
            ReloadStateChanged?.Invoke();
        }
    }
}
