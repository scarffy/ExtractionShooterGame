using System;
using ExtractionShooter.Core.Combat;

namespace ExtractionShooter.Core.Visuals
{
    /// <summary>A held item's pose relative to where the hand rests: offset in metres (x right, y up, z forward) and
    /// rotation in degrees.</summary>
    public readonly struct UsePose
    {
        public UsePose(float x, float y, float z, float pitch, float yaw, float roll)
        {
            X = x; Y = y; Z = z; Pitch = pitch; Yaw = yaw; Roll = roll;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Pitch { get; }
        public float Yaw { get; }
        public float Roll { get; }
    }

    /// <summary>
    /// How a medical item moves while it is used, the same in first person and on other players' bodies: it comes up from
    /// below, then a bandage is wound round in circles, a medkit is held up and pressed in, a splint goes down to the leg
    /// and food is lifted to the mouth. Pure logic, unit-tested; bounded (never more than ~0.2 m from rest).
    /// </summary>
    public static class UseItemMotion
    {
        /// <summary>Seconds to bring the item up.</summary>
        public const float RaiseSeconds = 0.25f;

        /// <param name="kind">What is being used (None: no offset).</param>
        /// <param name="seconds">Seconds since the use started.</param>
        /// <param name="progress">Use progress 0..1 (from the item's use time).</param>
        public static UsePose Evaluate(UseItemVisual kind, float seconds, float progress)
        {
            if (kind == UseItemVisual.None) return default;
            seconds = Math.Max(0f, seconds);
            progress = Clamp01(progress);

            float raise = Smooth(Clamp01(seconds / RaiseSeconds));
            float drop = -0.15f * (1f - raise);
            float tilt = 30f * (1f - raise);
            float t = seconds;

            switch (kind)
            {
                case UseItemVisual.Bandage:
                {
                    // Winding: small circles in the hand's plane while the roll spins.
                    double angle = 2.0 * Math.PI * 1.3 * t;
                    float x = 0.03f * (float)Math.Cos(angle) * raise;
                    float y = 0.03f * (float)Math.Sin(angle) * raise;
                    return new UsePose(x, y + drop, 0f, tilt, 0f, (float)(t * 280.0 % 360.0) * raise);
                }
                case UseItemVisual.Medkit:
                {
                    // Held up with a gentle bob; in the second half, pressed in toward the body.
                    float bob = 0.012f * (float)Math.Sin(2.0 * Math.PI * 1.5 * t) * raise;
                    float press = Smooth(Clamp01((progress - 0.55f) / 0.3f));
                    return new UsePose(0f, bob + drop + 0.02f * press, -0.06f * press, tilt + 15f * press, 0f, 0f);
                }
                case UseItemVisual.Splint:
                {
                    // Down to the leg, then a little side-to-side as it is strapped on.
                    float down = Smooth(Clamp01(progress / 0.35f));
                    float strap = 0.02f * (float)Math.Sin(2.0 * Math.PI * 1.6 * t) * down;
                    return new UsePose(strap, drop - 0.14f * down, -0.02f * down, tilt + 40f * down, 0f, 0f);
                }
                case UseItemVisual.Food:
                {
                    // Lifted to the mouth about once a second.
                    float lift = (float)Math.Max(0.0, Math.Sin(2.0 * Math.PI * 0.8 * t)) * raise;
                    return new UsePose(0.02f * lift, drop + 0.07f * lift, -0.04f * lift, tilt - 35f * lift, 0f, 0f);
                }
                default:
                    return new UsePose(0f, drop, 0f, tilt, 0f, 0f);
            }
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        private static float Smooth(float value) => value * value * (3f - 2f * value);
    }
}
