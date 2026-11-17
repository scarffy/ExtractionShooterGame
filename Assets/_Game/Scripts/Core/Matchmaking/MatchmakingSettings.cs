using System;

namespace ExtractionShooter.Core.Matchmaking
{
    /// <summary>Tunables for <see cref="MatchmakingService"/>. Values are clamped to sane ranges on construction.</summary>
    public sealed class MatchmakingSettings
    {
        public MatchmakingSettings(int maxPlayers = 12, int minPlayers = 1, double countdownSeconds = 15,
            double lateJoinSeconds = 120, double raidDurationSeconds = 1200)
        {
            MaxPlayers = Math.Max(1, maxPlayers);
            MinPlayers = Math.Min(MaxPlayers, Math.Max(1, minPlayers));
            CountdownSeconds = Math.Max(0, countdownSeconds);
            RaidDurationSeconds = Math.Max(10, raidDurationSeconds);
            LateJoinSeconds = Math.Min(RaidDurationSeconds, Math.Max(0, lateJoinSeconds));
        }

        /// <summary>Most players in one raid instance.</summary>
        public int MaxPlayers { get; }

        /// <summary>Players needed before the launch countdown starts.</summary>
        public int MinPlayers { get; }

        /// <summary>Seconds between reaching <see cref="MinPlayers"/> and launching (a full raid launches at once).</summary>
        public double CountdownSeconds { get; }

        /// <summary>Seconds after a raid starts during which queued squads may still join it while it has room.</summary>
        public double LateJoinSeconds { get; }

        /// <summary>Length of a raid instance; characters still inside when it ends are missing in action.</summary>
        public double RaidDurationSeconds { get; }
    }
}
