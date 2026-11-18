namespace ExtractionShooter.Core.Raid
{
    /// <summary>How a raid ended for one player.</summary>
    public enum RaidResult : byte
    {
        Extracted = 0,
        KilledByPlayer = 1,
        KilledByBot = 2,
        MissingInAction = 3,
        Left = 4,
    }

    /// <summary>
    /// End-of-raid report sent to the player (plain fields so the network layer serializes it as is).
    /// Values are credits at listed item value.
    /// </summary>
    public struct RaidSummary
    {
        public RaidResult Result;
        public int Seconds;
        public int PlayerKills;
        public int BotKills;
        /// <summary>Value of the gear deployed with.</summary>
        public long DeployedValue;
        /// <summary>Value brought back (0 unless extracted; the safe box is not counted, it is never at risk).</summary>
        public long ExtractedValue;
        /// <summary>Credits paid for a paid exit (0 for free exits).</summary>
        public long ExitFee;
        /// <summary>Who killed the player, when killed by a player.</summary>
        public string KilledBy;
        /// <summary>Where the player extracted.</summary>
        public string ExtractedAt;

        public long Profit => ExtractedValue - DeployedValue - ExitFee;

        public string ResultText
        {
            get
            {
                switch (Result)
                {
                    case RaidResult.Extracted: return string.IsNullOrEmpty(ExtractedAt) ? "EXTRACTED" : $"EXTRACTED · {ExtractedAt.ToUpperInvariant()}";
                    case RaidResult.KilledByPlayer: return string.IsNullOrEmpty(KilledBy) ? "KILLED IN ACTION" : $"KILLED BY {KilledBy}";
                    case RaidResult.KilledByBot: return "KILLED IN ACTION";
                    case RaidResult.MissingInAction: return "MISSING IN ACTION";
                    default: return "LEFT THE RAID";
                }
            }
        }

        /// <summary>"12:05".</summary>
        public string TimeText => $"{Seconds / 60:00}:{Seconds % 60:00}";
    }
}
