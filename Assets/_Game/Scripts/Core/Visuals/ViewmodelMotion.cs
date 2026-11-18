using System;

namespace ExtractionShooter.Core.Visuals
{
    /// <summary>
    /// Motion helpers for the first-person weapon: smoothing toward a target without overshoot, a springy return for
    /// recoil kicks, the walking bob and sway against the look direction. Frame-rate independent. Pure logic, unit-tested.
    /// </summary>
    public static class ViewmodelMotion
    {
        private const float MaxSubstep = 1f / 120f;

        /// <summary>
        /// Critically damped follow (no overshoot): reaches about 98% of the way to <paramref name="target"/> in
        /// <paramref name="smoothTime"/> x 2 seconds. Same model as Unity's SmoothDamp, without a speed limit.
        /// </summary>
        public static float SmoothDamp(float value, float target, ref float velocity, float smoothTime, float deltaTime)
        {
            if (deltaTime <= 0f) return value;
            if (smoothTime < 0.0001f)
            {
                velocity = 0f;
                return target;
            }

            float omega = 2f / smoothTime;
            float x = omega * deltaTime;
            float decay = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = value - target;
            float temp = (velocity + omega * change) * deltaTime;
            velocity = (velocity - omega * temp) * decay;
            float result = target + (change + temp) * decay;

            // Never pass the target (rounding at large steps).
            if ((target - value > 0f) == (result > target))
            {
                velocity = 0f;
                return target;
            }
            return result;
        }

        /// <summary>
        /// Damped spring toward <paramref name="target"/>: a kick (added velocity) springs out and settles back, slightly
        /// bouncy when <paramref name="damping"/> &lt; 1. Sub-stepped so long frames stay stable.
        /// </summary>
        /// <param name="frequency">Oscillations per second.</param>
        /// <param name="damping">Damping ratio: 1 settles without bouncing, lower bounces.</param>
        public static float Spring(float value, float target, ref float velocity, float frequency, float damping, float deltaTime)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime)) return value;
            float omega = (float)(2.0 * Math.PI) * (frequency < 0.01f ? 0.01f : frequency);
            float stiffness = omega * omega;
            float friction = 2f * (damping < 0f ? 0f : damping) * omega;

            float remaining = deltaTime > 0.25f ? 0.25f : deltaTime;
            while (remaining > 0f)
            {
                float step = remaining > MaxSubstep ? MaxSubstep : remaining;
                remaining -= step;
                velocity += (-stiffness * (value - target) - friction * velocity) * step;
                value += velocity * step;
            }
            return value;
        }

        /// <summary>Walking bob at a phase: a figure eight, sideways once per cycle and up/down twice (once per step).</summary>
        public static void Bob(float phase, float amount, out float sideways, out float vertical)
        {
            sideways = amount * (float)Math.Sin(phase);
            vertical = -amount * 0.6f * (float)Math.Abs(Math.Sin(phase));
        }

        /// <summary>
        /// Sway against a look movement: turning right swings the weapon left (it lags behind the view), clamped to
        /// <paramref name="max"/> degrees.
        /// </summary>
        public static float Sway(float lookDeltaDegrees, float degreesPerDegree, float max)
        {
            float sway = -lookDeltaDegrees * degreesPerDegree;
            if (float.IsNaN(sway)) return 0f;
            return sway < -max ? -max : sway > max ? max : sway;
        }

        /// <summary>Smallest signed difference between two angles in degrees (-180..180).</summary>
        public static float DeltaAngle(float from, float to)
        {
            float delta = (to - from) % 360f;
            if (delta > 180f) delta -= 360f;
            else if (delta < -180f) delta += 360f;
            return delta;
        }
    }
}
