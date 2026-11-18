using System;

namespace ExtractionShooter.Core.Visuals
{
    /// <summary>
    /// Procedural walk cycle. One cycle (2π) is two steps: each foot swings forward (lifted) for half the cycle and is
    /// planted for the other half while the body passes over it. The phase advances with the distance actually moved,
    /// so feet keep pace with any speed. The right foot runs half a cycle behind the left. Pure logic, unit-tested.
    /// </summary>
    public static class GaitCycle
    {
        public const float TwoPi = (float)(Math.PI * 2.0);

        /// <summary>A foot's offset from its rest point under the hip.</summary>
        public readonly struct FootOffset
        {
            public FootOffset(float forward, float lift)
            {
                Forward = forward;
                Lift = lift;
            }

            /// <summary>Along the direction of travel (negative: behind the hip).</summary>
            public float Forward { get; }

            /// <summary>Height above the ground (only while swinging).</summary>
            public float Lift { get; }
        }

        /// <summary>Step length for a speed: longer strides when running, never shorter than <paramref name="min"/>.</summary>
        public static float StrideLength(float speed, float metersPerSpeed, float min, float max)
        {
            float stride = Math.Abs(speed) * metersPerSpeed;
            return stride < min ? min : stride > max ? max : stride;
        }

        /// <summary>Advances the phase (radians, wrapped to 0..2π) by a distance moved. Half a cycle covers one stride.</summary>
        public static float Advance(float phase, float distance, float stride)
        {
            if (stride <= 0.0001f || float.IsNaN(distance) || float.IsInfinity(distance)) return Wrap(phase);
            return Wrap(phase + (float)Math.PI * Math.Abs(distance) / stride);
        }

        /// <summary>The foot at this phase: swinging forward and lifted while sin(phase) &gt; 0, planted otherwise.</summary>
        public static FootOffset Foot(float phase, float stride, float lift)
        {
            float forward = -0.5f * stride * (float)Math.Cos(phase);
            float sin = (float)Math.Sin(phase);
            return new FootOffset(forward, sin > 0f ? lift * sin : 0f);
        }

        /// <summary>
        /// How far the hips must drop so a straight leg of <paramref name="legLength"/> still reaches a foot half a
        /// stride ahead or behind.
        /// </summary>
        public static float HipDrop(float legLength, float stride)
        {
            float half = 0.5f * Math.Abs(stride);
            if (legLength <= 0f) return 0f;
            if (half >= legLength) return legLength;
            return legLength - (float)Math.Sqrt(legLength * legLength - half * half);
        }

        /// <summary>The other foot's phase (half a cycle later).</summary>
        public static float Opposite(float phase) => Wrap(phase + (float)Math.PI);

        /// <summary>Hip height change: lowest as each foot lands, highest as the body passes over the planted foot.</summary>
        public static float HipBob(float phase, float amount) => -amount * (float)Math.Cos(2f * phase);

        /// <summary>Arm swing (-1..1) opposite to the same side's leg: forward while that leg is behind.</summary>
        public static float ArmSwing(float phase) => (float)Math.Cos(phase);

        public static float Wrap(float phase)
        {
            if (float.IsNaN(phase) || float.IsInfinity(phase)) return 0f;
            phase %= TwoPi;
            return phase < 0f ? phase + TwoPi : phase;
        }
    }
}
