using System;

namespace ExtractionShooter.Core.Navigation
{
    /// <summary>
    /// Compass and map arithmetic shared by the HUD compass, the map and the tactical panel.
    /// World convention (Unity): +Z is north, +X is east, yaw 0 faces north and grows clockwise seen from above,
    /// so a bearing is simply the yaw wrapped into [0, 360).
    /// </summary>
    public static class NavigationMath
    {
        private const float RadToDeg = (float)(180.0 / Math.PI);

        private static readonly string[] PointLabels = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>Wraps any angle in degrees into [0, 360).</summary>
        public static float NormalizeDegrees(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return 0f;
            float wrapped = degrees % 360f;
            if (wrapped < 0f) wrapped += 360f;
            // -1e-6 % 360 + 360 rounds to exactly 360 in single precision.
            return wrapped >= 360f ? 0f : wrapped;
        }

        /// <summary>Compass bearing (0 = north, 90 = east) a character with this yaw faces.</summary>
        public static float BearingFromYaw(float yawDegrees) => NormalizeDegrees(yawDegrees);

        /// <summary>Compass bearing from one point on the ground plane to another; 0 when they coincide.</summary>
        public static float BearingTo(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX, dz = toZ - fromZ;
            if (dx == 0f && dz == 0f) return 0f;
            return NormalizeDegrees((float)Math.Atan2(dx, dz) * RadToDeg);
        }

        /// <summary>Distance on the ground plane (height ignored).</summary>
        public static float GroundDistance(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX, dz = toZ - fromZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Shortest signed turn from one bearing to another, in (-180, 180]; positive is clockwise.</summary>
        public static float DeltaAngle(float fromDegrees, float toDegrees)
        {
            float delta = NormalizeDegrees(toDegrees - fromDegrees);
            return delta > 180f ? delta - 360f : delta;
        }

        /// <summary>The nearest of the eight compass points (N, NE, E, ...) for a bearing.</summary>
        public static string CompassLabel(float bearingDegrees)
        {
            int index = (int)Math.Floor(NormalizeDegrees(bearingDegrees) / 45f + 0.5f) % PointLabels.Length;
            return PointLabels[index];
        }

        /// <summary>The compass point label for a tick exactly on a multiple of 45 degrees, otherwise null.</summary>
        public static string CardinalLabelAt(int degrees)
        {
            int wrapped = ((degrees % 360) + 360) % 360;
            return wrapped % 45 == 0 ? PointLabels[wrapped / 45] : null;
        }

        /// <summary>
        /// Horizontal offset on a compass strip, in pixels from its centre, of a bearing seen from a heading.
        /// Positive is to the right.
        /// </summary>
        public static float StripOffset(float headingDegrees, float bearingDegrees, float pixelsPerDegree) =>
            DeltaAngle(headingDegrees, bearingDegrees) * pixelsPerDegree;

        /// <summary>A bearing as three digits, the way it is shown under the compass ("007", "270").</summary>
        public static string FormatBearing(float bearingDegrees)
        {
            int rounded = (int)Math.Floor(NormalizeDegrees(bearingDegrees) + 0.5f) % 360;
            return rounded.ToString("000");
        }

        /// <summary>A ground distance for markers: "42 m" below a kilometre, "1.2 km" from there on.</summary>
        public static string FormatDistance(float meters)
        {
            if (float.IsNaN(meters) || meters < 0f) meters = 0f;
            if (meters < 999.5f) return $"{(int)Math.Floor(meters + 0.5f)} m";
            return $"{meters / 1000f:0.0} km";
        }

        /// <summary>Raid time left as mm:ss (negative counts as zero).</summary>
        public static string FormatClock(int seconds)
        {
            if (seconds < 0) seconds = 0;
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
}
