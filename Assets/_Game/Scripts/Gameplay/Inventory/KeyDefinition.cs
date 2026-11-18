using System;
using ExtractionShooter.Core.Inventory;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// A key: opens every locked door with the same lock id while carried anywhere on the player (rig, pockets, backpack,
    /// safe box). Each unlock uses it up a little; uses left are stored per key in its durability (see <see cref="KeyUses"/>),
    /// so they replicate and persist like armor durability, and the key is removed when the last use is spent.
    /// </summary>
    [CreateAssetMenu(fileName = "Key", menuName = "Extraction Shooter/Items/Key")]
    public sealed class KeyDefinition : ItemDefinition
    {
        [Header("Key")]
        [Tooltip("Lock this key opens. Doors with the same lock id accept it.")]
        [SerializeField] private string _lockId;

        [Tooltip("Unlocks before the key breaks.")]
        [SerializeField, Min(1)] private int _maxUses = 10;

        public string LockId => _lockId;
        public int MaxUses => Mathf.Max(1, _maxUses);

        /// <summary>Each key has its own uses left, so keys never stack.</summary>
        public override int MaxStack => 1;

        public bool Opens(string lockId) =>
            !string.IsNullOrEmpty(lockId) && string.Equals(_lockId, lockId, StringComparison.Ordinal);

        /// <summary>Uses left on one key.</summary>
        public int UsesLeft(in ItemStack stack) => KeyUses.Remaining(stack.Durability, MaxUses);

        protected override void OnValidate()
        {
            base.OnValidate();
            if (string.IsNullOrWhiteSpace(_lockId))
                Debug.LogWarning($"[{nameof(KeyDefinition)}] '{name}' has no lock id.", this);
        }
    }
}
