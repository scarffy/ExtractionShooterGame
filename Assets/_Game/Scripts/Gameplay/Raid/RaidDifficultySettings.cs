using ExtractionShooter.Core.Raid;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Raid
{
    /// <summary>
    /// What each raid difficulty changes: bot count and skill, and loot rolls per container. The map's
    /// <see cref="Session.SquadLobby"/> says which two difficulties it offers; the bot and loot spawners read these
    /// values when a raid opens.
    /// </summary>
    [CreateAssetMenu(fileName = "RaidDifficultySettings", menuName = "Extraction Shooter/Raid Difficulty Settings")]
    public sealed class RaidDifficultySettings : ScriptableObject
    {
        [SerializeField] private Tuning _easy = Tuning.From(DifficultyModifiers.DefaultFor(RaidDifficulty.Easy));
        [SerializeField] private Tuning _normal = Tuning.From(DifficultyModifiers.DefaultFor(RaidDifficulty.Normal));
        [SerializeField] private Tuning _hard = Tuning.From(DifficultyModifiers.DefaultFor(RaidDifficulty.Hard));

        public DifficultyModifiers For(RaidDifficulty difficulty)
        {
            switch (difficulty)
            {
                case RaidDifficulty.Easy: return _easy.ToModifiers();
                case RaidDifficulty.Hard: return _hard.ToModifiers();
                default: return _normal.ToModifiers();
            }
        }

        [System.Serializable]
        private struct Tuning
        {
            [Tooltip("Bots alive at once, as a multiple of the map's base count.")]
            [Range(0f, 3f)] public float _botCount;

            [Tooltip("Bot aim error: below 1 is more accurate.")]
            [Range(0.2f, 3f)] public float _botAimError;

            [Tooltip("Bot reaction time: below 1 fires sooner after spotting you.")]
            [Range(0.2f, 3f)] public float _botReaction;

            [Tooltip("Bot sight and hearing range.")]
            [Range(0.3f, 2f)] public float _botAwareness;

            [Tooltip("Bot headshot chance.")]
            [Range(0f, 3f)] public float _botHeadshot;

            [Tooltip("Extra loot rolls per container (negative for fewer; a container always gets at least one).")]
            [Range(-3, 3)] public int _lootRollBonus;

            public static Tuning From(DifficultyModifiers modifiers) => new Tuning
            {
                _botCount = modifiers.BotCountMultiplier, _botAimError = modifiers.BotAimErrorMultiplier,
                _botReaction = modifiers.BotReactionMultiplier, _botAwareness = modifiers.BotAwarenessMultiplier,
                _botHeadshot = modifiers.BotHeadshotMultiplier, _lootRollBonus = modifiers.LootRollBonus,
            };

            public DifficultyModifiers ToModifiers() => new DifficultyModifiers
            {
                BotCountMultiplier = _botCount, BotAimErrorMultiplier = _botAimError, BotReactionMultiplier = _botReaction,
                BotAwarenessMultiplier = _botAwareness, BotHeadshotMultiplier = _botHeadshot, LootRollBonus = _lootRollBonus,
            };
        }
    }
}
