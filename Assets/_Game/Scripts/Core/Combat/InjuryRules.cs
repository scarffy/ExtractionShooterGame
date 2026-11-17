namespace ExtractionShooter.Core.Combat
{
    /// <summary>Which injuries one hit causes.</summary>
    public readonly struct InjuryOutcome
    {
        public InjuryOutcome(bool bleeding, bool fracture)
        {
            Bleeding = bleeding;
            Fracture = fracture;
        }

        public bool Bleeding { get; }
        public bool Fracture { get; }
    }

    /// <summary>
    /// When hits cause bleeding or a fractured leg. Pure logic with the dice passed in, so it is unit-tested.
    /// Body and leg hits that get through armor can bleed; leg hits can fracture; head hits and internal damage
    /// (bleeding itself) never cause new injuries.
    /// </summary>
    public static class InjuryRules
    {
        public static InjuryOutcome FromHit(HitZone zone, int damageTaken, float bleedRoll, float fractureRoll,
            int bleedMinDamage, float bleedChance, int fractureMinDamage, float fractureChance)
        {
            if (zone == HitZone.Head || zone == HitZone.Internal || damageTaken <= 0) return new InjuryOutcome(false, false);

            bool bleeding = damageTaken >= bleedMinDamage && bleedRoll < bleedChance;
            bool fracture = zone == HitZone.Legs && damageTaken >= fractureMinDamage && fractureRoll < fractureChance;
            return new InjuryOutcome(bleeding, fracture);
        }

        /// <summary>How much of a treatment's healing is due after <paramref name="elapsed"/> of <paramref name="duration"/>.</summary>
        public static int HealDue(int totalHeal, float elapsed, float duration)
        {
            if (totalHeal <= 0) return 0;
            if (duration <= 0f || elapsed >= duration) return totalHeal;
            if (elapsed <= 0f) return 0;
            return (int)(totalHeal * (elapsed / duration));
        }
    }
}
