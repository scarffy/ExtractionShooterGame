using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>
    /// Puts an item's rarity colour on a tile (slot, grid item, trader offer) as a USS class: <c>rarity--white</c> to
    /// <c>rarity--red</c> (InventoryScreen.uss tints the background and draws a coloured bottom bar, Delta Force style).
    /// </summary>
    public static class RarityStyle
    {
        private static readonly string[] Classes =
            { "rarity--white", "rarity--green", "rarity--blue", "rarity--purple", "rarity--gold", "rarity--red" };

        public static string ClassName(ItemRarity rarity)
        {
            int index = (int)rarity;
            return index >= 0 && index < Classes.Length ? Classes[index] : Classes[0];
        }

        /// <summary>Shows the item's rarity on the tile (or none for a null item).</summary>
        public static void Apply(VisualElement tile, ItemDefinition item)
        {
            if (tile == null) return;
            Clear(tile);
            if (item != null) tile.AddToClassList(ClassName(item.Rarity));
        }

        public static void Clear(VisualElement tile)
        {
            if (tile == null) return;
            foreach (string className in Classes) tile.RemoveFromClassList(className);
        }

        /// <summary>The rarity's name in its colour, for rich-text labels ("Epic" in purple).</summary>
        public static string RichName(ItemRarity rarity) =>
            $"<color={RarityRules.ColorHex(rarity)}>{RarityRules.DisplayName(rarity)}</color>";
    }
}
