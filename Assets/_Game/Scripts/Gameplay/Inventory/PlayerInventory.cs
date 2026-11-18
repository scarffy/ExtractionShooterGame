using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Player;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// The only path for a client to change items. Tracks the world container being looted (and a bag
    /// opened inside it), and validates every request on the server.
    /// A player may touch: their equipment, their pockets, their equipped rig and backpack storage, their safe box
    /// (it lives on their session, so it is kept on death and never lootable), the open world container while
    /// within reach, and the storage of a bag lying in that container.
    /// Containers are sent in RPCs as <see cref="ItemContainer"/> references (object + component), so child
    /// containers such as the pockets and the safe box are addressed exactly.
    /// While a world container (or a bag lying in it) is open, the server grants this player's connection a view of its
    /// contents (<see cref="ItemContainer.ServerAddViewer"/>); closing it, walking away or dying takes the grant back,
    /// so the client receives a crate's or corpse's contents only while it is looting it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter), typeof(PlayerEquipment))]
    public sealed class PlayerInventory : NetworkBehaviour
    {
        /// <summary>World container being looted. Replicated to the owner only.</summary>
        private readonly SyncVar<NetworkObject> _openContainer =
            new SyncVar<NetworkObject>(new SyncTypeSettings(ReadPermission.OwnerOnly));

        /// <summary>Storage of a bag lying in the open container, opened for looting. Owner only.</summary>
        private readonly SyncVar<NetworkObject> _openNested =
            new SyncVar<NetworkObject>(new SyncTypeSettings(ReadPermission.OwnerOnly));

        private PlayerCharacter _character;
        private PlayerEquipment _equipment;
        private readonly List<SlotInventory> _quickHealInventories = new List<SlotInventory>();
        private readonly List<int> _quickHealSlots = new List<int>();
        private readonly List<MedicalDefinition> _quickHealMedicals = new List<MedicalDefinition>();
        private readonly List<TreatmentOption> _quickHealOptions = new List<TreatmentOption>();
        private ItemContainer _serverSafeBox;

        /// <summary>Server: the containers this player was granted a view of, and the connection they were granted to.</summary>
        private readonly ViewGrant _worldGrant = new ViewGrant();
        private readonly ViewGrant _nestedGrant = new ViewGrant();

        /// <summary>Owner side. Raised when the looted container or the opened bag changes.</summary>
        public event Action OpenContainersChanged;

        public PlayerEquipment Equipment => _equipment;

        public ItemContainer OpenContainer => Resolve(_openContainer.Value);

        public ItemContainer OpenNestedContainer => Resolve(_openNested.Value);

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _equipment = GetComponent<PlayerEquipment>();
            _openContainer.OnChange += OnOpenChanged;
            _openNested.OnChange += OnOpenChanged;
        }

        private void OnDestroy()
        {
            _openContainer.OnChange -= OnOpenChanged;
            _openNested.OnChange -= OnOpenChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            TimeManager.OnTick += ServerValidateOpenContainers;
            _character.Health.ServerDied += OnServerDied;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (TimeManager != null) TimeManager.OnTick -= ServerValidateOpenContainers;
            _character.Health.ServerDied -= OnServerDied;

            // Despawned (raid over, disconnect): nothing stays visible to this connection.
            _worldGrant.MoveTo(null, null);
            _nestedGrant.MoveTo(null, null);
        }

        // ---------------- Owner requests ----------------

        public void RequestOpen(ItemContainer worldContainer)
        {
            if (IsOwner && worldContainer != null) ServerOpen(worldContainer.NetworkObject);
        }

        /// <summary>Opens the storage of a bag lying in the open world container.</summary>
        public void RequestOpenNested(ItemContainer source, int slot)
        {
            if (IsOwner && source != null) ServerOpenNested(source.NetworkObject, slot);
        }

        public void RequestCloseNested()
        {
            if (IsOwner) ServerCloseNested();
        }

        public void RequestClose()
        {
            if (IsOwner) ServerClose();
        }

        /// <summary>
        /// Moves items between any two accessible containers.
        /// <paramref name="targetSlot"/> -1 places automatically; <paramref name="quantity"/> 0 moves the whole stack.
        /// In a grid, <paramref name="targetSlot"/> is the item's new top-left cell and <paramref name="rotated"/> its orientation there.
        /// </summary>
        public void RequestTransfer(ItemContainer source, int sourceSlot, ItemContainer target, int targetSlot = -1, int quantity = 0,
            bool rotated = false)
        {
            if (IsOwner && source != null && target != null)
                ServerTransfer(source, sourceSlot, target, targetSlot, quantity, rotated);
        }

        /// <summary>
        /// One-click move: the server picks the destination (equip, store, or loot side).
        /// <paramref name="quantity"/> 0 moves the whole stack; 1 moves a single item.
        /// </summary>
        public void RequestQuickMove(ItemContainer source, int sourceSlot, int quantity = 0)
        {
            if (IsOwner && source != null) ServerQuickMove(source, sourceSlot, quantity);
        }

        /// <summary>Uses a consumable (medical item) from the player's own equipment or bags.</summary>
        public void RequestUse(ItemContainer source, int sourceSlot)
        {
            if (IsOwner && source != null) ServerUse(source, sourceSlot);
        }

        /// <summary>Uses the best medical item carried for the player's wounds (bleeding, then a fracture, then health).</summary>
        public void RequestQuickHeal()
        {
            if (IsOwner) ServerQuickHeal();
        }

        // ---------------- Server RPCs ----------------

        [ServerRpc]
        private void ServerOpen(NetworkObject target)
        {
            ItemContainer container = Resolve(target);
            if (container == null || container.Access != ContainerAccess.World) return;
            if (!container.ServerIsWithinReach(_character)) return;

            _openContainer.Value = target;
            _openNested.Value = null;
        }

        [ServerRpc]
        private void ServerOpenNested(NetworkObject source, int slot)
        {
            ItemContainer world = OpenContainer;
            if (world == null || source != world.NetworkObject || !world.ServerIsWithinReach(_character)) return;

            ItemStack bag = world.GetSlot(slot);
            if (ItemContainer.TryResolveContents(NetworkManager, bag, out ItemContainer storage))
                _openNested.Value = storage.NetworkObject;
        }

        [ServerRpc]
        private void ServerCloseNested() => _openNested.Value = null;

        [ServerRpc]
        private void ServerClose()
        {
            _openNested.Value = null;
            _openContainer.Value = null;
        }

        [ServerRpc]
        private void ServerTransfer(ItemContainer source, int sourceSlot, ItemContainer target, int targetSlot, int quantity, bool rotated)
        {
            if (!_character.Health.IsAlive) return;

            SlotInventory from = ResolveAccessible(source, out _);
            SlotInventory to = ResolveAccessible(target, out _);
            if (from == null || to == null) return;

            // An attachment dropped on a weapon is fitted to it rather than moved.
            if (targetSlot >= 0 && WeaponModding.TryFit(from, sourceSlot, to, targetSlot, _equipment.Container.ItemDatabase) !=
                ModResult.NotApplicable) return;

            from.TransferTo(to, sourceSlot, targetSlot, quantity, targetSlot >= 0 ? rotated : (bool?)null);
        }

        /// <summary>Takes an attachment off a weapon in the player's own equipment or bags.</summary>
        public void RequestDetach(ItemContainer container, int slot, AttachmentSlot attachmentSlot)
        {
            if (IsOwner && container != null) ServerDetach(container, slot, attachmentSlot);
        }

        [ServerRpc]
        private void ServerDetach(ItemContainer container, int slot, AttachmentSlot attachmentSlot)
        {
            if (!_character.Health.IsAlive) return;
            SlotInventory inventory = ResolveAccessible(container, out ContainerRole role);
            if (inventory == null || role == ContainerRole.Loot) return;

            var destinations = new List<SlotInventory> { inventory };
            foreach (ItemContainer storage in _equipment.CarriedStorage)
                if (storage != null && storage.ServerInventory != inventory) destinations.Add(storage.ServerInventory);
            WeaponModding.TryDetach(inventory, slot, attachmentSlot, _equipment.Container.ItemDatabase, destinations.ToArray());
        }

        [ServerRpc]
        private void ServerQuickMove(ItemContainer source, int sourceSlot, int quantity)
        {
            if (!_character.Health.IsAlive) return;

            SlotInventory from = ResolveAccessible(source, out ContainerRole role);
            if (from == null || !from.IsValidSlot(sourceSlot) || from[sourceSlot].IsEmpty) return;

            SlotInventory equipment = _equipment.Container.ServerInventory;
            SlotInventory loot = LootTarget();

            switch (role)
            {
                case ContainerRole.Equipment:
                    // Unequip: to the loot side if looting, otherwise into the rig, then the backpack.
                    if (TryMove(from, sourceSlot, loot, quantity)) return;
                    TryMoveToStorage(from, sourceSlot, quantity);
                    return;

                case ContainerRole.Storage:
                    // From a bag: to the loot side if looting, otherwise try to equip it.
                    if (TryMove(from, sourceSlot, loot, quantity)) return;
                    TryMove(from, sourceSlot, equipment, quantity);
                    return;

                case ContainerRole.Loot:
                    // Taking loot: equip if a matching slot is free, otherwise into the rig, then the backpack.
                    if (TryMove(from, sourceSlot, equipment, quantity)) return;
                    TryMoveToStorage(from, sourceSlot, quantity);
                    return;
            }
        }

        [ServerRpc]
        private void ServerUse(ItemContainer source, int sourceSlot)
        {
            if (!_character.Health.IsAlive) return;

            SlotInventory from = ResolveAccessible(source, out ContainerRole role);
            if (from == null || role == ContainerRole.Loot || !from.IsValidSlot(sourceSlot)) return;

            ItemStack stack = from[sourceSlot];
            ItemDatabase database = _equipment.Container.ItemDatabase;
            if (stack.IsEmpty || database == null || !database.TryGet(stack.ItemId, out ItemDefinition definition)) return;
            if (!(definition is MedicalDefinition medical)) return;

            ServerTreat(medical, from, sourceSlot);
        }

        [ServerRpc]
        private void ServerQuickHeal()
        {
            PlayerInjuries injuries = _character.Injuries;
            ItemDatabase database = _equipment.Container != null ? _equipment.Container.ItemDatabase : null;
            if (!_character.Health.IsAlive || injuries == null || injuries.IsTreating || database == null) return;

            // Every medical item carried (rig, pockets, backpack), first slot of each kind.
            _quickHealInventories.Clear();
            _quickHealSlots.Clear();
            _quickHealMedicals.Clear();
            _quickHealOptions.Clear();
            foreach (ItemContainer container in _equipment.CarriedStorage)
            {
                SlotInventory inventory = container != null ? container.ServerInventory : null;
                if (inventory == null) continue;
                for (int slot = 0; slot < inventory.Capacity; slot++)
                {
                    ItemStack stack = inventory[slot];
                    if (stack.IsEmpty || !database.TryGet(stack.ItemId, out ItemDefinition definition) ||
                        !(definition is MedicalDefinition medical) || _quickHealMedicals.Contains(medical)) continue;
                    _quickHealInventories.Add(inventory);
                    _quickHealSlots.Add(slot);
                    _quickHealMedicals.Add(medical);
                    _quickHealOptions.Add(medical.ToOption());
                }
            }

            int pick = TreatmentRules.PickQuickHeal(_quickHealOptions, injuries.ServerWounds);
            if (pick >= 0) ServerTreat(_quickHealMedicals[pick], _quickHealInventories[pick], _quickHealSlots[pick]);
        }

        /// <summary>
        /// Server: starts treating with the item in this slot. The item stays where it is until the treatment completes;
        /// then one is taken from that slot, or from anywhere else carried if it was moved meanwhile.
        /// </summary>
        private void ServerTreat(MedicalDefinition medical, SlotInventory from, int sourceSlot)
        {
            PlayerInjuries injuries = _character.Injuries;
            string itemId = from[sourceSlot].ItemId;
            if (injuries == null)
            {
                if (_character.Health.ServerHeal(medical.HealAmount) > 0) from.TakeFromSlot(sourceSlot, 1);
                return;
            }
            injuries.ServerStartTreatment(medical, () => ServerTakeOne(itemId, from, sourceSlot));
        }

        /// <summary>Server: takes one of an item, preferring the given slot, else any carried slot. False if none is left.</summary>
        private bool ServerTakeOne(string itemId, SlotInventory preferred, int preferredSlot)
        {
            if (preferred != null && preferred.IsValidSlot(preferredSlot) && !preferred[preferredSlot].IsEmpty &&
                preferred[preferredSlot].ItemId == itemId)
            {
                preferred.TakeFromSlot(preferredSlot, 1);
                return true;
            }

            foreach (ItemContainer container in _equipment.CarriedStorage)
            {
                SlotInventory inventory = container != null ? container.ServerInventory : null;
                if (inventory == null) continue;
                for (int slot = 0; slot < inventory.Capacity; slot++)
                {
                    if (inventory[slot].IsEmpty || inventory[slot].ItemId != itemId) continue;
                    inventory.TakeFromSlot(slot, 1);
                    return true;
                }
            }
            return false;
        }

        // ---------------- Server helpers ----------------

        private enum ContainerRole { None, Equipment, Storage, Loot }

        /// <summary>Server, session: the owner's safe box, usable during this raid.</summary>
        public void ServerSetSafeBox(ItemContainer safeBox) => _serverSafeBox = safeBox;

        /// <summary>Server: the safe box this player may use during the raid, or null (keys in it open doors).</summary>
        public ItemContainer ServerSafeBox => _serverSafeBox;

        /// <summary>The inventory if this player may use it right now, otherwise null.</summary>
        private SlotInventory ResolveAccessible(ItemContainer container, out ContainerRole role)
        {
            role = ContainerRole.None;
            if (container == null || !container.IsSpawned) return null;

            if (container == _equipment.Container) role = ContainerRole.Equipment;
            else if (container == _equipment.ChestRigStorage || container == _equipment.BackpackStorage ||
                     container == _equipment.Pockets || (_serverSafeBox != null && container == _serverSafeBox))
                role = ContainerRole.Storage;
            else if (container == OpenContainer && container.ServerIsWithinReach(_character)) role = ContainerRole.Loot;
            else if (container == OpenNestedContainer && IsNestedStillReachable()) role = ContainerRole.Loot;
            else return null;

            return container.ServerInventory;
        }

        /// <summary>Where looted items go back to: the opened bag if any, else the world container.</summary>
        private SlotInventory LootTarget()
        {
            ItemContainer nested = OpenNestedContainer;
            if (nested != null && IsNestedStillReachable()) return nested.ServerInventory;

            ItemContainer world = OpenContainer;
            return world != null && world.ServerIsWithinReach(_character) ? world.ServerInventory : null;
        }

        private bool TryMoveToStorage(SlotInventory from, int slot, int quantity)
        {
            foreach (ItemContainer storage in _equipment.CarriedStorage)
            {
                if (storage != null && TryMove(from, slot, storage.ServerInventory, quantity)) return true;
            }
            return false;
        }

        /// <summary>Auto-places into the target (<paramref name="quantity"/> 0 = whole stack). True if anything moved.</summary>
        private static bool TryMove(SlotInventory from, int slot, SlotInventory to, int quantity)
        {
            if (to == null || ReferenceEquals(from, to)) return false;
            InventoryResult result = from.TransferTo(to, slot, -1, quantity);
            return result == InventoryResult.Success || result == InventoryResult.Partial;
        }

        /// <summary>The opened bag must still lie in the open world container, and that must be in reach.</summary>
        private bool IsNestedStillReachable()
        {
            ItemContainer world = OpenContainer;
            NetworkObject nested = _openNested.Value;
            if (world == null || nested == null || !world.ServerIsWithinReach(_character)) return false;

            int handle = ItemStack.ToContentsHandle(nested.ObjectId);
            for (int i = 0; i < world.SlotCount; i++)
                if (world.GetSlot(i).ContentsHandle == handle) return true;
            return false;
        }

        private void ServerValidateOpenContainers()
        {
            ItemContainer world = OpenContainer;
            if (_openContainer.Value != null && (world == null || !world.ServerIsWithinReach(_character)))
            {
                _openContainer.Value = null;
                _openNested.Value = null;
            }
            else if (_openNested.Value != null && !IsNestedStillReachable())
            {
                _openNested.Value = null;
            }

            // Normally done by the open-container callbacks already; this keeps grants and open containers in step regardless.
            ServerSyncViewGrants();
        }

        private void OnServerDied(DamageInfo _)
        {
            _openNested.Value = null;
            _openContainer.Value = null;
        }

        private static ItemContainer Resolve(NetworkObject networkObject) =>
            networkObject != null && networkObject.IsSpawned ? networkObject.GetComponent<ItemContainer>() : null;

        private void OnOpenChanged(NetworkObject previous, NetworkObject current, bool asServer)
        {
            if (asServer) ServerSyncViewGrants();
            else OpenContainersChanged?.Invoke();
        }

        /// <summary>Server: every change of the open containers moves the view grants with them.</summary>
        private void ServerSyncViewGrants()
        {
            if (!IsServerInitialized) return;
            NetworkConnection viewer = Owner != null && Owner.IsValid ? Owner : null;
            _worldGrant.MoveTo(viewer != null ? Resolve(_openContainer.Value) : null, viewer);
            _nestedGrant.MoveTo(viewer != null ? Resolve(_openNested.Value) : null, viewer);
        }

        /// <summary>One container this player may see because it has it open.</summary>
        private sealed class ViewGrant
        {
            private ItemContainer _container;
            private NetworkConnection _viewer;

            /// <summary>Revokes the current grant (if any) and grants <paramref name="container"/> instead.</summary>
            public void MoveTo(ItemContainer container, NetworkConnection viewer)
            {
                if (container == _container && ReferenceEquals(viewer, _viewer)) return;

                if (_container != null) _container.ServerRemoveViewer(_viewer);
                _container = container != null && viewer != null ? container : null;
                _viewer = _container != null ? viewer : null;
                if (_container != null) _container.ServerAddViewer(_viewer);
            }
        }
    }
}
