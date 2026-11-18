namespace ExtractionShooter.Core.Squad
{
    /// <summary>What a squad ping marks. Decided by the server from what the ray hit (or a double press for danger).</summary>
    public enum PingKind : byte
    {
        /// <summary>A spot on the ground or a wall: "go here", "look here".</summary>
        Location = 0,

        /// <summary>An enemy player or bot was under the crosshair. Marks the spot; it does not follow them.</summary>
        Enemy = 1,

        /// <summary>A crate, corpse or other lootable container.</summary>
        Loot = 2,

        /// <summary>Double press: a warning marker at the spot.</summary>
        Danger = 3,
    }

    /// <summary>Squad ping tunables and wording shared by the server, the HUD, the compass and the map. Pure logic.</summary>
    public static class PingRules
    {
        /// <summary>Furthest a ping reaches from the eyes (metres).</summary>
        public const float MaxDistance = 300f;

        /// <summary>Two presses at most this far apart (seconds) make the second one a danger ping.</summary>
        public const double DoublePressWindow = 0.4;

        /// <summary>Shortest time between two new pings from one player (seconds). Upgrading to danger is exempt.</summary>
        public const double MinInterval = 0.5;

        /// <summary>
        /// How long after a ping the server still accepts the danger upgrade (seconds): the double-press window plus
        /// slack for the two requests arriving with different latency.
        /// </summary>
        public const double UpgradeWindow = DoublePressWindow + 0.3;

        /// <summary>Most pings one player can have on the board; a new one replaces their oldest.</summary>
        public const int MaxActivePerPlayer = 3;

        /// <summary>Seconds a ping stays up.</summary>
        public const float Lifetime = 10f;

        public const int KindCount = 4;

        /// <summary>The marker's word ("Loot · 34 m").</summary>
        public static string Label(PingKind kind)
        {
            switch (kind)
            {
                case PingKind.Enemy: return "Enemy";
                case PingKind.Loot: return "Loot";
                case PingKind.Danger: return "Danger";
                default: return "Location";
            }
        }

        /// <summary>Red markers: enemies and danger.</summary>
        public static bool IsHostile(PingKind kind) => kind == PingKind.Enemy || kind == PingKind.Danger;

        /// <summary>A kind received over the network, with anything unknown read as a plain location.</summary>
        public static PingKind FromByte(byte value) => value < KindCount ? (PingKind)value : PingKind.Location;
    }
}
