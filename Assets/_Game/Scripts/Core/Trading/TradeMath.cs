using System;

namespace ExtractionShooter.Core.Trading
{
    /// <summary>
    /// Trader prices. Pure logic, unit-tested. Money is a whole number of credits; totals are rounded once
    /// per trade (not per unit) so selling cheap rounds one at a time never earns more than selling the stack.
    /// </summary>
    public static class TradeMath
    {
        /// <summary>
        /// What the trader pays: value × quantity × sell ratio × condition, rounded down.
        /// </summary>
        /// <param name="condition">0–1 share of the item left (armor durability); 1 for undamaged items.</param>
        public static long SellPrice(int unitValue, int quantity, float sellRatio, float condition = 1f)
        {
            if (unitValue <= 0 || quantity <= 0 || sellRatio <= 0f) return 0;
            double total = (double)unitValue * quantity * sellRatio * Clamp01(condition);
            return (long)Math.Floor(total + 1e-9);
        }

        /// <summary>What the trader charges: value × quantity × markup, rounded up, and never below 1 per trade.</summary>
        public static long BuyPrice(int unitValue, int quantity, float markup)
        {
            if (quantity <= 0) return 0;
            double total = (double)Math.Max(0, unitValue) * quantity * Math.Max(0f, markup);
            return Math.Max(1L, (long)Math.Ceiling(total - 1e-9));
        }

        /// <summary>Share of an item left, for items with durability. Items without durability count as whole.</summary>
        public static float Condition(int durability, int maxDurability) =>
            maxDurability > 0 ? Clamp01((float)durability / maxDurability) : 1f;

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
