using System;

namespace ExtractionShooter.Core.Visuals
{
    /// <summary>The shape of a smoke column: how high and far its puffs travel and how big they grow.</summary>
    public readonly struct SmokeColumnShape
    {
        public SmokeColumnShape(float lifetime, float riseHeight, float drift, float sway, float startRadius, float endRadius)
        {
            Lifetime = Math.Max(0.1f, lifetime);
            RiseHeight = Math.Max(0f, riseHeight);
            Drift = Math.Max(0f, drift);
            Sway = Math.Max(0f, sway);
            StartRadius = Math.Max(0f, startRadius);
            EndRadius = Math.Max(StartRadius, endRadius);
        }

        /// <summary>Seconds one puff lives, from the ground to gone.</summary>
        public float Lifetime { get; }

        /// <summary>Metres a puff rises over its life.</summary>
        public float RiseHeight { get; }

        /// <summary>Metres the wind carries a puff over its life.</summary>
        public float Drift { get; }

        /// <summary>Metres a puff wanders sideways (across the wind).</summary>
        public float Sway { get; }

        public float StartRadius { get; }
        public float EndRadius { get; }
    }

    /// <summary>One puff at one moment: offset from the emitter (up, along the wind, across it), radius and opacity.</summary>
    public readonly struct SmokePuff
    {
        public SmokePuff(float height, float along, float across, float radius, float opacity)
        {
            Height = height;
            Along = along;
            Across = across;
            Radius = radius;
            Opacity = opacity;
        }

        public float Height { get; }
        public float Along { get; }
        public float Across { get; }
        public float Radius { get; }

        /// <summary>0..1, multiplied into the smoke colour's alpha.</summary>
        public float Opacity { get; }
    }

    /// <summary>
    /// A signal-smoke column made of a fixed number of puffs that are emitted one after another, forever: puff i of n is
    /// i/n of a lifetime older than puff 0. Each rises fast then slows, is pushed further by the wind as it climbs,
    /// wanders a little, grows and fades. Deterministic (no stored state), so every client draws the same column.
    /// Pure logic, unit-tested.
    /// </summary>
    public static class SmokeColumn
    {
        /// <summary>Share of a puff's life spent fading in at the emitter.</summary>
        public const float FadeInShare = 0.08f;

        /// <summary>Age of puff <paramref name="index"/> of <paramref name="count"/> at <paramref name="time"/>, as a share of its life (0..1).</summary>
        public static float Age(int index, int count, double time, float lifetime)
        {
            if (count <= 0) return 0f;
            double cycles = time / Math.Max(0.1f, lifetime) + (double)Mod(index, count) / count;
            return (float)(cycles - Math.Floor(cycles));
        }

        public static SmokePuff Evaluate(in SmokeColumnShape shape, int index, int count, double time)
        {
            float age = Age(index, count, time, shape.Lifetime);
            float rest = 1f - age;

            float height = shape.RiseHeight * (1f - rest * rest);
            float along = shape.Drift * age * age;

            // A fixed phase per puff so neighbours do not move in step.
            float phase = Fraction(index * 0.618034f);
            float across = shape.Sway * age * (float)Math.Sin(2.0 * Math.PI * (age * 1.5f + phase)) + (phase - 0.5f) * 0.3f * age;

            float radius = shape.StartRadius + (shape.EndRadius - shape.StartRadius) * (float)Math.Sqrt(age);
            float fadeIn = Math.Min(1f, age / FadeInShare);
            float opacity = fadeIn * (float)Math.Pow(rest, 1.5);

            return new SmokePuff(height, along, across, radius, opacity);
        }

        private static int Mod(int value, int modulus)
        {
            int r = value % modulus;
            return r < 0 ? r + modulus : r;
        }

        private static float Fraction(float value) => value - (float)Math.Floor(value);
    }
}
