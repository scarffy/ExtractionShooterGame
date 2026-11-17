using ExtractionShooter.Core.Combat;
using ExtractionShooter.Gameplay.Combat;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>
    /// Shared bullet trace for every shooter (players and bots): follows the drop curve of <see cref="Ballistics"/> as a
    /// chain of straight raycasts (<see cref="HitscanQuery.TryGetFirstHit"/> per segment) up to the weapon's range.
    /// Resolved instantly, like hitscan; only the path bends.
    /// </summary>
    public static class BallisticTrace
    {
        /// <summary>
        /// Traces one bullet. Returns true on a hit; <paramref name="end"/> is the hit point, or where the bullet's
        /// range ran out. <paramref name="impactDirection"/> is the bullet's heading at the end (for damage direction).
        /// </summary>
        /// <param name="direction">Unit firing direction.</param>
        /// <param name="muzzleVelocity">Metres per second.</param>
        public static bool TryTrace(Vector3 origin, Vector3 direction, float muzzleVelocity, float range, LayerMask mask,
            Transform shooter, RaycastHit[] buffer, out RaycastHit hit, out Hitbox hitbox, out Vector3 end, out Vector3 impactDirection)
        {
            const float gravity = Ballistics.StandardGravity;
            float flightTime = Ballistics.FlightTime(range, muzzleVelocity);
            int segments = Ballistics.SegmentCount(flightTime, gravity);

            System.Numerics.Vector3 start = ToNumerics(origin), heading = ToNumerics(direction);
            Vector3 from = origin;
            impactDirection = direction;
            for (int i = 1; i <= segments; i++)
            {
                float time = Ballistics.SegmentEndTime(flightTime, i, segments);
                Vector3 to = ToUnity(Ballistics.PointAt(start, heading, muzzleVelocity, gravity, time));
                Vector3 step = to - from;
                float length = step.magnitude;
                if (length > 0.0001f)
                {
                    Vector3 stepDirection = step / length;
                    impactDirection = stepDirection;
                    if (HitscanQuery.TryGetFirstHit(from, stepDirection, length, mask, shooter, buffer, out hit, out hitbox))
                    {
                        end = hit.point;
                        return true;
                    }
                }
                from = to;
            }

            hit = default;
            hitbox = null;
            end = from;
            return false;
        }

        private static System.Numerics.Vector3 ToNumerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        private static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
