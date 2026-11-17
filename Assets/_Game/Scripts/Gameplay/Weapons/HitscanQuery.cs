using ExtractionShooter.Gameplay.Combat;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>
    /// Shared hitscan rules for every shooter (players and bots): which collider a ray stops at, and shot spread.
    /// </summary>
    public static class HitscanQuery
    {
        /// <summary>
        /// Nearest valid hit. Characters are hit through their hitbox triggers; their movement capsule
        /// (CharacterController), the shooter itself and any other trigger without a Hitbox are ignored.
        /// </summary>
        public static bool TryGetFirstHit(Vector3 origin, Vector3 direction, float range, LayerMask mask, Transform shooter,
            RaycastHit[] buffer, out RaycastHit closest, out Hitbox closestHitbox)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, buffer, range, mask, QueryTriggerInteraction.Collide);

            closest = default;
            closestHitbox = null;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = buffer[i];
                Collider collider = hit.collider;
                if (hit.distance >= closestDistance || collider.transform.IsChildOf(shooter)) continue;
                if (collider is CharacterController) continue;

                Hitbox hitbox = collider.GetComponent<Hitbox>();
                if (collider.isTrigger && hitbox == null) continue;

                closest = hit;
                closestHitbox = hitbox;
                closestDistance = hit.distance;
            }
            return closestDistance < float.MaxValue;
        }

        /// <summary>Random direction inside a cone of <paramref name="halfAngle"/> degrees, uniform over its area.</summary>
        public static Vector3 ApplySpread(Vector3 direction, float halfAngle)
        {
            if (halfAngle <= 0f) return direction;

            Vector3 axis = Vector3.Cross(direction, Vector3.up);
            if (axis.sqrMagnitude < 0.0001f) axis = Vector3.Cross(direction, Vector3.right);

            float tilt = halfAngle * Mathf.Sqrt(Random.value);
            float roll = Random.value * 360f;
            Vector3 tilted = Quaternion.AngleAxis(tilt, axis.normalized) * direction;
            return (Quaternion.AngleAxis(roll, direction) * tilted).normalized;
        }
    }
}
