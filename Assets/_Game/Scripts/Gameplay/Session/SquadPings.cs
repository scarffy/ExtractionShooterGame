using System;
using ExtractionShooter.Core.Squad;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>
    /// Client side: the pings this client's squad has placed (its own included), as the server sent them through
    /// <see cref="PlayerPings"/>. The HUD markers, the compass and the map read <see cref="Board"/>; only squadmates
    /// ever receive a ping, so nothing here is visible to enemies. Cleared when the local player spawns or despawns.
    /// </summary>
    public static class SquadPings
    {
        private static readonly PingBoard ClientBoard = new PingBoard();

        /// <summary>A ping arrived or was upgraded to danger (the HUD plays its sound).</summary>
        public static event Action<SquadPing> Received;

        /// <summary>Live pings, oldest first. Call <see cref="Prune"/> before reading to drop expired ones.</summary>
        public static PingBoard Board => ClientBoard;

        /// <summary>Drops expired pings (in the local clock). Cheap; every view calls it before reading.</summary>
        public static void Prune() => ClientBoard.Prune(Time.timeAsDouble);

        /// <summary>A ping's world position.</summary>
        public static Vector3 PositionOf(in SquadPing ping) => new Vector3(ping.X, ping.Y, ping.Z);

        internal static void ClientReceive(in SquadPing ping)
        {
            ClientBoard.Upsert(ping);
            Received?.Invoke(ping);
        }

        internal static void ClientClear() => ClientBoard.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ClientBoard.Clear();
            Received = null;
        }
    }
}
