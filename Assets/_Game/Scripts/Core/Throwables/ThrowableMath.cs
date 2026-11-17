using System;

namespace ExtractionShooter.Core.Throwables
{
    /// <summary>
    /// Numbers behind grenades: blast falloff, flash strength from distance and viewing angle, smoke growth and fade,
    /// the fuse, and cycling the selected grenade type. Shared by the server (damage, bots) and the owner's client
    /// (the flash overlay). Pure logic, unit-tested.
    /// </summary>
    public static class ThrowableMath
    {
        /// <summary>Number of <see cref="ThrowableKind"/> values.</summary>
        public const int KindCount = 3;

        /// <summary>
        /// Linear falloff with distance: 1 within <paramref name="innerRadius"/>, 0 at <paramref name="radius"/> and beyond.
        /// </summary>
        public static float Falloff(float distance, float innerRadius, float radius)
        {
            if (radius <= 0f || distance >= radius) return 0f;
            float inner = innerRadius < 0f ? 0f : innerRadius > radius ? radius : innerRadius;
            if (distance <= inner) return 1f;
            return 1f - (distance - inner) / (radius - inner);
        }

        /// <summary>Blast damage at <paramref name="distance"/> (0 outside the radius, never below 1 inside it).</summary>
        public static int BlastDamage(int maxDamage, float distance, float innerRadius, float radius)
        {
            float falloff = Falloff(distance, innerRadius, radius);
            if (maxDamage <= 0 || falloff <= 0f) return 0;
            int damage = (int)Math.Round(maxDamage * falloff, MidpointRounding.AwayFromZero);
            return damage < 1 ? 1 : damage;
        }

        /// <summary>
        /// How much a viewer facing <paramref name="angleDegrees"/> away from the flash takes in: 1 up to
        /// <paramref name="fullAngle"/>, falling to 0 at <paramref name="zeroAngle"/> (looking away).
        /// </summary>
        public static float FacingFactor(float angleDegrees, float fullAngle, float zeroAngle)
        {
            float angle = Math.Abs(angleDegrees);
            if (angle <= fullAngle) return 1f;
            if (angle >= zeroAngle || zeroAngle <= fullAngle) return 0f;
            return 1f - (angle - fullAngle) / (zeroAngle - fullAngle);
        }

        /// <summary>Flash strength 0..1: distance falloff times how directly the viewer looks at it.</summary>
        public static float FlashIntensity(float distance, float innerRadius, float radius, float angleDegrees, float fullAngle, float zeroAngle) =>
            Falloff(distance, innerRadius, radius) * FacingFactor(angleDegrees, fullAngle, zeroAngle);

        /// <summary>
        /// Seconds of white-out for a flash of <paramref name="intensity"/>: nothing below <paramref name="threshold"/>,
        /// otherwise from <paramref name="minSeconds"/> (a glance) to <paramref name="maxSeconds"/> (point blank, staring at it).
        /// </summary>
        public static float FlashSeconds(float intensity, float minSeconds, float maxSeconds, float threshold = 0.05f)
        {
            if (intensity < threshold || intensity <= 0f || maxSeconds <= 0f) return 0f;
            float t = intensity > 1f ? 1f : intensity;
            float min = minSeconds < 0f ? 0f : minSeconds > maxSeconds ? maxSeconds : minSeconds;
            return min + (maxSeconds - min) * t;
        }

        /// <summary>Overlay opacity <paramref name="elapsed"/> seconds into a white-out: full, then fades over the last part.</summary>
        public static float FlashOpacity(float elapsed, float totalSeconds, float fadeSeconds)
        {
            if (totalSeconds <= 0f || elapsed >= totalSeconds) return 0f;
            if (elapsed < 0f) return 1f;
            float fade = fadeSeconds <= 0f ? 0f : fadeSeconds > totalSeconds ? totalSeconds : fadeSeconds;
            float remaining = totalSeconds - elapsed;
            return fade <= 0f || remaining >= fade ? 1f : remaining / fade;
        }

        /// <summary>True once the fuse has burnt down.</summary>
        public static bool FuseExpired(double elapsedSeconds, float fuseSeconds) => elapsedSeconds >= fuseSeconds;

        /// <summary>
        /// Smoke cloud radius <paramref name="elapsed"/> seconds after it popped: grows (eased) to full over
        /// <paramref name="growSeconds"/>, holds, then thins out to nothing over the last <paramref name="fadeSeconds"/>.
        /// </summary>
        public static float SmokeRadius(float elapsed, float duration, float radius, float growSeconds, float fadeSeconds)
        {
            if (elapsed < 0f || elapsed >= duration || radius <= 0f) return 0f;

            float grow = growSeconds <= 0f ? 1f : Clamp01(elapsed / growSeconds);
            grow = 1f - (1f - grow) * (1f - grow); // ease out: puffs out fast, then settles
            float remaining = duration - elapsed;
            float fade = fadeSeconds <= 0f || remaining >= fadeSeconds ? 1f : remaining / fadeSeconds;
            return radius * grow * (0.5f + 0.5f * fade);
        }

        /// <summary>Smoke opacity 0..1: solid until the last <paramref name="fadeSeconds"/>, then clearing.</summary>
        public static float SmokeOpacity(float elapsed, float duration, float fadeSeconds) =>
            elapsed < 0f ? 0f : FlashOpacity(elapsed, duration, fadeSeconds);

        /// <summary>
        /// Whether the segment a-b passes within <paramref name="radius"/> of centre c (including either end inside it).
        /// </summary>
        public static bool SegmentIntersectsSphere(float ax, float ay, float az, float bx, float by, float bz,
            float cx, float cy, float cz, float radius)
        {
            if (radius <= 0f) return false;
            float dx = bx - ax, dy = by - ay, dz = bz - az;
            float fx = cx - ax, fy = cy - ay, fz = cz - az;
            float lengthSq = dx * dx + dy * dy + dz * dz;
            float t = lengthSq > 1e-8f ? Clamp01((fx * dx + fy * dy + fz * dz) / lengthSq) : 0f;
            float px = ax + dx * t - cx, py = ay + dy * t - cy, pz = az + dz * t - cz;
            return px * px + py * py + pz * pz <= radius * radius;
        }

        /// <summary>
        /// The next grenade type after <paramref name="current"/> that the player carries (<paramref name="counts"/> is
        /// indexed by <see cref="ThrowableKind"/>). When they carry none of the others, keeps the current one if carried,
        /// otherwise simply steps to the next type.
        /// </summary>
        public static ThrowableKind NextKind(ThrowableKind current, int[] counts)
        {
            int start = (int)current;
            for (int step = 1; step <= KindCount; step++)
            {
                int candidate = (start + step) % KindCount;
                if (counts != null && candidate < counts.Length && counts[candidate] > 0) return (ThrowableKind)candidate;
            }
            return (ThrowableKind)((start + 1) % KindCount);
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
