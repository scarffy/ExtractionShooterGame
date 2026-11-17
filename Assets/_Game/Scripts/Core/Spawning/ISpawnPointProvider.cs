using System.Collections.Generic;
using UnityEngine;

namespace ExtractionShooter.Core.Spawning
{
    /// <summary>
    /// Supplies world poses for spawning and respawning players.
    /// </summary>
    public interface ISpawnPointProvider
    {
        Pose GetNextSpawnPose();

        /// <summary>The spawn point farthest from every threat (players, bots); rotates when all are equally safe.</summary>
        Pose GetSafestSpawnPose(IReadOnlyList<Vector3> threats);
    }
}
