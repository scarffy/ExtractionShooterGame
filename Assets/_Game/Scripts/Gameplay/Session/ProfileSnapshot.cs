using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Core.Persistence;
using ExtractionShooter.Gameplay.Inventory;
using FishNet.Managing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>
    /// Server only: copies between live containers (stash, loadout, bag storage) and a <see cref="PlayerProfile"/>.
    /// Bags are recreated with fresh storage on load; their contents are restored into it.
    /// </summary>
    public static class ProfileSnapshot
    {
        private const string StashKey = "stash";
        private const string LoadoutKey = "loadout";

        public static PlayerProfile ServerCapture(string playerName, ItemContainer stash, ItemContainer loadout, NetworkManager networkManager)
        {
            var profile = new PlayerProfile { PlayerName = playerName };
            profile.Stash = Capture(stash, StashKey, profile, networkManager);
            profile.Loadout = Capture(loadout, LoadoutKey, profile, networkManager);
            return profile;
        }

        /// <summary>
        /// Fills empty stash and loadout containers from a profile. Items that cannot be placed where they were
        /// (unknown slot rules, data edited by hand) go to the stash if there is room, otherwise they are dropped and logged.
        /// </summary>
        public static void ServerRestore(PlayerProfile profile, ItemContainer stash, ItemContainer loadout, NetworkManager networkManager)
        {
            if (profile == null) return;
            Restore(profile.Stash, stash, stash, profile, networkManager, canHoldBags: true);
            Restore(profile.Loadout, loadout, stash, profile, networkManager, canHoldBags: true);
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
                    stack = ItemFactory.ServerCreate(networkManager, definition);
                    if (stack.IsEmpty) continue;
                    if (ItemContainer.TryResolveContents(networkManager, stack, out ItemContainer storage))
                        Restore(profile.FindBag(item.ContentsKey), storage, null, profile, networkManager, canHoldBags: false);
                }

                if (inventory.TryPlace(item.Slot, stack) == InventoryResult.Success) continue;

                SlotInventory fallback = overflow != null ? overflow.ServerInventory : null;
                int added = 0;
                if (fallback != null && !ReferenceEquals(fallback, inventory)) fallback.TryAdd(stack, out added);
                if (added == stack.Quantity) continue;

                Debug.LogWarning($"[{nameof(ProfileSnapshot)}] No room for '{stack.ItemId}' in {profile.PlayerName}'s profile; dropped.");
                if (added == 0) ItemFactory.ServerDestroy(networkManager, stack);
            }
        }
    }
}
