using System;

namespace ExtractionShooter.Core.Visuals
{
    /// <summary>
    /// Two-bone limb solver (arm: upper arm + forearm, leg: thigh + shin) in the plane of the limb, by the law of
    /// cosines. The caller picks the plane (a pole direction: elbows out and down, knees forward) and turns the result
    /// into positions: the joint sits at <c>root + dir * (RootCos * upper) + bend * (RootSin * upper)</c>, the end at
    /// <c>root + dir * Reach</c>. Pure logic, unit-tested.
    /// </summary>
    public static class LimbIk
    {
        /// <summary>The solved limb in its own plane.</summary>
        public readonly struct Solution
        {
            public Solution(float rootCos, float rootSin, float reach)
            {
                RootCos = rootCos;
                RootSin = rootSin;
                Reach = reach;
            }

            /// <summary>Cosine of the angle between the root-to-target direction and the upper bone.</summary>
            public float RootCos { get; }

            /// <summary>Sine of that angle (0 or more: the joint always bends toward the pole).</summary>
            public float RootSin { get; }

            /// <summary>Distance from the root to the end of the limb: the target distance, clamped to what the limb can reach.</summary>
            public float Reach { get; }
        }

        /// <summary>
        /// Solves a limb whose root is <paramref name="distance"/> from its target. Targets out of reach leave the limb
        /// (almost) straight, pointing at the target; targets too close fold it as far as the bone lengths allow.
        /// </summary>
        /// <param name="upper">Upper bone length (upper arm, thigh).</param>
        /// <param name="lower">Lower bone length (forearm, shin).</param>
        /// <param name="distance">Root to target distance.</param>
        /// <param name="slack">Fraction kept from full extension, so the joint never snaps straight.</param>
        public static Solution Solve(float upper, float lower, float distance, float slack = 0.002f)
        {
            upper = upper < 0.0001f ? 0.0001f : upper;
            lower = lower < 0.0001f ? 0.0001f : lower;
            float max = (upper + lower) * (1f - Clamp(slack, 0f, 0.5f));
            float min = Math.Abs(upper - lower) + 0.0001f;
            if (min > max) min = max;
            float reach = float.IsNaN(distance) ? max : Clamp(distance, min, max);

            float cos = (upper * upper + reach * reach - lower * lower) / (2f * upper * reach);
            cos = Clamp(cos, -1f, 1f);
            float sin = (float)Math.Sqrt(Math.Max(0f, 1f - cos * cos));
            return new Solution(cos, sin, reach);
        }

        /// <summary>Interior angle at the joint in degrees (180 = straight) for a solved limb.</summary>
        public static float JointAngleDegrees(float upper, float lower, in Solution solution)
        {
            float cos = (upper * upper + lower * lower - solution.Reach * solution.Reach) / (2f * upper * lower);
            return (float)(Math.Acos(Clamp(cos, -1f, 1f)) * 180.0 / Math.PI);
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
