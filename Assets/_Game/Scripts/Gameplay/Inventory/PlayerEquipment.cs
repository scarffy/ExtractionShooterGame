using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Weapons;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// The player's worn gear: helmet, body armor, chest rig, backpack, two primaries and a pistol.
    /// Owns weapon selection (1/2/3), the loaded ammo of each weapon, and applies armor to incoming damage.
    /// Gear arrives from the player's stash loadout when a raid starts (see <c>PlayerSession</c>).
    /// Slot rules are enforced by the equipment container itself (see <see cref="EquipmentRules"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter))]
    public sealed class PlayerEquipment : NetworkBehaviour, IDamageModifier
    {
        private const byte NoWeapon = byte.MaxValue;

        [Tooltip("Container with 8 slots, slot rules: Equipment, access: Carried.")]
        [SerializeField] private ItemContainer _equipment;

        [Tooltip("Child container: 5x1 grid, slot rules: Pockets, access: Carried. Always there, rig or not.")]
        [SerializeField] private ItemContainer _pockets;

        private readonly SyncVar<byte> _activeWeaponSlot = new SyncVar<byte>(NoWeapon);

        private readonly List<KeyValuePair<string, int>> _ammoBuffer = new List<KeyValuePair<string, int>>();

        private PlayerCharacter _character;

        /// <summary>Client side. Raised when equipped items or the active weapon change.</summary>
        public event Action Changed;

        public ItemContainer Container => _equipment;

        /// <summary>The pockets: five small items, carried with or without a chest rig. Lost on death.</summary>
        public ItemContainer Pockets => _pockets;

        public ItemStack GetItem(EquipmentSlot slot) =>
            _equipment != null ? _equipment.GetSlot((int)slot) : ItemStack.Empty;

        public bool TryGetDefinition<T>(EquipmentSlot slot, out T definition) where T : ItemDefinition
        {
            definition = null;
            ItemStack item = GetItem(slot);
            if (item.IsEmpty || _equipment.ItemDatabase == null) return false;
            if (!_equipment.ItemDatabase.TryGet(item.ItemId, out ItemDefinition found)) return false;

            definition = found as T;
            return definition != null;
        }

        /// <summary>The selected weapon slot, or null when no weapon is drawn.</summary>
        public EquipmentSlot? ActiveWeaponSlot =>
            _activeWeaponSlot.Value == NoWeapon ? (EquipmentSlot?)null : (EquipmentSlot)_activeWeaponSlot.Value;

        /// <summary>The weapon that fires, or null.</summary>
        public WeaponDefinition ActiveWeapon =>
            ActiveWeaponSlot is EquipmentSlot slot && TryGetDefinition(slot, out WeaponDefinition weapon) ? weapon : null;

        /// <summary>The drawn weapon with its slot and instance state (loaded ammo). False when none is drawn.</summary>
        public bool TryGetActiveWeapon(out EquipmentSlot slot, out WeaponDefinition weapon, out ItemStack item)
        {
            weapon = null;
            item = ItemStack.Empty;
            slot = default;
            if (!(ActiveWeaponSlot is EquipmentSlot active) || !TryGetDefinition(active, out weapon)) return false;

            slot = active;
            item = GetItem(active);
            return true;
        }

        /// <summary>The drawn weapon's effective numbers with its attachments. False when none is drawn.</summary>
        public bool TryGetActiveWeaponStats(out WeaponStats stats)
        {
            stats = default;
            if (!TryGetActiveWeapon(out EquipmentSlot _, out WeaponDefinition weapon, out ItemStack item)) return false;
            stats = WeaponStats.Of(weapon, item, _equipment.ItemDatabase);
            return true;
        }

        /// <summary>Looks up an ammo type by id.</summary>
        public bool TryGetAmmo(string ammoId, out AmmoDefinition ammo)
        {
            ammo = null;
            if (string.IsNullOrEmpty(ammoId) || _equipment.ItemDatabase == null) return false;
            if (!_equipment.ItemDatabase.TryGet(ammoId, out ItemDefinition found)) return false;

            ammo = found as AmmoDefinition;
            return ammo != null;
        }

        /// <summary>Storage of the equipped chest rig, or null.</summary>
        public ItemContainer ChestRigStorage => ResolveStorage(EquipmentSlot.ChestRig);

        /// <summary>Storage of the equipped backpack, or null.</summary>
        public ItemContainer BackpackStorage => ResolveStorage(EquipmentSlot.Backpack);

        /// <summary>Everything carried that holds loose items, in use order: rig, pockets, backpack (missing ones skipped).</summary>
        public IEnumerable<ItemContainer> CarriedStorage
        {
            get
            {
                ItemContainer rig = ChestRigStorage;
                if (rig != null) yield return rig;
                if (_pockets != null) yield return _pockets;
                ItemContainer backpack = BackpackStorage;
                if (backpack != null) yield return backpack;
            }
        }

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _activeWeaponSlot.OnChange += OnActiveWeaponChanged;

            if (_equipment == null)
                Debug.LogError($"[{nameof(PlayerEquipment)}] Equipment container is not assigned on {name}.", this);
            if (_pockets == null)
                Debug.LogError($"[{nameof(PlayerEquipment)}] Pockets container is not assigned on {name}.", this);
        }

        private void OnDestroy()
        {
            _activeWeaponSlot.OnChange -= OnActiveWeaponChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _equipment.ServerChanged += ServerValidateActiveWeapon;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _equipment.ServerChanged -= ServerValidateActiveWeapon;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _equipment.Changed += RaiseChanged;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _equipment.Changed -= RaiseChanged;
        }

        private void Update()
        {
            if (!IsOwner) return;

            int selected = _character.Input.WeaponSlotPressed;
            if (selected >= 0 && selected < EquipmentRules.WeaponSlots.Length)
                ServerSelectWeapon((byte)EquipmentRules.WeaponSlots[selected]);
            else if (_character.Input.CycleWeaponPressed)
                ServerCycleWeapon();
        }

        // ---- Weapon selection ----

        [ServerRpc]
        private void ServerSelectWeapon(byte slot)
        {
            if (!_character.Health.IsAlive || slot >= EquipmentRules.SlotCount) return;

            var equipmentSlot = (EquipmentSlot)slot;
            if (EquipmentRules.IsWeaponSlot(equipmentSlot) && TryGetDefinition(equipmentSlot, out WeaponDefinition _))
                _activeWeaponSlot.Value = slot;
        }

        [ServerRpc]
        private void ServerCycleWeapon()
        {
            if (!_character.Health.IsAlive) return;

            int start = Array.IndexOf(EquipmentRules.WeaponSlots, ActiveWeaponSlot ?? EquipmentRules.WeaponSlots[0]);
            for (int step = 1; step <= EquipmentRules.WeaponSlots.Length; step++)
            {
                EquipmentSlot candidate = EquipmentRules.WeaponSlots[(start + step) % EquipmentRules.WeaponSlots.Length];
                if (TryGetDefinition(candidate, out WeaponDefinition _))
                {
                    _activeWeaponSlot.Value = (byte)candidate;
                    return;
                }
            }
        }

        /// <summary>Server: keep the active weapon pointing at a slot that still holds a weapon.</summary>
        private void ServerValidateActiveWeapon()
        {
            if (ActiveWeaponSlot is EquipmentSlot active && TryGetDefinition(active, out WeaponDefinition _)) return;

            foreach (EquipmentSlot slot in EquipmentRules.WeaponSlots)
            {
                if (TryGetDefinition(slot, out WeaponDefinition _))
                {
                    _activeWeaponSlot.Value = (byte)slot;
                    return;
                }
            }
            _activeWeaponSlot.Value = NoWeapon;
        }

        // ---- Armor ----

        /// <summary>
        /// Server: the armor covering the hit zone stops or lets through the round depending on its
        /// penetration against the armor class, and wears down by the round's armor damage.
        /// </summary>
        public DamageInfo ModifyIncomingDamage(in DamageInfo damage)
        {
            if (!IsServerInitialized) return damage;

            EquipmentSlot slot = damage.Zone == HitZone.Head ? EquipmentSlot.Helmet : EquipmentSlot.BodyArmor;
            if (!TryGetDefinition(slot, out ArmorDefinition armor) || armor.ProtectedZone != damage.Zone) return damage;

            ItemStack piece = GetItem(slot);
            ArmorMath.Result result = ArmorMath.Apply(damage.Amount, damage.Penetration, damage.ArmorDamagePercent,
                piece.Durability, armor.MaxDurability, armor.ArmorClass, armor.BluntThroughput, armor.WearMultiplier);

            if (result.DurabilityAfter != piece.Durability)
                _equipment.ServerInventory.SetDurability((int)slot, result.DurabilityAfter);

            return damage.WithAmount(result.DamageTaken);
        }

        // ---- Ammo ----

        /// <summary>Server: sets the rounds loaded in the weapon in a slot (firing, reloading).</summary>
        public void ServerSetLoadedAmmo(EquipmentSlot slot, string ammoId, int count)
        {
            if (!IsServerInitialized) return;
            _equipment.ServerInventory.SetLoadedAmmo((int)slot, ammoId, count);
        }

        /// <summary>Server: true if a reload of the weapon in <paramref name="slot"/> would change anything.</summary>
        public bool ServerCanReload(EquipmentSlot slot) =>
            ServerPlanReload(slot, out WeaponDefinition _, out ReloadPlanner.Plan plan) && !plan.IsNoOp;

        /// <summary>
        /// Server: reloads the weapon in <paramref name="slot"/> from the rig and backpack in one step.
        /// Keeps the loaded type when more of it is carried, otherwise switches to the first compatible
        /// type found and puts the old rounds back into storage. If the old rounds do not fit, nothing changes.
        /// </summary>
        public bool ServerTryReload(EquipmentSlot slot)
        {
            if (!IsServerInitialized || !ServerPlanReload(slot, out WeaponDefinition _, out ReloadPlanner.Plan plan) || plan.IsNoOp)
                return false;

            int taken = ServerRemoveFromStorage(plan.AmmoId, plan.RoundsToTake);
            if (taken != plan.RoundsToTake)
            {
                ServerAddToStorage(new ItemStack(plan.AmmoId, taken));
                return false;
            }

            if (plan.UnloadAmmoId != null)
            {
                int returned = ServerAddToStorage(new ItemStack(plan.UnloadAmmoId, plan.RoundsToUnload));
                if (returned < plan.RoundsToUnload)
                {
                    // No room for the old rounds: undo and keep the weapon as it was.
                    ServerRemoveFromStorage(plan.UnloadAmmoId, returned);
                    ServerAddToStorage(new ItemStack(plan.AmmoId, taken));
                    return false;
                }
            }

            ServerSetLoadedAmmo(slot, plan.AmmoId, plan.RoundsAfter);
            return true;
        }

        private bool ServerPlanReload(EquipmentSlot slot, out WeaponDefinition weapon, out ReloadPlanner.Plan plan)
        {
            plan = default;
            if (!TryGetDefinition(slot, out weapon)) return false;

            ItemStack item = GetItem(slot);
            string loaded = TryGetAmmo(item.AmmoId, out AmmoDefinition loadedAmmo) && weapon.Accepts(loadedAmmo) ? item.AmmoId : null;

            ServerCollectAmmo(weapon.Caliber, _ammoBuffer);
            int magazine = WeaponStats.Of(weapon, item, _equipment.ItemDatabase).MagazineSize;
            plan = ReloadPlanner.Create(loaded, loaded != null ? item.AmmoCount : 0, magazine, _ammoBuffer);
            return true;
        }

        /// <summary>Server: every stack of ammo of a caliber in the rig, pockets, then the backpack, in slot order.</summary>
        private void ServerCollectAmmo(Caliber caliber, List<KeyValuePair<string, int>> results)
        {
            results.Clear();
            foreach (ItemContainer storage in CarriedStorage)
            {
                SlotInventory inventory = storage != null ? storage.ServerInventory : null;
                if (inventory == null) continue;

                for (int i = 0; i < inventory.Capacity; i++)
                {
                    ItemStack stack = inventory[i];
                    if (!stack.IsEmpty && TryGetAmmo(stack.ItemId, out AmmoDefinition ammo) && ammo.Caliber == caliber)
                        results.Add(new KeyValuePair<string, int>(stack.ItemId, stack.Quantity));
                }
            }
        }

        /// <summary>Server: removes up to <paramref name="quantity"/> of an item from the rig, pockets, then the backpack.</summary>
        private int ServerRemoveFromStorage(string itemId, int quantity)
        {
            int removed = 0;
            foreach (ItemContainer storage in CarriedStorage)
            {
                SlotInventory inventory = storage != null ? storage.ServerInventory : null;
                if (inventory == null || removed >= quantity) continue;
                removed += inventory.Remove(itemId, quantity - removed);
            }
            return removed;
        }

        // ---- Raid start ----

        /// <summary>Server: loads every weapon with an empty magazine from carried ammo, then draws one.</summary>
        public void ServerLoadEmptyWeapons()
        {
            if (!IsServerInitialized) return;

            foreach (EquipmentSlot slot in EquipmentRules.WeaponSlots)
            {
                if (TryGetDefinition(slot, out WeaponDefinition _) && GetItem(slot).AmmoCount == 0)
                    ServerTryReload(slot);
            }
            ServerValidateActiveWeapon();
        }

        /// <summary>Server: puts an item into the rig, then the backpack. Returns how many were added.</summary>
        public int ServerAddToStorage(in ItemStack item) => LoadoutBuilder.ServerAddToStorage(NetworkManager, _equipment, item);

        private ItemContainer ResolveStorage(EquipmentSlot slot) => LoadoutBuilder.ResolveStorage(NetworkManager, _equipment, slot);

        private void OnActiveWeaponChanged(byte previous, byte current, bool asServer)
        {
            if (!asServer) RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke();
    }
}
