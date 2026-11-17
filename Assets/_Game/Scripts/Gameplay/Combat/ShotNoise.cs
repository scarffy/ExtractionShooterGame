using System;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Combat
{
    /// <summary>
    /// Server-side "a gun went off here" broadcast, so AI can hear gunfire. Weapons report every shot with how far it
    /// carries; listeners (bots) decide whether they heard it.
    /// </summary>
    public static class ShotNoise
    {
        /// <summary>Server: (where, how far it can be heard in metres, shooter's client id or -1 for bots).</summary>
        public static event Action<Vector3, float, int> ServerShotFired;

        public static void ServerReport(Vector3 origin, float audibleRange, int shooterClientId) =>
            ServerShotFired?.Invoke(origin, audibleRange, shooterClientId);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ServerShotFired = null;
    }
}
