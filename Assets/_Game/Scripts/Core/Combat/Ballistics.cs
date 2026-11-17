using System;
using System.Numerics;

namespace ExtractionShooter.Core.Combat
{
    /// <summary>
    /// Bullet flight under gravity, without drag: <c>p(t) = origin + direction * v * t - up * g * t^2 / 2</c>.
    /// Shots are resolved instantly by tracing the curve as a chain of straight segments (<see cref="SegmentCount"/>),
    /// so long shots drop while short ones behave like plain hitscan. Y is up. Pure logic, unit-tested.
    /// </summary>
    public static class Ballistics
    {
        /// <summary>Gravity for bullets (m/s^2).</summary>
        public const float StandardGravity = 9.81f;

        /// <summary>Default largest gap (m) allowed between the true curve and a straight segment of the trace.</summary>
        public const float DefaultMaxSag = 0.02f;

        /// <summary>Upper bound on segments per trace (cost cap).</summary>
        public const int MaxSegments = 48;

        /// <summary>Slowest muzzle velocity (m/s) used, so a bad value can't stall a trace.</summary>
        public const float MinVelocity = 1f;

        /// <summary>Position after <paramref name="time"/> seconds of flight.</summary>
        /// <param name="direction">Unit firing direction.</param>
        public static Vector3 PointAt(Vector3 origin, Vector3 direction, float velocity, float gravity, float time) =>
            origin + direction * TravelAt(velocity, time) - Vector3.UnitY * DropAt(gravity, time);

        /// <summary>Velocity vector after <paramref name="time"/> seconds (the bullet's heading at that moment).</summary>
        public static Vector3 VelocityAt(Vector3 direction, float velocity, float gravity, float time) =>
            direction * Velocity(velocity) - Vector3.UnitY * (gravity * time);

        /// <summary>Distance covered along the firing direction after <paramref name="time"/> seconds.</summary>
        public static float TravelAt(float velocity, float time) => Velocity(velocity) * time;

        /// <summary>How far the bullet has fallen below the firing line after <paramref name="time"/> seconds.</summary>
        public static float DropAt(float gravity, float time) => 0.5f * gravity * time * time;

        /// <summary>Seconds to cover <paramref name="distance"/> along the firing line.</summary>
        public static float FlightTime(float distance, float velocity) => distance <= 0f ? 0f : distance / Velocity(velocity);

        /// <summary>Drop below the firing line once the bullet is <paramref name="distance"/> metres downrange.</summary>
        public static float DropAtDistance(float distance, float velocity, float gravity) =>
            DropAt(gravity, FlightTime(distance, velocity));

        /// <summary>
        /// Straight segments needed so no segment strays more than <paramref name="maxSag"/> from the curve.
        /// A chord across <c>dt</c> seconds of a parabola sags <c>g * dt^2 / 8</c> at its middle. At least 1,
        /// at most <see cref="MaxSegments"/>.
        /// </summary>
        public static int SegmentCount(float flightTime, float gravity, float maxSag = DefaultMaxSag)
        {
            if (flightTime <= 0f || gravity <= 0f || maxSag <= 0f) return 1;
            double segmentTime = Math.Sqrt(8.0 * maxSag / gravity);
            double count = Math.Ceiling(flightTime / segmentTime);
            return count < 1.0 ? 1 : count > MaxSegments ? MaxSegments : (int)count;
        }

        /// <summary>Largest gap (m) between the curve and a straight segment spanning <paramref name="segmentTime"/> seconds.</summary>
        public static float SagOf(float gravity, float segmentTime) => gravity * segmentTime * segmentTime / 8f;

        /// <summary>Time at the end of segment <paramref name="index"/> (1-based) of <paramref name="count"/> over the flight.</summary>
        public static float SegmentEndTime(float flightTime, int index, int count) =>
            count <= 0 ? flightTime : flightTime * index / count;

        private static float Velocity(float velocity) => velocity < MinVelocity ? MinVelocity : velocity;
    }
}
