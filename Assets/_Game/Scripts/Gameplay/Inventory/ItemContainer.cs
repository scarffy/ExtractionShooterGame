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

        [Tooltip("0: plain slots (equipment). Otherwise a grid this many cells wide; capacity must be whole rows.")]
        [SerializeField, Min(0)] private int _gridWidth;
        [SerializeField] private ContainerAccess _access = ContainerAccess.World;
        [SerializeField] private ContainerSlotRules _slotRules = ContainerSlotRules.Any;

        [Tooltip("World containers: max distance from the player's eyes to the container's collider.")]
        [SerializeField, Min(0.5f)] private float _accessRange = 3f;

        [SerializeField] private ItemDatabase _itemDatabase;

        private readonly SyncList<ItemStack> _slots = new SyncList<ItemStack>();
        private readonly SyncVar<string> _replicatedName = new SyncVar<string>();
        private readonly SyncVar<int> _replicatedGridWidth = new SyncVar<int>();

        private SlotInventory _serverInventory;
        private SlotInventory _layout;
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

        /// <summary>Cells per row, or 0 for plain slots. Replicated (bag storage is configured at runtime).</summary>
        public int GridWidth => !IsServerInitialized && IsClientInitialized ? _replicatedGridWidth.Value : _gridWidth;

        public bool IsGrid => GridWidth > 0;

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

        /// <summary>Server only, before spawning: sets capacity, name and grid width (bag storage created at runtime).</summary>
        public void ServerConfigure(int capacity, string displayName, int gridWidth = 0)
        {
            if (IsSpawned)
            {
                Debug.LogError($"[{nameof(ItemContainer)}] Configure must be called before the container is spawned.", this);
                return;
            }

            _capacity = Mathf.Max(1, capacity);
            _displayName = displayName;
            _gridWidth = gridWidth > 0 && _capacity % gridWidth == 0 ? gridWidth : 0;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            EnsureServerSlots();
            _replicatedName.Value = _displayName;
            _replicatedGridWidth.Value = _gridWidth;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();

            // Items carried inside this container leave the game with it: despawn bag storage they own.
            NetworkManager manager = NetworkManager;
            for (int i = 0; i < _slots.Count; i++)
                ItemFactory.ServerDestroy(manager, _slots[i]);
        }

        /// <summary>
        /// Server only, while spawned: grows the container to a new capacity and grid width (safe box upgrades).
        /// Items keep their column and row. Refused (false) when an item would no longer fit.
        /// </summary>
        public bool ServerResize(int capacity, int gridWidth)
        {
            SlotInventory current = ServerInventory;
            if (current == null || capacity < 1 || gridWidth < 0 || (gridWidth > 0 && capacity % gridWidth != 0)) return false;
            if (capacity == _capacity && gridWidth == _gridWidth) return true;

            int oldWidth = Mathf.Max(1, current.IsGrid ? current.GridWidth : _capacity);
            var moved = new ItemStack[capacity];
            for (int i = 0; i < _slots.Count; i++)
            {
                ItemStack stack = _slots[i];
                if (stack.IsEmpty) continue;

                int x = i % oldWidth, y = i / oldWidth;
                int width = gridWidth > 0 ? gridWidth : capacity;
                if (x >= width) return false;
                int index = y * width + x;
                if (index >= capacity) return false;
                moved[index] = stack;
            }

            var resized = new SlotInventory(new ArraySlotStore(capacity), _itemDatabase, SlotFilters.Create(_slotRules, _itemDatabase), gridWidth);
            for (int i = 0; i < capacity; i++)
                if (!moved[i].IsEmpty && resized.TryPlace(i, moved[i]) != InventoryResult.Success) return false;

            _capacity = capacity;
            _gridWidth = gridWidth;
            _slots.Clear();
            for (int i = 0; i < capacity; i++) _slots.Add(moved[i]);
            _serverInventory = new SlotInventory(this, _itemDatabase, SlotFilters.Create(_slotRules, _itemDatabase), _gridWidth);
            _layout = null;
            _replicatedGridWidth.Value = _gridWidth;
            return true;
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

            if (_gridWidth > 0 && _capacity % _gridWidth != 0)
            {
                Debug.LogError($"[{nameof(ItemContainer)}] {name}: capacity {_capacity} is not whole rows of {_gridWidth}; using plain slots.", this);
                _gridWidth = 0;
            }

            _serverInventory = new SlotInventory(
                this, _itemDatabase, SlotFilters.Create(_slotRules, _itemDatabase), _gridWidth);
        }

        /// <summary>
        /// Read-only rules view on either side: grid geometry for UI (which cell belongs to which item, where an item
        /// could go). Never change contents through it on a client; the server uses <see cref="ServerInventory"/>.
        /// Null until slots have replicated.
        /// </summary>
        public SlotInventory Layout
        {
            get
            {
                if (IsServerInitialized) return ServerInventory;
                int width = GridWidth;
                int count = _slots.Count;
                if (count == 0 || _itemDatabase == null || (width > 0 && count % width != 0)) return null;
                if (_layout == null || _layout.Capacity != count || _layout.GridWidth != width)
                    _layout = new SlotInventory(this, _itemDatabase, SlotFilters.Create(_slotRules, _itemDatabase), width);
                return _layout;
            }
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
