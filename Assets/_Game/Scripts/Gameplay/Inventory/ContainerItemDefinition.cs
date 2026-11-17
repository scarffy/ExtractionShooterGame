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

        [Tooltip("Storage grid: cells across and rows.")]
        [SerializeField, Min(1)] private int _gridWidth = 4;
        [SerializeField, Min(1)] private int _gridHeight = 3;

        [Tooltip("Networked prefab with an ItemContainer (slot rules: Storage, access: Carried).")]
        [SerializeField] private ItemContainer _storagePrefab;

        public ContainerKind ContainerKind => _containerKind;
        public int GridWidth => _gridWidth;
        public int GridHeight => _gridHeight;
        public int Capacity => _gridWidth * _gridHeight;
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
