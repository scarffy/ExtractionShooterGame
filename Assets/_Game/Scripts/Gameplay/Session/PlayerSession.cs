using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Core.Persistence;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Trading;
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
        LoadoutPockets = 6,
        SafeBox = 7,
    }

    /// <summary>
    /// One per connected client, owned by it. Holds the player's profile on the server: the stash
    /// (child container), the equipped loadout (child container, equipment slot rules) and, through
    /// <see cref="PlayerTrading"/> on the same object, the player's credits.
    /// <list type="bullet">
    /// <item>Sign in loads the profile from disk (a new profile gets the starter kit).</item>
    /// <item>In the hideout the owner moves items between stash, loadout, the loadout's bags and a bag opened in the stash.</item>
    /// <item>Deploying (matchmaking through <see cref="PlayerSquad"/> and <see cref="SquadLobby"/>) hands the loadout to a new
    /// player character (gear is now at risk; the profile is saved without it).</item>
    /// <item>Extracting brings the character's gear back into the loadout; dying (or leaving mid-raid) loses it. A starter kit is issued only to broke players.</item>
    /// </list>
    /// Every change is saved to disk immediately. Character spawning is done by the raid director when the
    /// <see cref="SquadLobby"/> sends the player's squad into a raid.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSession : NetworkBehaviour
    {
        [Tooltip("Child container: stash, slot rules Any, access Carried.")]
        [SerializeField] private ItemContainer _stash;

        [Tooltip("Child container: 8 slots, slot rules Equipment, access Carried.")]
        [SerializeField] private ItemContainer _loadout;

        [Tooltip("Child container: 5x1 grid, slot rules Pockets, access Carried. Goes into the raid with the loadout.")]
        [SerializeField] private ItemContainer _pockets;

        [Tooltip("Child container: grid, slot rules Storage, access Carried. Sized by the safe box tier; never leaves the session.")]
        [SerializeField] private ItemContainer _safeBox;

        [Tooltip("Knife given to new profiles. The melee slot is kept through death.")]
        [SerializeField] private ItemDefinition _starterMelee;

        [Tooltip("Given to new profiles, and as a safety net to broke players: see ServerIssueStarterKitIfBroke.")]
        [SerializeField] private PlayerLoadout _starterKit;

        private static readonly HashSet<string> ServerNamesInUse = new HashSet<string>();

        private readonly SyncVar<string> _playerName = new SyncVar<string>();
        private readonly SyncVar<SessionState> _state = new SyncVar<SessionState>(SessionState.SigningIn);
        private readonly SyncVar<NetworkObject> _openBag = new SyncVar<NetworkObject>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncVar<int> _safeBoxTier = new SyncVar<int>(new SyncTypeSettings(ReadPermission.OwnerOnly));

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

        public string PlayerName => _playerName.Value;
        public SessionState State => _state.Value;
        public ItemContainer Stash => _stash;
        public ItemContainer Loadout => _loadout;

        /// <summary>The loadout's pockets (between raids; in a raid they are on the character).</summary>
        public ItemContainer LoadoutPockets => _pockets;

        /// <summary>The safe box: usable in and out of raids, kept on death, never lootable.</summary>
        public ItemContainer SafeBox => _safeBox;

        /// <summary>Owner side: index into the trader's safe box tiers (0 = the free box).</summary>
        public int SafeBoxTier => _safeBoxTier.Value;
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
            _safeBoxTier.OnChange += OnSafeBoxTierChanged;

            if (_stash == null || _loadout == null || _pockets == null || _safeBox == null)
                Debug.LogError($"[{nameof(PlayerSession)}] Stash, loadout, pockets or safe box container is not assigned on {name}.", this);
        }

        private void OnDestroy()
        {
            _playerName.OnChange -= OnNameChanged;
            _state.OnChange -= OnStateChanged;
            _openBag.OnChange -= OnOpenBagChanged;
            _safeBoxTier.OnChange -= OnSafeBoxTierChanged;
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

        /// <param name="targetSlot">-1 places automatically.</param>
        /// <param name="quantity">0 moves the whole stack.</param>
        /// <param name="rotated">In a grid: the item's orientation at <paramref name="targetSlot"/> (its new top-left cell).</param>
        public void RequestTransfer(ItemContainer source, int sourceSlot, ItemContainer target, int targetSlot = -1, int quantity = 0,
            bool rotated = false)
        {
            SessionContainer from = RoleOf(source), to = RoleOf(target);
            if (IsOwner && from != SessionContainer.None && to != SessionContainer.None)
                ServerTransfer(from, sourceSlot, to, targetSlot, quantity, rotated);
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

        /// <summary>Re-packs the stash: weapons, gear, bags, ammo, medical, grenades, mods, valuables; big items first.</summary>
        public void RequestSortStash()
        {
            if (IsOwner) ServerSortStash();
        }

        [ServerRpc]
        private void ServerSortStash()
        {
            if (State != SessionState.Hideout || _stash == null) return;
            ItemDatabase database = _stash.ItemDatabase;
            if (database == null) return;

            int Rank(in ItemStack stack)
            {
                if (!database.TryGet(stack.ItemId, out ItemDefinition item)) return int.MaxValue;
                if (item is ContainerItemDefinition) return 2;
                switch (item.Category)
                {
                    case ItemCategory.Weapon: return 0;
                    case ItemCategory.Armor: return 1;
                    case ItemCategory.Melee: return 3;
                    case ItemCategory.Ammo: return 4;
                    case ItemCategory.Medical: return 5;
                    case ItemCategory.Throwable: return 6;
                    case ItemCategory.Attachment: return 7;
                    case ItemCategory.Valuable: return 8;
                    default: return 9;
                }
            }

            bool sorted = _stash.ServerInventory.Sort((a, b) =>
            {
                int byRank = Rank(a).CompareTo(Rank(b));
                if (byRank != 0) return byRank;
                ItemSize sa = database.GetSize(a.ItemId), sb = database.GetSize(b.ItemId);
                int byArea = (sb.Width * sb.Height).CompareTo(sa.Width * sa.Height);
                return byArea != 0 ? byArea : string.CompareOrdinal(a.ItemId, b.ItemId);
            });
            if (sorted) ServerAfterChange();
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
                ServerApplySafeBoxTier(profile.SafeBoxTier);
                ProfileSnapshot.ServerRestore(profile, Containers, NetworkManager);
                if (_trading != null) _trading.ServerSetMoney(profile.Money);
                // Covers gear lost by leaving mid-raid, the same as a death.
                ServerIssueStarterKitIfBroke();
                ServerIssueMeleeIfMissing();
                Debug.Log($"[{nameof(PlayerSession)}] Loaded profile '{playerName}' from {_store.Directory}.");
            }
            else
            {
                ServerApplySafeBoxTier(0);
                LoadoutBuilder.ServerGive(NetworkManager, _starterKit, _loadout);
                ServerIssueMeleeIfMissing();
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

        /// <summary>Server, raid director: the loadout goes onto the new character and is now at risk.</summary>
        public void ServerBeginRaid(PlayerCharacter character)
        {
            if (!IsServerInitialized || _state.Value != SessionState.Hideout || character == null) return;

            _character = character;
            _openBag.Value = null;
            LoadoutBuilder.ServerMoveAll(_loadout, character.Equipment.Container);
            LoadoutBuilder.ServerMoveAll(_pockets, character.Equipment.Pockets);
            character.Inventory.ServerSetSafeBox(_safeBox);
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

            if (_character != null && _character.Equipment != null)
            {
                // Extracted: everything comes home. Died: only the kept slots (the knife) are still on the character.
                if (!LoadoutBuilder.ServerMoveAll(_character.Equipment.Container, _loadout))
                    ServerMoveRestToStash(_character.Equipment.Container);
                if (extracted && !LoadoutBuilder.ServerMoveAll(_character.Equipment.Pockets, _pockets))
                    ServerMoveRestToStash(_character.Equipment.Pockets);
                _character.Inventory.ServerSetSafeBox(null);
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
            if (_starterKit == null || !IsLoadoutBare() || OwnsWeapon()) return;
            if (_trading != null && _trading.Money >= StarterKitValue()) return;
            LoadoutBuilder.ServerGive(NetworkManager, _starterKit, _loadout);
        }

        /// <summary>Nothing equipped except the kept slots (the knife), and nothing in the pockets.</summary>
        private bool IsLoadoutBare()
        {
            SlotInventory loadout = _loadout.ServerInventory;
            for (int slot = 0; slot < loadout.Capacity; slot++)
                if (!loadout[slot].IsEmpty && !EquipmentRules.IsKeptOnDeath((EquipmentSlot)slot)) return false;
            return _pockets.ServerInventory.IsEmpty;
        }

        /// <summary>
        /// The knife is never lost, so every profile has one: new profiles, and ones saved before the melee slot existed.
        /// </summary>
        private void ServerIssueMeleeIfMissing()
        {
            if (_starterMelee == null || !_loadout.ServerInventory[(int)EquipmentSlot.Melee].IsEmpty) return;
            if (ContainsCategory(_stash, ItemCategory.Melee) || ContainsCategory(_pockets, ItemCategory.Melee) ||
                ContainsCategory(_safeBox, ItemCategory.Melee)) return;
            _loadout.ServerInventory.TryPlace((int)EquipmentSlot.Melee, ItemFactory.ServerCreate(NetworkManager, _starterMelee));
        }

        private bool OwnsWeapon() => ContainsWeapon(_loadout) || ContainsWeapon(_stash) || ContainsWeapon(_pockets) || ContainsWeapon(_safeBox);

        private bool ContainsWeapon(ItemContainer container) => ContainsCategory(container, ItemCategory.Weapon);

        private bool ContainsCategory(ItemContainer container, ItemCategory category)
        {
            ItemDatabase database = container != null ? container.ItemDatabase : null;
            if (database == null) return false;

            for (int slot = 0; slot < container.SlotCount; slot++)
            {
                ItemStack stack = container.GetSlot(slot);
                if (stack.IsEmpty) continue;
                if (database.TryGet(stack.ItemId, out ItemDefinition item) && item.Category == category) return true;
                if (ItemContainer.TryResolveContents(NetworkManager, stack, out ItemContainer storage) && ContainsCategory(storage, category)) return true;
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

        private ProfileSnapshot.SessionContainers Containers =>
            new ProfileSnapshot.SessionContainers(_stash, _loadout, _pockets, _safeBox);

        /// <summary>
        /// Server: sizes the safe box for a tier (clamped to the trader's tiers). Growing keeps every item in place.
        /// Returns false if the box could not be resized (it never shrinks below its contents).
        /// </summary>
        internal bool ServerApplySafeBoxTier(int tier)
        {
            global::ExtractionShooter.Gameplay.Trading.SafeBoxTier[] tiers = SafeBoxTiers;
            int clamped = Mathf.Clamp(tier, 0, tiers.Length - 1);
            global::ExtractionShooter.Gameplay.Trading.SafeBoxTier size = tiers[clamped];
            if (!_safeBox.ServerResize(size.Width * size.Height, size.Width)) return false;
            _safeBoxTier.Value = clamped;
            return true;
        }

        /// <summary>The trader's safe box sizes, or just the free 2x1 box when there is no trader.</summary>
        internal global::ExtractionShooter.Gameplay.Trading.SafeBoxTier[] SafeBoxTiers =>
            _trading != null && _trading.Trader != null && _trading.Trader.SafeBoxTiers.Count > 0
                ? _trading.Trader.SafeBoxTiersArray
                : new[] { global::ExtractionShooter.Gameplay.Trading.SafeBoxTier.Default };

        private void ServerMoveRestToStash(ItemContainer equipment)
        {
            SlotInventory gear = equipment.ServerInventory;
            if (!gear.TransferAllTo(_stash.ServerInventory))
                Debug.LogWarning($"[{nameof(PlayerSession)}] Stash full: some extracted gear of {PlayerName} was lost.", this);
        }

        // ---------------- Server: hideout inventory ----------------

        [ServerRpc]
        private void ServerTransfer(SessionContainer source, int sourceSlot, SessionContainer target, int targetSlot, int quantity, bool rotated)
        {
            SlotInventory from = ResolveAccessible(source);
            SlotInventory to = ResolveAccessible(target);
            if (from == null || to == null) return;

            // An attachment dropped on a weapon is fitted to it rather than moved.
            if (targetSlot < 0 || WeaponModding.TryFit(from, sourceSlot, to, targetSlot, _stash.ItemDatabase) == ModResult.NotApplicable)
                from.TransferTo(to, sourceSlot, targetSlot, quantity, targetSlot >= 0 ? rotated : (bool?)null);
            ServerAfterChange();
        }

        /// <summary>Takes an attachment off a weapon in the stash or loadout; it goes to the same container, else the stash.</summary>
        public void RequestDetach(ItemContainer container, int slot, AttachmentSlot attachmentSlot)
        {
            SessionContainer role = RoleOf(container);
            if (IsOwner && role != SessionContainer.None) ServerDetach(role, slot, attachmentSlot);
        }

        [ServerRpc]
        private void ServerDetach(SessionContainer container, int slot, AttachmentSlot attachmentSlot)
        {
            SlotInventory inventory = ResolveAccessible(container);
            if (inventory == null) return;
            if (WeaponModding.TryDetach(inventory, slot, attachmentSlot, _stash.ItemDatabase, inventory, _stash.ServerInventory) ==
                ModResult.Fitted)
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
                    if (!TryMove(from, sourceSlot, _loadout, quantity) && !TryMove(from, sourceSlot, LoadoutRig, quantity) &&
                        !TryMove(from, sourceSlot, _pockets, quantity))
                        TryMove(from, sourceSlot, LoadoutBackpack, quantity);
                    break;

                case SessionContainer.Loadout:
                case SessionContainer.LoadoutRig:
                case SessionContainer.LoadoutBackpack:
                case SessionContainer.LoadoutPockets:
                case SessionContainer.SafeBox:
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
            if (container == _pockets) return SessionContainer.LoadoutPockets;
            if (container == _safeBox) return SessionContainer.SafeBox;
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
                case SessionContainer.LoadoutPockets: container = _pockets; break;
                case SessionContainer.SafeBox: container = _safeBox; break;
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
            PlayerProfile profile = ProfileSnapshot.ServerCapture(_playerName.Value, Containers, NetworkManager);
            if (_trading != null) profile.Money = _trading.Money;
            profile.SafeBoxTier = _safeBoxTier.Value;
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

        private void OnSafeBoxTierChanged(int previous, int current, bool asServer)
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
