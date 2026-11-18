using ExtractionShooter.Core.Inventory;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// Server only: fills the container on this object from a loot table when it spawns.
    /// Items are created through <see cref="ItemFactory"/> so armor and bags get their state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ItemContainer))]
    public sealed class LootSpawner : NetworkBehaviour
    {
        [SerializeField] private LootTable _lootTable;

        private ItemContainer _container;

        private void Awake()
        {
            _container = GetComponent<ItemContainer>();

            if (_lootTable == null)
                Debug.LogError($"[{nameof(LootSpawner)}] Loot table is not assigned on {name}.", this);
        }

        /// <summary>The table this container is filled from.</summary>
        public LootTable LootTable => _lootTable;

        /// <summary>
        /// Server, before spawning: fills this container from another table (world loot points use it for
        /// high-tier and medical crates). Ignored once spawned (it is already filled) or for a null table.
        /// </summary>
        public void ServerOverrideLootTable(LootTable table)
        {
            if (table == null) return;
            if (IsSpawned)
            {
                Debug.LogError($"[{nameof(LootSpawner)}] The loot table must be set before {name} is spawned.", this);
                return;
            }
            _lootTable = table;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_lootTable == null) return;

            LootFiller.ServerFill(NetworkManager, _lootTable, _container.ServerInventory,
                new System.Random(unchecked(System.Environment.TickCount * 31 + ObjectId)));
        }
    }
}
