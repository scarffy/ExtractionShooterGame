using ExtractionShooter.Gameplay.Session;
using FishNet.Object;

namespace ExtractionShooter.Gameplay.Raid
{
    /// <summary>
    /// Server only: finds the credits that pay for a paid extraction. A raid character and its player's
    /// <see cref="PlayerSession"/> are owned by the same connection, so the session is looked up by owner.
    /// </summary>
    internal static class ExtractionWallet
    {
        /// <summary>The trading component (credits) of the session owned by the character's owner, or null.</summary>
        public static PlayerTrading Find(NetworkBehaviour character)
        {
            if (character == null || character.OwnerId < 0) return null;

            foreach (PlayerSquad squad in PlayerSquad.ServerAll)
            {
                if (squad == null || squad.OwnerId != character.OwnerId) continue;
                PlayerSession session = squad.Session;
                if (session != null && session.Trading != null) return session.Trading;
            }
            return null;
        }

        /// <summary>Credits the character's player has, or 0 when no session is found.</summary>
        public static long CreditsOf(NetworkBehaviour character)
        {
            PlayerTrading trading = Find(character);
            return trading != null ? trading.Money : 0L;
        }
    }
}
