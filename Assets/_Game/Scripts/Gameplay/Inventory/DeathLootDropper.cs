using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// Server only: the extraction rule. On death every equipped item and everything in the pockets moves into a
    /// lootable corpse at the death position. Bags keep their contents; armor keeps its durability.
    /// Kept slots (the knife) stay on the character for the session to take back; the safe box is never on the
    /// character at all, so it cannot be looted.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter), typeof(PlayerEquipment))]
    public sealed class DeathLootDropper : NetworkBehaviour
    {
        [Tooltip("Networked prefab with an ItemContainer (access: World), a grid big enough for a full kit.")]
        [SerializeField] private ItemContainer _corpsePrefab;

        private PlayerCharacter _character;
        private PlayerEquipment _equipment;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _equipment = GetComponent<PlayerEquipment>();

            if (_corpsePrefab == null)
                Debug.LogError($"[{nameof(DeathLootDropper)}] Corpse prefab is not assigned on {name}.", this);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _character.Health.ServerDied += OnServerDied;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _character.Health.ServerDied -= OnServerDied;
        }

        private void OnServerDied(DamageInfo damage)
        {
            SlotInventory gear = _equipment.Container != null ? _equipment.Container.ServerInventory : null;
            SlotInventory pockets = _equipment.Pockets != null ? _equipment.Pockets.ServerInventory : null;
            if (gear == null || _corpsePrefab == null) return;
            if (!HasDroppableGear(gear) && (pockets == null || pockets.IsEmpty)) return;

            ItemContainer corpse = Instantiate(_corpsePrefab, transform.position, transform.rotation);
            Spawn(corpse.NetworkObject);
            SlotInventory body = corpse.ServerInventory;

            // Equipment first (the slots that survive death stay on the character), then pockets.
            bool allMoved = true;
            for (int slot = 0; slot < gear.Capacity; slot++)
            {
                if (gear[slot].IsEmpty || EquipmentRules.IsKeptOnDeath((EquipmentSlot)slot)) continue;
                if (gear.TransferTo(body, slot) != InventoryResult.Success) allMoved = false;
            }
            if (pockets != null && !pockets.TransferAllTo(body)) allMoved = false;

            if (!allMoved)
                Debug.LogWarning($"[{nameof(DeathLootDropper)}] Corpse could not hold all gear; the rest is destroyed.", this);

            for (int slot = 0; slot < gear.Capacity; slot++)
            {
                if (gear[slot].IsEmpty || EquipmentRules.IsKeptOnDeath((EquipmentSlot)slot)) continue;
                ItemFactory.ServerDestroy(NetworkManager, gear[slot]);
                gear.TakeFromSlot(slot, gear[slot].Quantity);
            }
            pockets?.Clear();
        }

        private static bool HasDroppableGear(SlotInventory gear)
        {
            for (int slot = 0; slot < gear.Capacity; slot++)
                if (!gear[slot].IsEmpty && !EquipmentRules.IsKeptOnDeath((EquipmentSlot)slot)) return true;
            return false;
        }
    }
}
