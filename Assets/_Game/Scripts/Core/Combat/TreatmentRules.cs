using System.Collections.Generic;

namespace ExtractionShooter.Core.Combat
{
    /// <summary>What a player is seen holding while using an item (first person and to other players).</summary>
    public enum UseItemVisual : byte
    {
        None = 0,
        Bandage = 1,
        Medkit = 2,
        Splint = 3,
        Food = 4,
    }

    /// <summary>What a medical item can do.</summary>
    public readonly struct TreatmentOption
    {
        public TreatmentOption(int heal, bool stopsBleeding, bool fixesFracture, float useSeconds)
        {
            Heal = heal;
            StopsBleeding = stopsBleeding;
            FixesFracture = fixesFracture;
            UseSeconds = useSeconds;
        }

        public int Heal { get; }
        public bool StopsBleeding { get; }
        public bool FixesFracture { get; }
        public float UseSeconds { get; }
    }

    /// <summary>A player's wounds right now.</summary>
    public readonly struct WoundState
    {
        public WoundState(int missingHealth, bool bleeding, bool fractured)
        {
            MissingHealth = missingHealth < 0 ? 0 : missingHealth;
            Bleeding = bleeding;
            Fractured = fractured;
        }

        public int MissingHealth { get; }
        public bool Bleeding { get; }
        public bool Fractured { get; }
        public bool IsHurt => MissingHealth > 0 || Bleeding || Fractured;
    }

    /// <summary>
    /// Rules for using medical items. A treatment takes the item's use time; its effects (heal, stop bleeding, set a
    /// fracture) and the item's consumption happen together when it completes, so cancelling costs nothing but time and
    /// can't be used to get part of a heal for free.
    /// </summary>
    public static class TreatmentRules
    {
        /// <summary>Whether using the item would do anything.</summary>
        public static bool IsUseful(in TreatmentOption option, in WoundState wounds) =>
            (option.Heal > 0 && wounds.MissingHealth > 0) || (option.StopsBleeding && wounds.Bleeding) ||
            (option.FixesFracture && wounds.Fractured);

        /// <summary>
        /// The quick-heal pick: bleeding first (it keeps draining), then a fracture (no sprinting), then health.
        /// Within a need the fastest item wins; for health, the smallest heal that covers the missing health (so a medkit
        /// isn't spent on a scratch), else the biggest heal. Returns the option's index, or -1 when nothing helps.
        /// </summary>
        public static int PickQuickHeal(IReadOnlyList<TreatmentOption> options, in WoundState wounds)
        {
            if (options == null || options.Count == 0 || !wounds.IsHurt) return -1;

            if (wounds.Bleeding)
            {
                int pick = Fastest(options, o => o.StopsBleeding);
                if (pick >= 0) return pick;
            }
            if (wounds.Fractured)
            {
                int pick = Fastest(options, o => o.FixesFracture);
                if (pick >= 0) return pick;
            }
            if (wounds.MissingHealth <= 0) return -1;

            int covering = -1, biggest = -1;
            for (int i = 0; i < options.Count; i++)
            {
                TreatmentOption o = options[i];
                if (o.Heal <= 0) continue;
                if (o.Heal >= wounds.MissingHealth && (covering < 0 || Better(o, options[covering], smallerHeal: true))) covering = i;
                if (biggest < 0 || Better(o, options[biggest], smallerHeal: false)) biggest = i;
            }
            return covering >= 0 ? covering : biggest;
        }

        private static int Fastest(IReadOnlyList<TreatmentOption> options, System.Func<TreatmentOption, bool> fits)
        {
            int best = -1;
            for (int i = 0; i < options.Count; i++)
            {
                if (!fits(options[i])) continue;
                TreatmentOption o = options[i];
                if (best < 0 || o.UseSeconds < options[best].UseSeconds ||
                    (o.UseSeconds == options[best].UseSeconds && o.Heal < options[best].Heal))
                    best = i;
            }
            return best;
        }

        private static bool Better(in TreatmentOption candidate, in TreatmentOption current, bool smallerHeal)
        {
            if (candidate.Heal != current.Heal) return smallerHeal ? candidate.Heal < current.Heal : candidate.Heal > current.Heal;
            return candidate.UseSeconds < current.UseSeconds;
        }
    }
}
