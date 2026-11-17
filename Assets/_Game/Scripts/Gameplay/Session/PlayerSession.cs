using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Core.Persistence;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>Where a player is in the raid loop.</summary>
    public enum SessionState : byte
    {
        /// <summary>Connected, waiting for a player name.</summary>
        SigningIn = 0,

        /// <summary>Between raids: managing the stash and loadout.</summary>
        Hideout = 1,

        /// <summary>The loadout is in the raid on a player character.</summary>
        InRaid = 2,
    }

    /// <summary>
    /// A session's containers, named by role in RPCs: the stash and loadout are child components of the
    /// session object, so their NetworkObject alone does not identify them.
    /// </summary>
    internal enum SessionContainer : byte
    {
        None = 0,
        Stash = 1,
        Loadout = 2,
        LoadoutRig = 3,
        LoadoutBackpack = 4,
        OpenBag = 5,
    }

    /// <summary>
    /// One per connected client, owned by it. Holds the player's profile on the server: the stash
    /// (child container), the equipped loadout (child container, equipment slot rules) and, through
    /// <see cref="PlayerTrading"/> on the same object, the player's credits.
    /// <list type="bullet">
    /// <item>Sign in loads the profile from disk (a new profile gets the starter kit).</item>
    /// <item>In the hideout the owner moves items between stash, loadout, the loadout's bags and a bag opened in the stash.</item>
    /// <item>Deploy hands the loadout to a new player character (gear is now at risk; the profile is saved without it).</item>
    /// <item>Extracting brings the character's gear back into the loadout; dying (or leaving mid-raid) loses it. A starter kit is issued only to broke players.</item>
    /// </list>
    /// Every change is saved to disk immediately. Character spawning is done by the raid director, which
    /// listens to <see cref="ServerDeployRequested"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSession : NetworkBehaviour
    {
        [Tooltip("Child container: stash, slot rules Any, access Carried.")]
        [SerializeField] private ItemContainer _stash;

        [Tooltip("Child container: 7 slots, slot rules Equipment, access Carried.")]
        [SerializeField] private ItemContainer _loadout;

        [Tooltip("Given to new profiles, and as a safety net to broke players: see ServerIssueStarterKitIfBroke.")]
        [SerializeField] private PlayerLoadout _starterKit;

        private static readonly HashSet<string> ServerNamesInUse = new HashSet<string>();

        private readonly SyncVar<string> _playerName = new SyncVar<string>();
        private readonly SyncVar<SessionState> _state = new SyncVar<SessionState>(SessionState.SigningIn);
        private readonly SyncVar<NetworkObject> _openBag = new SyncVar<NetworkObject>(new SyncTypeSettings(ReadPermission.OwnerOnly));

        private PlayerTrading _trading;
        private ProfileStore _store;
        private string _nameKey;
        private PlayerCharacter _character;

        /// <summary>The local client's session, or null.</summary>
        public static PlayerSession Local { get; private set; }

        public static event Action<PlayerSession> LocalSessionChanged;

        /// <summary>Client side: state, name or opened bag changed.</summary>
        public event Action Changed;

        /// <summary>Owner side: the server refused the sign-in (reason).</summary>
        public event Action<string> SignInFailed;

        /// <summary>Server: the owner pressed Deploy in the hideout.</summary>
        public event Action<PlayerSession> ServerDeployRequested;

        public string PlayerName => _playerName.Value;
        public SessionState State => _state.Value;
        public ItemContainer Stash => _stash;
        public ItemContainer Loadout => _loadout;
        public ItemContainer OpenBag => Resolve(_openBag.Value);
        public ItemContainer LoadoutRig => LoadoutBuilder.ResolveStorage(NetworkManager, _loadout, EquipmentSlot.ChestRig);
        public ItemContainer LoadoutBackpack => LoadoutBuilder.ResolveStorage(NetworkManager, _loadout, EquipmentSlot.Backpack);

        /// <summary>Credits and the lobby trader, or null if this session has none.</summary>
        public PlayerTrading Trading => _trading;

        private void Awake()
        {
            _trading = GetComponent<PlayerTrading>();
            _playerName.OnChange += OnNameChanged;
            _state.OnChange += OnStateChanged;
            _openBag.OnChange += OnOpenBagChanged;

            if (_stash == null || _loadout == null)
                Debug.LogError($"[{nameof(PlayerSession)}] Stash or loadout container is not assigned on {name}.", this);
        }

        private void OnDestroy()
        {
            _playerName.OnChange -= OnNameChanged;
            _state.OnChange -= OnStateChanged;
            _openBag.OnChange -= OnOpenBagChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _store = ProfileStore.CreateDefault();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (_nameKey != null) ServerNamesInUse.Remove(_nameKey);
            _nameKey = null;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;

            Local = this;
            LocalSessionChanged?.Invoke(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (Local != this) return;

            Local = null;
            LocalSessionChanged?.Invoke(null);
        }

        // ---------------- Owner requests ----------------

        public void RequestSignIn(string playerName)
        {
            if (IsOwner) ServerSignIn(playerName ?? string.Empty);
        }

        public void RequestDeploy()
        {
            if (IsOwner) ServerDeploy();
        }

        /// <param name="targetSlot">-1 places automatically.</param>
        /// <param name="quantity">0 moves the whole stack.</param>
        public void RequestTransfer(ItemContainer source, int sourceSlot, ItemContainer target, int targetSlot = -1, int quantity = 0)
        {
            SessionContainer from = RoleOf(source), to = RoleOf(target);
            if (IsOwner && from != SessionContainer.None && to != SessionContainer.None)
                ServerTransfer(from, sourceSlot, to, targetSlot, quantity);
        }

        /// <summary>Stash → loadout (equip, else into the loadout's bags); loadout and its bags → stash.</summary>
        public void RequestQuickMove(ItemContainer source, int sourceSlot, int quantity = 0)
        {
            SessionContainer from = RoleOf(source);
            if (IsOwner && from != SessionContainer.None) ServerQuickMove(from, sourceSlot, quantity);
        }

        /// <summary>Opens the contents of a bag lying in the stash.</summary>
        public void RequestOpenBag(ItemContainer source, int sourceSlot)
        {
            if (IsOwner && RoleOf(source) == SessionContainer.Stash) ServerOpenBag(sourceSlot);
        }

        public void RequestCloseBag()
        {
            if (IsOwner) ServerCloseBag();
        }

        // ---------------- Server: sign in ----------------

        [ServerRpc]
        private void ServerSignIn(string requestedName)
        {
            if (_state.Value != SessionState.SigningIn) return;

            if (!ProfileNames.TryNormalize(requestedName, out string playerName))
            {
                TargetSignInFailed(Owner, $"Use {ProfileNames.MinLength}–{ProfileNames.MaxLength} letters, digits, _ or -.");
                return;
            }

            string key = ProfileNames.ToKey(playerName);
            if (!ServerNamesInUse.Add(key))
            {
                TargetSignInFailed(Owner, $"'{playerName}' is already playing.");
                return;
            }
            _nameKey = key;

            if (_store.TryLoad(playerName, out PlayerProfile profile))
            {
                ProfileSnapshot.ServerRestore(profile, _stash, _loadout, NetworkManager);
                if (_trading != null) _trading.ServerSetMoney(profile.Money);
                // Covers gear lost by leaving mid-raid, the same as a death.
                ServerIssueStarterKitIfBroke();
                Debug.Log($"[{nameof(PlayerSession)}] Loaded profile '{playerName}' from {_store.Directory}.");
            }
            else
            {
                LoadoutBuilder.ServerGive(NetworkManager, _starterKit, _loadout);
                if (_trading != null) _trading.ServerSetMoney(0);
                Debug.Log($"[{nameof(PlayerSession)}] Created profile '{playerName}' in {_store.Directory}.");
            }

            _playerName.Value = playerName;
            _state.Value = SessionState.Hideout;
            ServerSave();
        }

        [TargetRpc]
        private void TargetSignInFailed(NetworkConnection connection, string reason) => SignInFailed?.Invoke(reason);

        // ---------------- Server: raid ----------------

        [ServerRpc]
        private void ServerDeploy()
        {
            if (_state.Value == SessionState.Hideout) ServerDeployRequested?.Invoke(this);
        }

        /// <summary>Server, raid director: the loadout goes onto the new character and is now at risk.</summary>
        public void ServerBeginRaid(PlayerCharacter character)
        {
            if (!IsServerInitialized || _state.Value != SessionState.Hideout || character == null) return;

            _character = character;
            _openBag.Value = null;
            LoadoutBuilder.ServerMoveAll(_loadout, character.Equipment.Container);
            character.Equipment.ServerLoadEmptyWeapons();

            _state.Value = SessionState.InRaid;
            ServerSave();
        }

        /// <summary>
        /// Server, raid director: the raid is over. Extracted gear returns to the loadout; on death it is already
        /// on the corpse, and an empty loadout gets the starter kit. Call before despawning the character.
        /// </summary>
        public void ServerEndRaid(bool extracted)
        {
            if (!IsServerInitialized || _state.Value != SessionState.InRaid) return;

            if (extracted && _character != null && _character.Equipment != null)
            {
                if (!LoadoutBuilder.ServerMoveAll(_character.Equipment.Container, _loadout))
                    ServerMoveRestToStash(_character.Equipment.Container);
            }

            if (!extracted) ServerIssueStarterKitIfBroke();

            _character = null;
            _state.Value = SessionState.Hideout;
            ServerSave();
        }

        /// <summary>
        /// Safety net so a wiped player can always play: the starter kit is issued only when the loadout is empty,
        /// no weapon is owned anywhere (loadout, stash, bags) and the credits could not buy the kit's worth.
        /// Selling an issued kit therefore lifts the player out of the net after a cycle or two; it cannot be farmed.
        /// </summary>
        private void ServerIssueStarterKitIfBroke()
        {
            if (_starterKit == null || !_loadout.ServerInventory.IsEmpty || OwnsWeapon()) return;
            if (_trading != null && _trading.Money >= StarterKitValue()) return;
            LoadoutBuilder.ServerGive(NetworkManager, _starterKit, _loadout);
        }

        private bool OwnsWeapon() => ContainsWeapon(_loadout) || ContainsWeapon(_stash);

        private bool ContainsWeapon(ItemContainer container)
        {
            ItemDatabase database = container != null ? container.ItemDatabase : null;
            if (database == null) return false;

            for (int slot = 0; slot < container.SlotCount; slot++)
            {
                ItemStack stack = container.GetSlot(slot);
                if (stack.IsEmpty) continue;
                if (database.TryGet(stack.ItemId, out ItemDefinition item) && item.Category == ItemCategory.Weapon) return true;
                if (ItemContainer.TryResolveContents(NetworkManager, stack, out ItemContainer storage) && ContainsWeapon(storage)) return true;
            }
            return false;
        }

        /// <summary>The kit's total item value: a player with this many credits can rebuild it themselves.</summary>
        private long StarterKitValue()
        {
            long total = 0;
            foreach (LoadoutEntry entry in _starterKit.Items)
                if (entry != null && entry.Item != null) total += (long)entry.Item.Value * entry.Quantity;
            return total;
        }

        private void ServerMoveRestToStash(ItemContainer equipment)
        {
            SlotInventory gear = equipment.ServerInventory;
            if (!gear.TransferAllTo(_stash.ServerInventory))
                Debug.LogWarning($"[{nameof(PlayerSession)}] Stash full: some extracted gear of {PlayerName} was lost.", this);
        }

        // ---------------- Server: hideout inventory ----------------

        [ServerRpc]
        private void ServerTransfer(SessionContainer source, int sourceSlot, SessionContainer target, int targetSlot, int quantity)
        {
            SlotInventory from = ResolveAccessible(source);
            SlotInventory to = ResolveAccessible(target);
            if (from == null || to == null) return;

            from.TransferTo(to, sourceSlot, targetSlot, quantity);
            ServerAfterChange();
        }

        [ServerRpc]
        private void ServerQuickMove(SessionContainer source, int sourceSlot, int quantity)
        {
            SlotInventory from = ResolveAccessible(source);
            if (from == null || !from.IsValidSlot(sourceSlot) || from[sourceSlot].IsEmpty) return;

            switch (source)
            {
                case SessionContainer.Stash:
                case SessionContainer.OpenBag:
                    if (!TryMove(from, sourceSlot, _loadout, quantity) && !TryMove(from, sourceSlot, LoadoutRig, quantity))
                        TryMove(from, sourceSlot, LoadoutBackpack, quantity);
                    break;

                case SessionContainer.Loadout:
                case SessionContainer.LoadoutRig:
                case SessionContainer.LoadoutBackpack:
                    TryMove(from, sourceSlot, _stash, quantity);
                    break;
            }
            ServerAfterChange();
        }

        [ServerRpc]
        private void ServerOpenBag(int sourceSlot)
        {
            SlotInventory from = ResolveAccessible(SessionContainer.Stash);
            if (from == null || !from.IsValidSlot(sourceSlot)) return;

            if (ItemContainer.TryResolveContents(NetworkManager, from[sourceSlot], out ItemContainer storage))
                _openBag.Value = storage.NetworkObject;
        }

        [ServerRpc]
        private void ServerCloseBag() => _openBag.Value = null;

        /// <summary>Which of this session's containers a container is (either side), or None.</summary>
        internal SessionContainer RoleOf(ItemContainer container)
        {
            if (container == null) return SessionContainer.None;
            if (container == _stash) return SessionContainer.Stash;
            if (container == _loadout) return SessionContainer.Loadout;
            if (container == LoadoutRig) return SessionContainer.LoadoutRig;
            if (container == LoadoutBackpack) return SessionContainer.LoadoutBackpack;
            if (container == OpenBag) return SessionContainer.OpenBag;
            return SessionContainer.None;
        }

        /// <summary>Server: the inventory for a role if the owner may use it in the hideout right now, otherwise null.</summary>
        internal SlotInventory ResolveAccessible(SessionContainer role)
        {
            if (_state.Value != SessionState.Hideout) return null;

            ItemContainer container;
            switch (role)
            {
                case SessionContainer.Stash: container = _stash; break;
                case SessionContainer.Loadout: container = _loadout; break;
                case SessionContainer.LoadoutRig: container = LoadoutRig; break;
                case SessionContainer.LoadoutBackpack: container = LoadoutBackpack; break;
                case SessionContainer.OpenBag:
                    container = OpenBag;
                    if (container != null && !IsInStash(container)) container = null;
                    break;
                default: container = null; break;
            }
            return container != null ? container.ServerInventory : null;
        }

        /// <summary>After a hideout change: close the opened bag if it left the stash, then save.</summary>
        internal void ServerAfterChange()
        {
            ItemContainer bag = OpenBag;
            if (_openBag.Value != null && (bag == null || !IsInStash(bag))) _openBag.Value = null;
            ServerSave();
        }

        private bool IsInStash(ItemContainer storage)
        {
            int handle = ItemStack.ToContentsHandle(storage.ObjectId);
            for (int i = 0; i < _stash.SlotCount; i++)
                if (_stash.GetSlot(i).ContentsHandle == handle) return true;
            return false;
        }

        private static bool TryMove(SlotInventory from, int slot, ItemContainer to, int quantity)
        {
            SlotInventory target = to != null ? to.ServerInventory : null;
            if (target == null || ReferenceEquals(from, target)) return false;

            InventoryResult result = from.TransferTo(target, slot, -1, quantity);
            return result == InventoryResult.Success || result == InventoryResult.Partial;
        }

        private void ServerSave()
        {
            if (_store == null || string.IsNullOrEmpty(_playerName.Value)) return;
            PlayerProfile profile = ProfileSnapshot.ServerCapture(_playerName.Value, _stash, _loadout, NetworkManager);
            if (_trading != null) profile.Money = _trading.Money;
            _store.Save(profile);
        }

        private static ItemContainer Resolve(NetworkObject networkObject) =>
            networkObject != null && networkObject.IsSpawned ? networkObject.GetComponent<ItemContainer>() : null;

        // ---------------- Client notifications ----------------

        private void OnNameChanged(string previous, string current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }

        private void OnStateChanged(SessionState previous, SessionState current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }

        private void OnOpenBagChanged(NetworkObject previous, NetworkObject current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            LocalSessionChanged = null;
            ServerNamesInUse.Clear();
        }
    }
}
