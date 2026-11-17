using ExtractionShooter.Core.Inventory;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// Server only: creates and destroys item instances with their per-instance state.
    /// Armor starts at full durability; bags get a freshly spawned storage container.
    /// Every item that may carry state must be created here, never with a bare <see cref="ItemStack"/>.
    /// </summary>
    public static class ItemFactory
    {
        /// <summary>
        /// Creates one item stack. Stateful items (armor, bags) ignore <paramref name="quantity"/> and return 1.
        /// Returns <see cref="ItemStack.Empty"/> if the item cannot be created.
        /// </summary>
        public static ItemStack ServerCreate(NetworkManager networkManager, ItemDefinition definition, int quantity = 1)
        {
            if (networkManager == null || !networkManager.IsServerStarted || definition == null || string.IsNullOrEmpty(definition.Id))
                return ItemStack.Empty;

            switch (definition)
            {
                case ArmorDefinition armor:
                    return new ItemStack(armor.Id, 1, armor.MaxDurability);

                case ContainerItemDefinition bag:
                {
                    if (bag.StoragePrefab == null)
                    {
                        Debug.LogError($"[{nameof(ItemFactory)}] '{bag.name}' has no storage prefab.");
                        return ItemStack.Empty;
                    }

                    ItemContainer storage = Object.Instantiate(bag.StoragePrefab);
                    storage.name = $"{bag.DisplayName} Storage";
                    storage.ServerConfigure(bag.Capacity, bag.DisplayName, bag.GridWidth);
                    networkManager.ServerManager.Spawn(storage.NetworkObject);
                    return new ItemStack(bag.Id, 1, 0, ItemStack.ToContentsHandle(storage.ObjectId));
                }

                default:
                    return quantity > 0 ? new ItemStack(definition.Id, quantity) : ItemStack.Empty;
            }
        }

        /// <summary>
        /// Destroys an item's carried state (despawns a bag's storage and everything in it).
        /// Call when an item leaves the game, never when it merely moves.
        /// </summary>
        public static void ServerDestroy(NetworkManager networkManager, in ItemStack stack)
        {
            if (!stack.HasContents || networkManager == null || !networkManager.IsServerStarted) return;

            if (networkManager.ServerManager.Objects.Spawned.TryGetValue(stack.ContentsObjectId, out NetworkObject storage) &&
                storage != null && storage.IsSpawned)
            {
                networkManager.ServerManager.Despawn(storage);
            }
        }
    }
}
