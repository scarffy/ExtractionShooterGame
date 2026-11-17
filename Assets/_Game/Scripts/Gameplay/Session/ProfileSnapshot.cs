using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Core.Persistence;
using ExtractionShooter.Gameplay.Inventory;
using FishNet.Managing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>
    /// Server only: copies between live containers (stash, loadout, pockets, safe box, bag storage) and a
    /// <see cref="PlayerProfile"/>. Bags are recreated with fresh storage on load; their contents are restored into it.
    /// </summary>
    public static class ProfileSnapshot
    {
        private const string StashKey = "stash";
        private const string LoadoutKey = "loadout";
        private const string PocketsKey = "pockets";
        private const string SafeBoxKey = "safebox";

        /// <summary>A session's persistent containers.</summary>
        public readonly struct SessionContainers
        {
            public readonly ItemContainer Stash;
            public readonly ItemContainer Loadout;
            public readonly ItemContainer Pockets;
            public readonly ItemContainer SafeBox;

            public SessionContainers(ItemContainer stash, ItemContainer loadout, ItemContainer pockets, ItemContainer safeBox)
            {
                Stash = stash;
                Loadout = loadout;
                Pockets = pockets;
                SafeBox = safeBox;
            }
        }

        public static PlayerProfile ServerCapture(string playerName, in SessionContainers containers, NetworkManager networkManager)
        {
            var profile = new PlayerProfile { PlayerName = playerName };
            profile.Stash = Capture(containers.Stash, StashKey, profile, networkManager);
            profile.Loadout = Capture(containers.Loadout, LoadoutKey, profile, networkManager);
            profile.Pockets = Capture(containers.Pockets, PocketsKey, profile, networkManager);
            profile.SafeBox = Capture(containers.SafeBox, SafeBoxKey, profile, networkManager);
            return profile;
        }

        /// <summary>
        /// Fills empty containers from a profile (size the safe box first). Items that cannot be placed where they were
        /// (saved before grids existed, data edited by hand) go to the first free place in the same container, then the
        /// stash; only if there is no room anywhere are they dropped and logged.
        /// </summary>
        public static void ServerRestore(PlayerProfile profile, in SessionContainers containers, NetworkManager networkManager)
        {
            if (profile == null) return;
            ItemContainer stash = containers.Stash;
            Restore(profile.Stash, stash, stash, profile, networkManager, canHoldBags: true);
            Restore(profile.Loadout, containers.Loadout, stash, profile, networkManager, canHoldBags: true);
            Restore(profile.Pockets, containers.Pockets, stash, profile, networkManager, canHoldBags: false);
            Restore(profile.SafeBox, containers.SafeBox, stash, profile, networkManager, canHoldBags: false);
        }

        private static StoredContainer Capture(ItemContainer container, string key, PlayerProfile profile, NetworkManager networkManager)
        {
            var stored = new StoredContainer { Key = key };
            if (container == null) return stored;

            for (int slot = 0; slot < container.SlotCount; slot++)
            {
                ItemStack stack = container.GetSlot(slot);
                if (stack.IsEmpty) continue;

                string contentsKey = null;
                if (ItemContainer.TryResolveContents(networkManager, stack, out ItemContainer storage))
                {
                    contentsKey = $"{key}-{slot}";
                    profile.Bags.Add(Capture(storage, contentsKey, profile, networkManager));
                }
                stored.Items.Add(ProfileCodec.ToStored(slot, stack, contentsKey));
            }
            return stored;
        }

        private static void Restore(StoredContainer stored, ItemContainer target, ItemContainer overflow,
            PlayerProfile profile, NetworkManager networkManager, bool canHoldBags)
        {
            SlotInventory inventory = target != null ? target.ServerInventory : null;
            ItemDatabase database = target != null ? target.ItemDatabase : null;
            if (stored == null || inventory == null || database == null) return;

            foreach (StoredItem item in ProfileCodec.PlaceableItems(stored, inventory.Capacity))
            {
                if (!ProfileCodec.TryFromStored(item, database, out ItemStack stack) || !database.TryGet(stack.ItemId, out ItemDefinition definition))
                {
                    Debug.LogWarning($"[{nameof(ProfileSnapshot)}] Dropped unknown item '{item.ItemId}' from {profile.PlayerName}'s profile.");
                    continue;
                }

                if (definition is ContainerItemDefinition)
                {
                    if (!canHoldBags) continue;
                    stack = ItemFactory.ServerCreate(networkManager, definition).WithRotation(item.Rotated);
                    if (stack.IsEmpty) continue;
                    if (ItemContainer.TryResolveContents(networkManager, stack, out ItemContainer storage))
                        Restore(profile.FindBag(item.ContentsKey), storage, null, profile, networkManager, canHoldBags: false);
                }

                if (inventory.TryPlace(item.Slot, stack) == InventoryResult.Success) continue;

                // Saved before grids existed, or the layout changed: the first free place in the same container,
                // then the stash.
                inventory.TryAdd(stack, out int added);
                SlotInventory fallback = overflow != null ? overflow.ServerInventory : null;
                if (added < stack.Quantity && fallback != null && !ReferenceEquals(fallback, inventory))
                {
                    fallback.TryAdd(stack.WithQuantity(stack.Quantity - added), out int more);
                    added += more;
                }
                if (added == stack.Quantity) continue;

                Debug.LogWarning($"[{nameof(ProfileSnapshot)}] No room for '{stack.ItemId}' in {profile.PlayerName}'s profile; dropped.");
                if (added == 0) ItemFactory.ServerDestroy(networkManager, stack);
            }
        }
    }
}
