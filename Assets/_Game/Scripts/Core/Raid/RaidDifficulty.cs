using System;

namespace ExtractionShooter.Core.Raid
{
    /// <summary>How hard a raid is: more and sharper bots, more loot.</summary>
    public enum RaidDifficulty : byte
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }

    /// <summary>
    /// The two difficulties a map offers. A map is either Easy/Normal or Normal/Hard, never Easy/Hard or all three,
    /// so this is the only way to state a map's difficulties.
    /// </summary>
    public enum MapDifficulties : byte
    {
        EasyNormal = 0,
        NormalHard = 1,
    }

    public static class RaidDifficulties
    {
        public static RaidDifficulty Lower(this MapDifficulties map) =>
            map == MapDifficulties.NormalHard ? RaidDifficulty.Normal : RaidDifficulty.Easy;

        public static RaidDifficulty Upper(this MapDifficulties map) =>
            map == MapDifficulties.NormalHard ? RaidDifficulty.Hard : RaidDifficulty.Normal;

        /// <summary>Whether the map can be played at this difficulty.</summary>
        public static bool Allows(this MapDifficulties map, RaidDifficulty difficulty) =>
            difficulty == map.Lower() || difficulty == map.Upper();

        /// <summary>The nearest difficulty the map offers (Hard on an Easy/Normal map is Normal).</summary>
        public static RaidDifficulty Clamp(this MapDifficulties map, RaidDifficulty difficulty)
        {
            if (map.Allows(difficulty)) return difficulty;
            return difficulty < map.Lower() ? map.Lower() : map.Upper();
        }

        public static string DisplayName(this RaidDifficulty difficulty)
        {
            switch (difficulty)
            {
                case RaidDifficulty.Easy: return "Easy";
                case RaidDifficulty.Hard: return "Hard";
                default: return "Normal";
            }
        }

        /// <summary>Parses "easy" / "Normal" / "HARD" (case-insensitive). False for anything else.</summary>
        public static bool TryParse(string text, out RaidDifficulty difficulty)
        {
            difficulty = RaidDifficulty.Normal;
            if (string.IsNullOrWhiteSpace(text)) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "easy": difficulty = RaidDifficulty.Easy; return true;
                case "normal": difficulty = RaidDifficulty.Normal; return true;
                case "hard": difficulty = RaidDifficulty.Hard; return true;
                default: return false;
            }
        }
    }

    /// <summary>
    /// What a difficulty changes, relative to Normal (1 = unchanged). Applied by the server when a raid opens:
    /// bot count and skill, and extra (or fewer) loot rolls per container.
    /// </summary>
    [Serializable]
    public struct DifficultyModifiers
    {
        /// <summary>Bots alive at once, as a multiple of the map's base count.</summary>
        public float BotCountMultiplier;
        /// <summary>Bot aim error multiplier: below 1 is more accurate.</summary>
        public float BotAimErrorMultiplier;
        /// <summary>Bot reaction time multiplier: below 1 fires sooner after spotting you.</summary>
        public float BotReactionMultiplier;
        /// <summary>Bot sight and hearing range multiplier.</summary>
        public float BotAwarenessMultiplier;
        /// <summary>Bot headshot chance multiplier.</summary>
        public float BotHeadshotMultiplier;
        /// <summary>Extra loot rolls per container (negative for fewer; a container always gets at least one).</summary>
        public int LootRollBonus;

        public static DifficultyModifiers Neutral => new DifficultyModifiers
        {
            BotCountMultiplier = 1f, BotAimErrorMultiplier = 1f, BotReactionMultiplier = 1f,
            BotAwarenessMultiplier = 1f, BotHeadshotMultiplier = 1f, LootRollBonus = 0,
        };

        /// <summary>Built-in values; the scene's difficulty settings asset can override them.</summary>
        public static DifficultyModifiers DefaultFor(RaidDifficulty difficulty)
        {
            switch (difficulty)
            {
                case RaidDifficulty.Easy:
                    return new DifficultyModifiers
                    {
                        BotCountMultiplier = 0.6f, BotAimErrorMultiplier = 1.5f, BotReactionMultiplier = 1.5f,
                        BotAwarenessMultiplier = 0.8f, BotHeadshotMultiplier = 0.5f, LootRollBonus = -1,
                    };
                case RaidDifficulty.Hard:
                    return new DifficultyModifiers
                    {
                        BotCountMultiplier = 1.5f, BotAimErrorMultiplier = 0.7f, BotReactionMultiplier = 0.7f,
                        BotAwarenessMultiplier = 1.2f, BotHeadshotMultiplier = 1.5f, LootRollBonus = 1,
                    };
                default:
                    return Neutral;
            }
        }

        /// <summary>Bots for a map whose base count is <paramref name="baseCount"/> (never negative; 0 stays 0).</summary>
        public int BotCount(int baseCount) =>
            baseCount <= 0 ? 0 : Math.Max(1, (int)Math.Round(baseCount * Math.Max(0f, BotCountMultiplier), MidpointRounding.AwayFromZero));

        /// <summary>Rolls for a container that would roll <paramref name="baseRolls"/> (at least 1 when it had any).</summary>
        public int Rolls(int baseRolls) => baseRolls <= 0 ? 0 : Math.Max(1, baseRolls + LootRollBonus);
    }
}
