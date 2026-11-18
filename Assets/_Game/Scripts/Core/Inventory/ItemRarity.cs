using System;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>
    /// Delta Force-style rarity tiers, lowest to highest. Shown as the item's colour everywhere it appears.
    /// </summary>
    public enum ItemRarity : byte
    {
        White = 0,
        Green = 1,
        Blue = 2,
        Purple = 3,
        Gold = 4,
        Red = 5,
    }

    /// <summary>An item's rarity as set in its data: derived from its value (<see cref="Auto"/>) or a fixed tier.</summary>
    public enum RarityOverride : byte
    {
        Auto = 0,
        White = 1,
        Green = 2,
        Blue = 3,
        Purple = 4,
        Gold = 5,
        Red = 6,
    }

    /// <summary>
    /// Derives an item's rarity from its value per grid cell, so a small valuable outranks a bulky one of the same price.
    /// Thresholds are chosen so the item catalog spreads over every tier and gold/red stay for the best loot:
    /// <list type="bullet">
    /// <item>White under 40 per cell (food, bandages, small bags)</item>
    /// <item>Green from 40 (basic ammo, light gear, medkits)</item>
    /// <item>Blue from 130 (most guns, standard ammo, heavy helmet)</item>
    /// <item>Purple from 330 (attachments, good ammo, marksman rifles, watches)</item>
    /// <item>Gold from 700 (top armor-piercing ammo, gold bars, laptops)</item>
    /// <item>Red from 2000 (encrypted drives, prototype chips, the vault key)</item>
    /// </list>
    /// </summary>
    public static class RarityRules
    {
        public const int GreenFrom = 40;
        public const int BlueFrom = 130;
        public const int PurpleFrom = 330;
        public const int GoldFrom = 700;
        public const int RedFrom = 2000;

        /// <summary>The tier for a value per grid cell.</summary>
        public static ItemRarity FromCellValue(double valuePerCell)
        {
            if (valuePerCell >= RedFrom) return ItemRarity.Red;
            if (valuePerCell >= GoldFrom) return ItemRarity.Gold;
            if (valuePerCell >= PurpleFrom) return ItemRarity.Purple;
            if (valuePerCell >= BlueFrom) return ItemRarity.Blue;
            if (valuePerCell >= GreenFrom) return ItemRarity.Green;
            return ItemRarity.White;
        }

        /// <summary>
        /// The tier from an item's stored value per unit.
        /// <paramref name="unitsPerCell"/> is how many units are judged together: 1 for most items, a full stack for ammo
        /// (a single round is worth next to nothing; a stack of AP rounds is not).
        /// </summary>
        public static ItemRarity FromValue(int unitValue, int unitsPerCell, ItemSize size)
        {
            long total = (long)Math.Max(0, unitValue) * Math.Max(1, unitsPerCell);
            int area = Math.Max(1, size.Width * size.Height);
            return FromCellValue(total / (double)area);
        }

        /// <summary>The override's tier, or the value-derived one for <see cref="RarityOverride.Auto"/>.</summary>
        public static ItemRarity Resolve(RarityOverride rarityOverride, int unitValue, int unitsPerCell, ItemSize size)
        {
            if (rarityOverride > RarityOverride.Auto && rarityOverride <= RarityOverride.Red)
                return (ItemRarity)((int)rarityOverride - 1);
            return FromValue(unitValue, unitsPerCell, size);
        }

        /// <summary>Player-facing name of a tier.</summary>
        public static string DisplayName(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Green: return "Uncommon";
                case ItemRarity.Blue: return "Rare";
                case ItemRarity.Purple: return "Epic";
                case ItemRarity.Gold: return "Legendary";
                case ItemRarity.Red: return "Mythic";
                default: return "Common";
            }
        }

        /// <summary>The tier's colour as a hex string ("#rrggbb"), for rich text.</summary>
        public static string ColorHex(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Green: return "#5fb85f";
                case ItemRarity.Blue: return "#4f8fe0";
                case ItemRarity.Purple: return "#a86ae0";
                case ItemRarity.Gold: return "#e0b040";
                case ItemRarity.Red: return "#e0504a";
                default: return "#b4b8bc";
            }
        }
    }
}
