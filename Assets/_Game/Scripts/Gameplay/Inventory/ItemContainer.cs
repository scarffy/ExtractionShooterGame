using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// A networked, server-authoritative set of item slots: equipment, a bag's storage, a crate or a corpse.
    /// Only the server changes contents (through <see cref="ServerInventory"/>); clients read their copy and ask for
    /// changes through <see cref="PlayerInventory"/> (raid) or the player session (hideout).
    /// <para><b>Replication rule.</b> Contents are sent only to peers allowed to see them, so a modified client never
    /// receives other players' bags, stashes or unopened crates:</para>
    /// <list type="bullet">
    /// <item><see cref="ContainerVisibility.Public"/>, a player's worn equipment only: every observer gets the slots
    /// (other clients draw the worn gear and the drawn weapon). Bags worn there stay private: see below.</item>
    /// <item><see cref="ContainerVisibility.Private"/>, everything else: the <em>contents owner</em> plus peers granted
    /// a view. The contents owner is the owner of this network object (the session's stash, loadout, pockets and safe
    /// box; the character's pockets), or, for a bag's storage, the contents owner of the container the bag lies in. So a
    /// rig or backpack follows whoever carries it (worn, in the loadout, in the stash) and has no owner in a crate or on
    /// a corpse. <see cref="PlayerInventory"/> grants a view while its player has a world container, or a bag lying in
    /// it, open (<see cref="ServerAddViewer"/> / <see cref="ServerRemoveViewer"/>).</item>
    /// </list>
    /// A peer joining the audience gets a full copy (late joiners and re-spawns too, see <see cref="OnSpawnServer"/>);
    /// changes are batched per tick and sent as absolute slot values; a peer leaving it is told to drop its copy, so
    /// <see cref="SlotCount"/> is 0 and <see cref="Layout"/> null there again. A bag's contents are sent before the slot
    /// change that shows the bag. Display name and grid width are public SyncVars (they reveal nothing carried).
    /// </summary>
    /// <remarks>
    /// The container is itself the <see cref="ISlotStore"/> behind <see cref="ServerInventory"/>, so every server write
    /// passes through here: that is where bags moving in and out are noticed (their storage's holder, which decides its
    /// audience) and changes are queued. Server code keeps using <see cref="ServerInventory"/>, <see cref="GetSlot"/>
    /// and <see cref="ServerChanged"/> as before; on a host <see cref="GetSlot"/> reads the server's slots.
    /// Bag storage is spawned server-owned (<see cref="ItemFactory"/>): visibility follows the holder, not FishNet
    /// ownership, so storage is never despawned with a disconnecting owner and needs no ownership transfers.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ItemContainer : NetworkBehaviour, ISlotStore
    {
        /// <summary>Bags inside bags: how far holders are followed. Guards against a corrupt cycle.</summary>
        private const int MaxBagDepth = 8;

        [SerializeField] private string _displayName = "Container";
        [SerializeField, Min(1)] private int _capacity = 12;

        [Tooltip("0: plain slots (equipment). Otherwise a grid this many cells wide; capacity must be whole rows.")]
        [SerializeField, Min(0)] private int _gridWidth;
        [SerializeField] private ContainerAccess _access = ContainerAccess.World;
        [SerializeField] private ContainerSlotRules _slotRules = ContainerSlotRules.Any;

        [Tooltip("Private: only the contents owner and peers looting it receive the contents (stash, bags, pockets, crates, " +
                 "corpses). Public: every observer (a character's worn equipment only).")]
        [SerializeField] private ContainerVisibility _visibility = ContainerVisibility.Private;

        [Tooltip("World containers: max distance from the player's eyes to the container's collider.")]
        [SerializeField, Min(0.5f)] private float _accessRange = 3f;

        [SerializeField] private ItemDatabase _itemDatabase;

        private static int _serverFlushDepth;

        private readonly SyncVar<string> _replicatedName = new SyncVar<string>();
        private readonly SyncVar<int> _replicatedGridWidth = new SyncVar<int>();

        // ---- Server ----
        private ItemStack[] _serverSlots = Array.Empty<ItemStack>();
        private SlotInventory _serverInventory;
        private readonly SlotChangeSet _changes = new SlotChangeSet();
        private readonly ViewerSet<NetworkConnection> _viewers = new ViewerSet<NetworkConnection>(ConnectionComparer.Instance);
        private readonly List<NetworkConnection> _added = new List<NetworkConnection>();
        private readonly List<NetworkConnection> _removed = new List<NetworkConnection>();
        private readonly List<NetworkConnection> _pendingPublicCopies = new List<NetworkConnection>();
        private readonly List<ItemContainer> _movedStorages = new List<ItemContainer>();
        private ItemContainer _serverHolder;
        private NetworkConnection _contentsOwner;
        private bool _audienceDirty;
        private TimeManager _flushTimeManager;
        private Func<NetworkConnection, bool> _canReceive;

        // ---- Client ----
        private readonly SlotReplica _replica = new SlotReplica();
        private SlotInventory _layout;

        private Collider _accessCollider;

        /// <summary>
        /// Client (and host) side: raised when this peer's copy changes (full copy received, slots changed, copy dropped).
        /// </summary>
        public event Action Changed;

        /// <summary>Server only. Raised whenever any slot changes, immediately.</summary>
        public event Action ServerChanged;

        public string DisplayName => string.IsNullOrEmpty(_replicatedName.Value) ? _displayName : _replicatedName.Value;
        public int Capacity => _capacity;
        public ContainerAccess Access => _access;
        public ContainerSlotRules SlotRules => _slotRules;
        public ContainerVisibility Visibility => _visibility;
        public ItemDatabase ItemDatabase => _itemDatabase;

        /// <summary>Cells per row, or 0 for plain slots. Replicated (bag storage is configured at runtime).</summary>
        public int GridWidth
        {
            get
            {
                if (IsServerInitialized || !IsClientInitialized) return _gridWidth;
                return _replica.HasCopy ? _replica.GridWidth : _replicatedGridWidth.Value;
            }
        }

        public bool IsGrid => GridWidth > 0;

        /// <summary>
        /// Slots this peer can read. Server and host: all of them. Client: its copy; 0 until the first copy arrives,
        /// and whenever this peer is not allowed to see the contents.
        /// </summary>
        public int SlotCount => IsServerInitialized ? _serverSlots.Length : _replica.Count;

        /// <summary>True when this peer can read the contents: always on the server, on a client while it holds a copy.</summary>
        public bool CanReadContents => IsServerInitialized || _replica.HasCopy;

        public ItemStack GetSlot(int index)
        {
            if (!IsServerInitialized) return _replica[index];
            return index >= 0 && index < _serverSlots.Length ? _serverSlots[index] : ItemStack.Empty;
        }

        /// <summary>Server only: inventory rules bound to the slots. Null on clients.</summary>
        public SlotInventory ServerInventory
        {
            get
            {
                if (!IsServerInitialized) return null;
                EnsureServerSlots();
                return _serverInventory;
            }
        }

        /// <summary>
        /// Read-only rules view on either side: grid geometry for UI (which cell belongs to which item, where an item
        /// could go). Never change contents through it on a client (it throws); the server uses <see cref="ServerInventory"/>.
        /// Null on a client without a copy.
        /// </summary>
        public SlotInventory Layout
        {
            get
            {
                if (IsServerInitialized) return ServerInventory;
                int width = GridWidth;
                int count = _replica.Count;
                if (count == 0 || _itemDatabase == null || (width > 0 && count % width != 0)) return null;
                if (_layout == null || _layout.GridWidth != width)
                    _layout = new SlotInventory(_replica, _itemDatabase, SlotFilters.Create(_slotRules, _itemDatabase), width);
                return _layout;
            }
        }

        private void Awake()
        {
            _accessCollider = GetComponentInChildren<Collider>();
            _canReceive = CanReceive;

            if (_itemDatabase == null)
                Debug.LogError($"[{nameof(ItemContainer)}] Item database is not assigned on {name}.", this);
        }

        private void OnDestroy()
        {
            CancelServerFlush();
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

            // The owner (session containers, the character's pockets) gets its copy even if nothing is ever written.
            ServerMarkAudienceDirty();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            CancelServerFlush();

            // Items carried inside this container leave the game with it: despawn bag storage they own.
            ItemStack[] slots = _serverSlots;
            _serverSlots = Array.Empty<ItemStack>();
            _serverInventory = null;
            _changes.Clear();
            _viewers.Reset();
            _pendingPublicCopies.Clear();
            _movedStorages.Clear();
            _serverHolder = null;
            _contentsOwner = null;
            _audienceDirty = false;

            NetworkManager manager = NetworkManager;
            for (int i = 0; i < slots.Length; i++)
                ItemFactory.ServerDestroy(manager, slots[i]);
        }

        /// <summary>The object was spawned for a peer (connect, late join, back in view): it needs a full copy if allowed.</summary>
        public override void OnSpawnServer(NetworkConnection connection)
        {
            base.OnSpawnServer(connection);
            if (connection == null) return;

            if (_visibility == ContainerVisibility.Public)
            {
                if (!_pendingPublicCopies.Contains(connection)) _pendingPublicCopies.Add(connection);
                ServerScheduleFlush();
                return;
            }

            // Whatever it had went with the previous spawn; the next flush sends a copy if it is in the audience.
            _viewers.Forget(connection);
            ServerMarkAudienceDirty();
        }

        /// <summary>The object is despawned for a peer: its copy goes with it.</summary>
        public override void OnDespawnServer(NetworkConnection connection)
        {
            base.OnDespawnServer(connection);
            _viewers.Forget(connection);
            _pendingPublicCopies.Remove(connection);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _replica.Clear();
            _layout = null;
        }

        // ---------------- Server: viewers ----------------

        /// <summary>
        /// Server: lets a peer see the contents on top of the contents owner (looting a world container or a bag in it),
        /// until <see cref="ServerRemoveViewer"/>. Grants are counted. Ignored for public containers.
        /// </summary>
        public void ServerAddViewer(NetworkConnection connection)
        {
            if (!IsServerInitialized || connection == null || !connection.IsValid || _visibility == ContainerVisibility.Public) return;
            _viewers.Grant(connection);
            ServerMarkAudienceDirty();
        }

        /// <summary>Server: takes back one <see cref="ServerAddViewer"/> grant; the peer drops its copy unless it still may see it.</summary>
        public void ServerRemoveViewer(NetworkConnection connection)
        {
            if (!IsServerInitialized || connection == null) return;
            if (_viewers.Revoke(connection)) ServerMarkAudienceDirty();
        }

        // ---------------- Server: contents ----------------

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
            for (int i = 0; i < _serverSlots.Length; i++)
            {
                ItemStack stack = _serverSlots[i];
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
            _serverSlots = moved;
            _serverInventory = new SlotInventory(this, _itemDatabase, SlotFilters.Create(_slotRules, _itemDatabase), _gridWidth);
            _replicatedGridWidth.Value = _gridWidth;

            // The slot count changed: everyone who can see it gets a fresh full copy.
            _changes.MarkAll();
            ServerScheduleFlush();
            ServerChanged?.Invoke();
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

            if (_gridWidth > 0 && _capacity % _gridWidth != 0)
            {
                Debug.LogError($"[{nameof(ItemContainer)}] {name}: capacity {_capacity} is not whole rows of {_gridWidth}; using plain slots.", this);
                _gridWidth = 0;
            }

            _serverSlots = new ItemStack[_capacity];
            _serverInventory = new SlotInventory(
                this, _itemDatabase, SlotFilters.Create(_slotRules, _itemDatabase), _gridWidth);
        }

        // ---- ISlotStore: the server's slots. Every write is noticed here. ----

        int ISlotStore.Count => _serverSlots.Length;

        ItemStack ISlotStore.this[int index]
        {
            get => _serverSlots[index];
            set => ServerWrite(index, value);
        }

        private void ServerWrite(int index, in ItemStack value)
        {
            ItemStack previous = _serverSlots[index];
            if (previous == value) return; // only real changes are sent

            _serverSlots[index] = value;
            if (previous.ContentsHandle != value.ContentsHandle)
            {
                if (previous.HasContents) ServerNoteStorageMoved(previous, arrived: false);
                if (value.HasContents) ServerNoteStorageMoved(value, arrived: true);
            }

            _changes.Mark(index);
            ServerScheduleFlush();
            ServerChanged?.Invoke();
        }

        /// <summary>
        /// A bag arrived here or left. Arrival makes this its holder at once; departure is checked when the storage
        /// flushes (a move within one container, or out and back, must not flicker its audience).
        /// </summary>
        private void ServerNoteStorageMoved(in ItemStack bag, bool arrived)
        {
            if (!TryResolveContents(NetworkManager, bag, out ItemContainer storage) || storage == this) return;

            if (arrived) storage._serverHolder = this;
            storage.ServerMarkAudienceDirty();
            if (!_movedStorages.Contains(storage)) _movedStorages.Add(storage);
        }

        private bool ServerHolds(ItemContainer storage)
        {
            int handle = ItemStack.ToContentsHandle(storage.ObjectId);
            for (int i = 0; i < _serverSlots.Length; i++)
                if (_serverSlots[i].ContentsHandle == handle) return true;
            return false;
        }

        /// <summary>
        /// Who owns what is in here: this object's owner, or for bag storage, the contents owner of the container
        /// the bag lies in. Null when nobody does (crates, corpses, bags in them, bots' gear).
        /// </summary>
        private NetworkConnection ServerContentsOwner()
        {
            ItemContainer container = this;
            for (int depth = 0; depth < MaxBagDepth && container != null; depth++)
            {
                ItemContainer holder = container._serverHolder;
                if (holder != null && holder.IsServerInitialized)
                {
                    container = holder;
                    continue;
                }

                NetworkConnection owner = container.Owner;
                return owner != null && owner.IsValid ? owner : null;
            }
            return null;
        }

        private bool CanReceive(NetworkConnection connection)
        {
            NetworkObject networkObject = NetworkObject;
            return connection != null && connection.IsActive && networkObject != null && networkObject.Observers.Contains(connection);
        }

        // ---------------- Server: sending ----------------

        private void ServerMarkAudienceDirty()
        {
            _audienceDirty = true;
            ServerScheduleFlush();
        }

        /// <summary>Changes are sent once per tick, after the tick's gameplay ran.</summary>
        private void ServerScheduleFlush()
        {
            if (_flushTimeManager != null || !IsServerInitialized) return;
            TimeManager timeManager = TimeManager;
            if (timeManager == null) return;

            _flushTimeManager = timeManager;
            timeManager.OnPostTick += ServerFlush;
        }

        private void CancelServerFlush()
        {
            if (_flushTimeManager == null) return;
            _flushTimeManager.OnPostTick -= ServerFlush;
            _flushTimeManager = null;
        }

        private void ServerFlush()
        {
            CancelServerFlush();
            if (!IsServerInitialized || _serverFlushDepth >= MaxBagDepth) return;

            _serverFlushDepth++;
            try
            {
                // Bags that moved in or out first: a peer gets a bag's contents no later than the slot change showing it.
                if (_movedStorages.Count > 0)
                {
                    ItemContainer[] moved = _movedStorages.ToArray();
                    _movedStorages.Clear();
                    foreach (ItemContainer storage in moved)
                        if (storage != null && storage != this) storage.ServerFlush();
                }

                if (_audienceDirty) ServerRefreshAudience();
                ServerSendPublicCopies();
                ServerSendChanges();
            }
            finally
            {
                _serverFlushDepth--;
            }
        }

        private void ServerRefreshAudience()
        {
            _audienceDirty = false;
            if (_serverHolder != null && (!_serverHolder.IsServerInitialized || !_serverHolder.ServerHolds(this))) _serverHolder = null;

            NetworkConnection owner = ServerContentsOwner();
            bool ownerChanged = !ReferenceEquals(owner, _contentsOwner);
            _contentsOwner = owner;

            if (_visibility == ContainerVisibility.Private && _viewers.Update(owner, owner != null, _canReceive, _added, _removed))
            {
                foreach (NetworkConnection connection in _removed)
                    if (CanReceive(connection)) TargetDropCopy(connection);

                if (_added.Count > 0)
                {
                    SlotReplication.Capture(this, out int[] indices, out ItemStack[] stacks);
                    foreach (NetworkConnection connection in _added)
                        TargetFullCopy(connection, _serverSlots.Length, _gridWidth, indices, stacks);
                }
            }

            // Bags carried in here follow this container's contents owner (bags in bags).
            if (ownerChanged) ServerRefreshHeldStorages();
        }

        private void ServerRefreshHeldStorages()
        {
            for (int i = 0; i < _serverSlots.Length; i++)
            {
                if (!TryResolveContents(NetworkManager, _serverSlots[i], out ItemContainer storage) || storage == this) continue;
                storage._audienceDirty = true;
                storage.ServerFlush();
            }
        }

        /// <summary>Public containers: full copies for peers it was just spawned for.</summary>
        private void ServerSendPublicCopies()
        {
            if (_pendingPublicCopies.Count == 0) return;

            SlotReplication.Capture(this, out int[] indices, out ItemStack[] stacks);
            foreach (NetworkConnection connection in _pendingPublicCopies)
                if (CanReceive(connection)) TargetFullCopy(connection, _serverSlots.Length, _gridWidth, indices, stacks);
            _pendingPublicCopies.Clear();
        }

        private void ServerSendChanges()
        {
            if (_changes.IsEmpty) return;
            bool isPublic = _visibility == ContainerVisibility.Public;

            if (_changes.NeedsFullCopy)
            {
                _changes.Clear();
                SlotReplication.Capture(this, out int[] all, out ItemStack[] allStacks);
                if (isPublic)
                {
                    ObserversFullCopy(_serverSlots.Length, _gridWidth, all, allStacks);
                    return;
                }
                foreach (NetworkConnection connection in _viewers.Current)
                    if (CanReceive(connection)) TargetFullCopy(connection, _serverSlots.Length, _gridWidth, all, allStacks);
                return;
            }

            _changes.Drain(this, out int[] indices, out ItemStack[] stacks);
            if (indices.Length == 0) return;

            if (isPublic)
            {
                ObserversChanges(indices, stacks);
                return;
            }
            foreach (NetworkConnection connection in _viewers.Current)
                if (CanReceive(connection)) TargetChanges(connection, indices, stacks);
        }

        // ---------------- Client: receiving ----------------

        [TargetRpc]
        private void TargetFullCopy(NetworkConnection connection, int capacity, int gridWidth, int[] indices, ItemStack[] stacks) =>
            ClientApplyFullCopy(capacity, gridWidth, indices, stacks);

        [ObserversRpc]
        private void ObserversFullCopy(int capacity, int gridWidth, int[] indices, ItemStack[] stacks) =>
            ClientApplyFullCopy(capacity, gridWidth, indices, stacks);

        [TargetRpc]
        private void TargetChanges(NetworkConnection connection, int[] indices, ItemStack[] stacks) => ClientApplyChanges(indices, stacks);

        [ObserversRpc]
        private void ObserversChanges(int[] indices, ItemStack[] stacks) => ClientApplyChanges(indices, stacks);

        [TargetRpc]
        private void TargetDropCopy(NetworkConnection connection)
        {
            // On a host the server's slots are read directly; only the notification matters.
            if (!IsServerInitialized)
            {
                _replica.Clear();
                _layout = null;
            }
            Changed?.Invoke();
        }

        private void ClientApplyFullCopy(int capacity, int gridWidth, int[] indices, ItemStack[] stacks)
        {
            if (!IsServerInitialized)
            {
                _replica.ApplyFullCopy(capacity, gridWidth, indices, stacks);
                _layout = null;
            }
            Changed?.Invoke();
        }

        private void ClientApplyChanges(int[] indices, ItemStack[] stacks)
        {
            // Changes before the first full copy are ignored: the copy carries them.
            if (!IsServerInitialized && !_replica.ApplyChanges(indices, stacks)) return;
            Changed?.Invoke();
        }

        /// <summary>
        /// Connections compared by reference: a connection's client id is reset when it disconnects and may be reused
        /// by a later client, so an id must never key a grant.
        /// </summary>
        private sealed class ConnectionComparer : IEqualityComparer<NetworkConnection>
        {
            public static readonly ConnectionComparer Instance = new ConnectionComparer();

            public bool Equals(NetworkConnection x, NetworkConnection y) => ReferenceEquals(x, y);

            public int GetHashCode(NetworkConnection connection) => RuntimeHelpers.GetHashCode(connection);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _serverFlushDepth = 0;
    }
}
