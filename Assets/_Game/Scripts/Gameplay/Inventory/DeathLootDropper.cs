using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// Server only: the extraction rule. On death every equipped item moves into a lootable corpse
    /// at the death position. Bags keep their contents; armor keeps its durability.
    /// The player respawns with only the starting loadout.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter), typeof(PlayerEquipment))]
    public sealed class DeathLootDropper : NetworkBehaviour
    {
        [Tooltip("Networked prefab with an ItemContainer (access: World), at least 7 slots.")]
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
            if (gear == null || gear.IsEmpty || _corpsePrefab == null) return;

            ItemContainer corpse = Instantiate(_corpsePrefab, transform.position, transform.rotation);
            Spawn(corpse.NetworkObject);

            if (!gear.TransferAllTo(corpse.ServerInventory))
            {
                Debug.LogWarning($"[{nameof(DeathLootDropper)}] Corpse could not hold all gear; the rest is destroyed.", this);
                for (int i = 0; i < gear.Capacity; i++)
                    ItemFactory.ServerDestroy(NetworkManager, gear[i]);
            }

            gear.Clear();
        }
    }
}
