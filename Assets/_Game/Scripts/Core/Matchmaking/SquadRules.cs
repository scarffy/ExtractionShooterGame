namespace ExtractionShooter.Core.Matchmaking
{
    /// <summary>Squad-related rules shared by gameplay and UI. Pure logic, unit-tested.</summary>
    public static class SquadRules
    {
        /// <summary>Squad id meaning "not in a squad".</summary>
        public const int NoSquad = 0;

        /// <summary>
        /// Whether a hit from one player on another deals damage. Players in different squads (or in none) always can;
        /// squadmates only with friendly fire on. Hurting yourself (a grenade at your feet) is always allowed.
        /// </summary>
        public static bool AllowsDamage(int attackerSquad, int victimSquad, bool sameClient, bool friendlyFire) =>
            sameClient || friendlyFire || attackerSquad == NoSquad || attackerSquad != victimSquad;

        /// <summary>Player-facing text for a refused squad request.</summary>
        public static string Describe(SquadResult result)
        {
            switch (result)
            {
                case SquadResult.Ok: return string.Empty;
                case SquadResult.NotInSquad: return "You are not in a squad.";
                case SquadResult.NotFound: return "No squad with that code.";
                case SquadResult.Full: return "That squad is full.";
                case SquadResult.NotLeader: return "Only the squad leader can do that.";
                case SquadResult.Locked: return "That squad is searching for a match.";
                case SquadResult.NoInvite: return "The invite is no longer valid.";
                case SquadResult.InvalidTarget: return "You cannot invite yourself.";
                case SquadResult.AlreadyMember: return "Already in that squad.";
                default: return result.ToString();
            }
        }

        /// <summary>One line for the lobby: "Searching… 3/12 players, starting in 12 s", "Raid in progress — next raid in ~04:10".</summary>
        public static string DescribeQueue(QueueStatus status)
        {
            switch (status.Phase)
            {
                case MatchmakingPhase.Searching:
                    return $"Searching… {status.PlayersFound}/{status.Capacity} players";
                case MatchmakingPhase.Starting:
                    return $"Searching… {status.PlayersFound}/{status.Capacity} players, starting in {status.SecondsLeft} s";
                case MatchmakingPhase.WaitingForRaid:
                    return $"Raid in progress — next raid in ~{status.SecondsLeft / 60:00}:{status.SecondsLeft % 60:00}";
                default:
                    return string.Empty;
            }
        }
    }
}
