using ExtractionShooter.Core.Squad;
using UnityEngine;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// One palette for squad pings wherever they are drawn (world markers, compass, map), so a loot ping is the same
    /// gold everywhere: location cyan, loot gold, enemy and danger red.
    /// </summary>
    internal static class PingStyle
    {
        public static readonly Color Location = new Color(0.55f, 0.85f, 1f, 1f);
        public static readonly Color Loot = new Color(1f, 0.78f, 0.3f, 1f);
        public static readonly Color Enemy = new Color(0.95f, 0.3f, 0.25f, 1f);
        public static readonly Color Danger = new Color(1f, 0.18f, 0.15f, 1f);
        public static readonly Color Outline = new Color(0f, 0f, 0f, 0.7f);

        public static Color ColorOf(PingKind kind)
        {
            switch (kind)
            {
                case PingKind.Loot: return Loot;
                case PingKind.Enemy: return Enemy;
                case PingKind.Danger: return Danger;
                default: return Location;
            }
        }
    }
}
