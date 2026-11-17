using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// Items a player spawns with. Equippable items go to their equipment slot; everything
    /// else goes into the equipped chest rig, then backpack. Order matters: list bags before loot.
    /// Placeholder until loadouts come from the player's stash.
    /// </summary>
    [CreateAssetMenu(fileName = "Loadout", menuName = "Extraction Shooter/Player Loadout")]
    public sealed class PlayerLoadout : ScriptableObject
    {
        [SerializeField] private List<LoadoutEntry> _items = new List<LoadoutEntry>();

        public IReadOnlyList<LoadoutEntry> Items => _items;
    }

    [Serializable]
    public sealed class LoadoutEntry
    {
        [SerializeField] private ItemDefinition _item;
        [SerializeField, Min(1)] private int _quantity = 1;

        public ItemDefinition Item => _item;
        public int Quantity => Mathf.Max(1, _quantity);
    }
}
