using System;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// A networked, server-authoritative set of item slots: equipment, a bag's storage, a crate or a corpse.
    /// Only the server changes contents (through <see cref="ServerInventory"/>); clients read the
    /// replicated slots and ask for changes through <see cref="PlayerInventory"/>.
    /// </summary>
    /// <remarks>
    /// Contents replicate to all observers. A modified client could therefore read other players' bags;
    /// acceptable for now, revisit with per-owner visibility before any public release.
    /// The container is itself the <see cref="ISlotStore"/> behind <see cref="ServerInventory"/>: FishNet only
    /// allows SyncTypes to be referenced from NetworkBehaviours, so no separate adapter class holds the SyncList.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ItemContainer : NetworkBehaviour, ISlotStore
    {
        [SerializeField] private string _displayName = "Container";
        [SerializeField, Min(1)] private int _capacity = 12;
        [SerializeField] private ContainerAccess _access = ContainerAccess.World;
        [SerializeField] private ContainerSlotRules _slotRules = ContainerSlotRules.Any;

        [Tooltip("World containers: max distance from the player's eyes to the container's collider.")]
        [SerializeField, Min(0.5f)] private float _accessRange = 3f;

        [SerializeField] private ItemDatabase _itemDatabase;

        private readonly SyncList<ItemStack> _slots = new SyncList<ItemStack>();
        private readonly SyncVar<string> _replicatedName = new SyncVar<string>();

        private SlotInventory _serverInventory;
        private Collider _accessCollider;

        /// <summary>Raised once per client whenever any slot changes.</summary>
        public event Action Changed;

        /// <summary>Server only. Raised whenever any slot changes.</summary>
        public event Action ServerChanged;

        public string DisplayName => string.IsNullOrEmpty(_replicatedName.Value) ? _displayName : _replicatedName.Value;
        public int Capacity => _capacity;
        public ContainerAccess Access => _access;
        public ContainerSlotRules SlotRules => _slotRules;
        public ItemDatabase ItemDatabase => _itemDatabase;

        /// <summary>Replicated slot count (0 until the first sync arrives on clients).</summary>
        public int SlotCount => _slots.Count;

        public ItemStack GetSlot(int index) =>
            index >= 0 && index < _slots.Count ? _slots[index] : ItemStack.Empty;

        /// <summary>Server only: inventory rules bound to the replicated slots. Null on clients.</summary>
        public SlotInventory ServerInventory
        {
            get
            {
                if (!IsServerInitialized) return null;
                EnsureServerSlots();
                return _serverInventory;
            }
        }

        private void Awake()
        {
            _accessCollider = GetComponentInChildren<Collider>();
            _slots.OnChange += OnSlotsChanged;

            if (_itemDatabase == null)
                Debug.LogError($"[{nameof(ItemContainer)}] Item database is not assigned on {name}.", this);
        }

        private void OnDestroy()
        {
            _slots.OnChange -= OnSlotsChanged;
        }

        /// <summary>Server only, before spawning: sets capacity and name (bag storage created at runtime).</summary>
        public void ServerConfigure(int capacity, string displayName)
        {
            if (IsSpawned)
            {
                Debug.LogError($"[{nameof(ItemContainer)}] Configure must be called before the container is spawned.", this);
                return;
            }

            _capacity = Mathf.Max(1, capacity);
            _displayName = displayName;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            EnsureServerSlots();
            _replicatedName.Value = _displayName;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();

            // Items carried inside this container leave the game with it: despawn bag storage they own.
            NetworkManager manager = NetworkManager;
            for (int i = 0; i < _slots.Count; i++)
                ItemFactory.ServerDestroy(manager, _slots[i]);
        }

        /// <summary>Server only: whether a living player is close enough to use this world container.</summary>
        public bool ServerIsWithinReach(PlayerCharacter player)
        {
            if (!IsServerInitialized || _access != ContainerAccess.World) return false;
            if (player == null || !player.IsSpawned || !player.Health.IsAlive) return false;

            Vector3 eye = player.Look.EyePosition;
            Vector3 closest = _accessCollider != null ? _accessCollider.ClosestPoint(eye) : transform.position;
            return (closest - eye).sqrMagnitude <= _accessRange * _accessRange;
        }

        /// <summary>Finds the storage container carried by a bag item, on the server or a client.</summary>
        public static bool TryResolveContents(NetworkManager manager, in ItemStack stack, out ItemContainer storage)
        {
            storage = null;
            if (!stack.HasContents || manager == null) return false;

            NetworkObject networkObject = null;
            bool found = manager.IsServerStarted
                ? manager.ServerManager.Objects.Spawned.TryGetValue(stack.ContentsObjectId, out networkObject)
                : manager.ClientManager.Objects.Spawned.TryGetValue(stack.ContentsObjectId, out networkObject);

            if (found && networkObject != null) storage = networkObject.GetComponent<ItemContainer>();
            return storage != null;
        }

        private void EnsureServerSlots()
        {
            if (_serverInventory != null) return;

            _slots.Clear();
            for (int i = 0; i < _capacity; i++)
                _slots.Add(ItemStack.Empty);

            _serverInventory = new SlotInventory(
                this, _itemDatabase, SlotFilters.Create(_slotRules, _itemDatabase));
        }

        // ---- ISlotStore (server writes straight into the replicated list) ----

        int ISlotStore.Count => _slots.Count;

        ItemStack ISlotStore.this[int index]
        {
            get => _slots[index];
            set
            {
                // Only write real changes, to avoid redundant network traffic.
                if (_slots[index] != value) _slots[index] = value;
            }
        }

        private void OnSlotsChanged(SyncListOperation op, int index, ItemStack oldItem, ItemStack newItem, bool asServer)
        {
            // FishNet raises this once with asServer = true on the server and once with false on clients
            // (both on a host), so each event fires exactly once per side.
            if (asServer) ServerChanged?.Invoke();
            else Changed?.Invoke();
        }
    }
}
