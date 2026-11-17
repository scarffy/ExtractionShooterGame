using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>
    /// Shared pieces of the Delta Force-style loadout layout used by the hideout stash and the raid inventory:
    /// where each equipment slot is drawn, and the "Chest rig: 6/12" style section titles.
    /// </summary>
    internal static class LoadoutLayout
    {
        private static readonly (EquipmentSlot slot, string host)[] Hosts =
        {
            (EquipmentSlot.Helmet, "equip-helmet"),
            (EquipmentSlot.BodyArmor, "equip-armor"),
            (EquipmentSlot.Pistol, "equip-pistol"),
            (EquipmentSlot.Melee, "equip-melee"),
            (EquipmentSlot.Primary1, "equip-primary1"),
            (EquipmentSlot.Primary2, "equip-primary2"),
            (EquipmentSlot.ChestRig, "equip-rig"),
            (EquipmentSlot.Backpack, "equip-backpack"),
        };

        /// <summary>Places every equipment slot of <paramref name="view"/> in its host element (names prefixed by <paramref name="prefix"/>).</summary>
        public static void HostEquipment(SlotGridView view, VisualElement root, string prefix)
        {
            foreach ((EquipmentSlot slot, string host) in Hosts)
                view.SetSlotHost((int)slot, root.Q<VisualElement>(prefix + host));
        }

        /// <summary>"Small Chest Rig: 6/12" — cells in use out of the grid's capacity; just the fallback when empty.</summary>
        public static string SectionTitle(ItemContainer container, string fallback)
        {
            if (container == null) return fallback;
            SlotInventory layout = container.Layout;
            return layout != null ? $"{container.DisplayName}:  {UsedCells(layout)}/{layout.Capacity}" : container.DisplayName;
        }

        /// <summary>Cells covered by items.</summary>
        public static int UsedCells(SlotInventory layout)
        {
            int used = 0;
            for (int cell = 0; cell < layout.Capacity; cell++)
                if (layout.AnchorAt(cell) >= 0) used++;
            return used;
        }
    }
}
