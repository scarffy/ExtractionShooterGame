using System.Collections.Generic;

namespace ExtractionShooter.Core.Hideout
{
    /// <summary>Whether a hideout upgrade (stash size level) can be bought.</summary>
    public enum UpgradeCheck
    {
        Ok,
        MaxLevel,
        NotEnoughCredits,
    }

    /// <summary>
    /// Level rules for bought hideout upgrades. Level 0 is the free starting size; each further level is bought in
    /// order at its price (the price of level 0 is ignored). Pure logic, unit-tested.
    /// </summary>
    public static class HideoutUpgrades
    {
        /// <summary>A saved level kept inside the levels that exist (old saves: 0; levels removed from the data: the top one).</summary>
        public static int ClampLevel(int level, int levelCount) =>
            levelCount <= 0 ? 0 : level < 0 ? 0 : level >= levelCount ? levelCount - 1 : level;

        /// <summary>The next level's price, or false at the top level.</summary>
        public static bool TryGetNextPrice(int level, IReadOnlyList<long> prices, out long price)
        {
            price = 0;
            if (prices == null) return false;
            int next = ClampLevel(level, prices.Count) + 1;
            if (next >= prices.Count) return false;
            price = prices[next] < 0 ? 0 : prices[next];
            return true;
        }

        public static UpgradeCheck Check(int level, IReadOnlyList<long> prices, long credits)
        {
            if (!TryGetNextPrice(level, prices, out long price)) return UpgradeCheck.MaxLevel;
            return price > credits ? UpgradeCheck.NotEnoughCredits : UpgradeCheck.Ok;
        }
    }
}
