using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>Anything that fires visible hitscan shots (players, bots). Drives tracer effects on clients.</summary>
    public interface IShotSource
    {
        /// <summary>Client side. Raised for every confirmed projectile (origin, end point).</summary>
        event System.Action<Vector3, Vector3> ShotConfirmed;
    }
}
