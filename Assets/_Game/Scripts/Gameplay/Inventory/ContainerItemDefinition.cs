using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>Which equipment slot a storage item occupies.</summary>
    public enum ContainerKind
    {
        ChestRig = 0,
        Backpack = 1,
    }

    /// <summary>
    /// A wearable bag (chest rig or backpack) with its own storage. Each item instance owns a networked
    /// storage container that travels with it: drop a full backpack and it stays full.
    /// </summary>
    [CreateAssetMenu(fileName = "Bag", menuName = "Extraction Shooter/Items/Bag")]
    public sealed class ContainerItemDefinition : ItemDefinition
    {
        [Header("Storage")]
        [SerializeField] private ContainerKind _containerKind = ContainerKind.Backpack;
        [SerializeField, Min(1)] private int _capacity = 12;

        [Tooltip("Networked prefab with an ItemContainer (slot rules: Storage, access: Carried).")]
        [SerializeField] private ItemContainer _storagePrefab;

        public ContainerKind ContainerKind => _containerKind;
        public int Capacity => _capacity;
        public ItemContainer StoragePrefab => _storagePrefab;

        /// <summary>Each bag has its own contents, so bags never stack.</summary>
        public override int MaxStack => 1;

        protected override void OnValidate()
        {
            base.OnValidate();
            if (_storagePrefab == null)
                Debug.LogWarning($"[{nameof(ContainerItemDefinition)}] '{name}' has no storage prefab.", this);
        }
    }
}
