using System;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>How heavy a load is, Delta Force-style: light, heavy, very heavy, overloaded.</summary>
    public enum LoadLevel : byte
    {
        Light = 0,
        Heavy = 1,
        VeryHeavy = 2,
        Overloaded = 3,
    }

    /// <summary>
    /// Carried weight rules (pure, unit-tested). Up to <see cref="LightLimit"/> there is no penalty; from there to
    /// <see cref="HeavyLimit"/> speed falls gradually; up to <see cref="MaxLoad"/> speed keeps falling; past it the player is overloaded: slow and unable to sprint.
    /// </summary>
    public readonly struct Encumbrance
    {
        public Encumbrance(float lightLimit, float heavyLimit, float maxLoad, float heavySpeed, float maxLoadSpeed, float overloadedSpeed)
        {
            if (!(lightLimit > 0f && heavyLimit > lightLimit && maxLoad > heavyLimit))
                throw new ArgumentException("Limits must increase: light < heavy < max.");
            LightLimit = lightLimit;
            HeavyLimit = heavyLimit;
            MaxLoad = maxLoad;
            HeavySpeed = Clamp01(heavySpeed);
            MaxLoadSpeed = Clamp01(maxLoadSpeed);
            OverloadedSpeed = Clamp01(overloadedSpeed);
        }

        /// <summary>Default numbers: 35 / 55 / 70 kg.</summary>
        public static Encumbrance Default => new Encumbrance(35f, 55f, 70f, 0.85f, 0.7f, 0.45f);

        public float LightLimit { get; }
        public float HeavyLimit { get; }
        public float MaxLoad { get; }
        /// <summary>Speed multiplier at the heavy limit.</summary>
        public float HeavySpeed { get; }
        /// <summary>Speed multiplier at the max load.</summary>
        public float MaxLoadSpeed { get; }
        /// <summary>Speed multiplier when overloaded.</summary>
        public float OverloadedSpeed { get; }

        public LoadLevel LevelOf(float kilograms) =>
            kilograms > MaxLoad ? LoadLevel.Overloaded
            : kilograms > HeavyLimit ? LoadLevel.VeryHeavy
            : kilograms > LightLimit ? LoadLevel.Heavy
            : LoadLevel.Light;

        /// <summary>Movement speed multiplier for a load: 1 when light, easing down to the overloaded speed.</summary>
        public float SpeedMultiplier(float kilograms)
        {
            if (kilograms <= LightLimit) return 1f;
            if (kilograms <= HeavyLimit) return Lerp(1f, HeavySpeed, (kilograms - LightLimit) / (HeavyLimit - LightLimit));
            if (kilograms <= MaxLoad) return Lerp(HeavySpeed, MaxLoadSpeed, (kilograms - HeavyLimit) / (MaxLoad - HeavyLimit));
            return OverloadedSpeed;
        }

        public bool AllowsSprint(float kilograms) => kilograms <= MaxLoad;

        private static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
