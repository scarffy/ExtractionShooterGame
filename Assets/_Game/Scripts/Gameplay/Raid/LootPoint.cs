using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Raid
{
    /// <summary>
    /// Optional settings on a world loot spawn point: a different loot table (high-tier crates in the loot room,
    /// medical boxes) and the name the container shows. Points without one get the container prefab's defaults.
    /// Read by the server's world loot spawner before it spawns the container.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LootPoint : MonoBehaviour
    {
        [Tooltip("Table the container is filled from. Empty: the container prefab's own table.")]
        [SerializeField] private LootTable _lootTable;

        [Tooltip("Name shown when searching the container. Empty: the container prefab's own name.")]
        [SerializeField] private string _displayName = string.Empty;

        public LootTable LootTable => _lootTable;

        public string DisplayName => _displayName;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = _lootTable != null ? new Color(0.95f, 0.35f, 0.9f) : new Color(1f, 0.75f, 0.2f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * 0.4f, new Vector3(1.2f, 0.8f, 0.8f));
        }
#endif
    }
}
