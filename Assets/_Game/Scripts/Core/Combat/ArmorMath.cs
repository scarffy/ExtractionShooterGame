using System;

namespace ExtractionShooter.Core.Combat
{
    /// <summary>
    /// Penetration versus armor class, kept pure so it can be unit-tested.
    /// <list type="bullet">
    /// <item>Armor protection = class x 10, reduced to as low as half as the armor wears out.</item>
    /// <item>Penetration equal to protection lets half the damage through; 10 above lets all of it through,
    /// 10 below stops the bullet. Linear in between.</item>
    /// <item>A stopped bullet still does blunt damage (a fraction set per armor).</item>
    /// <item>Every hit wears the armor by damage x armor damage % x wear multiplier.</item>
    /// <item>Broken armor (0 durability) does nothing.</item>
    /// </list>
    /// </summary>
    public static class ArmorMath
    {
        public const float ProtectionPerClass = 10f;
        public const float PenetrationWindow = 20f;

        public readonly struct Result
        {
            public Result(int damageTaken, int absorbed, int durabilityAfter, float penetrationChance)
            {
                DamageTaken = damageTaken;
                Absorbed = absorbed;
                DurabilityAfter = durabilityAfter;
                PenetrationFactor = penetrationChance;
            }

            public int DamageTaken { get; }
            public int Absorbed { get; }
            public int DurabilityAfter { get; }

            /// <summary>0 = fully stopped, 1 = fully penetrated.</summary>
            public float PenetrationFactor { get; }
        }

        /// <summary>Effective protection of armor at its current durability.</summary>
        public static float Protection(int armorClass, int durability, int maxDurability)
        {
            if (armorClass <= 0 || durability <= 0 || maxDurability <= 0) return 0f;
            float condition = Math.Min(1f, durability / (float)maxDurability);
            return armorClass * ProtectionPerClass * (0.5f + 0.5f * condition);
        }

        /// <summary>How much of the bullet gets through: 0..1.</summary>
        public static float PenetrationFactor(float penetration, float protection)
        {
            if (protection <= 0f) return 1f;
            return Clamp01(0.5f + (penetration - protection) / PenetrationWindow);
        }

        public static Result Apply(int damage, float penetration, float armorDamagePercent,
            int durability, int maxDurability, int armorClass, float bluntThroughput, float wearMultiplier)
        {
            durability = Math.Max(0, durability);
            if (damage <= 0) return new Result(0, 0, durability, 1f);

            float protection = Protection(armorClass, durability, maxDurability);
            if (protection <= 0f) return new Result(damage, 0, durability, 1f);

            float factor = PenetrationFactor(penetration, protection);
            float blunt = Clamp01(bluntThroughput);
            float through = factor + (1f - factor) * blunt;

            int taken = Math.Min(damage, Math.Max(0, (int)Math.Round(damage * through, MidpointRounding.AwayFromZero)));
            int wear = (int)Math.Ceiling(damage * Math.Max(0f, armorDamagePercent) / 100f * Math.Max(0f, wearMultiplier));
            int durabilityAfter = Math.Max(0, durability - wear);

            return new Result(taken, damage - taken, durabilityAfter, factor);
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
