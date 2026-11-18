using System;
using System.Globalization;
using ExtractionShooter.Core.Navigation;

namespace ExtractionShooter.Core.Raid
{
    /// <summary>What an extraction point asks of the player before it lets them out.</summary>
    public enum ExtractionRuleKind : byte
    {
        /// <summary>Open for the whole raid.</summary>
        AlwaysOpen = 0,

        /// <summary>Open for the whole raid, but extracting costs credits from the player's profile.</summary>
        Paid = 1,

        /// <summary>Closed until the raid clock is down to a threshold, then open until the end.</summary>
        LateRaid = 2,
    }

    /// <summary>Whether a player can extract at a point right now.</summary>
    public enum ExtractionAvailability : byte
    {
        /// <summary>Free and open.</summary>
        Open = 0,

        /// <summary>Open, and the player can afford the fee (charged when the extraction completes).</summary>
        PaidAffordable = 1,

        /// <summary>Open, but the player cannot afford the fee.</summary>
        PaidUnaffordable = 2,

        /// <summary>Not open yet.</summary>
        Closed = 3,
    }

    /// <summary>
    /// One extraction point's condition (Delta Force style): always open, paid (a credit fee charged on extraction)
    /// or late-raid (opens when the remaining raid time drops to a threshold). Immutable, pure logic, unit-tested.
    /// </summary>
    public readonly struct ExtractionRule : IEquatable<ExtractionRule>
    {
        public ExtractionRule(ExtractionRuleKind kind, long cost = 0, int opensWithSecondsLeft = 0)
        {
            Kind = kind;
            Cost = kind == ExtractionRuleKind.Paid ? Math.Max(0L, cost) : 0L;
            OpensWithSecondsLeft = kind == ExtractionRuleKind.LateRaid ? Math.Max(0, opensWithSecondsLeft) : 0;
        }

        public static ExtractionRule AlwaysOpen => new ExtractionRule(ExtractionRuleKind.AlwaysOpen);

        public static ExtractionRule Paid(long cost) => new ExtractionRule(ExtractionRuleKind.Paid, cost);

        public static ExtractionRule LateRaid(int opensWithSecondsLeft) =>
            new ExtractionRule(ExtractionRuleKind.LateRaid, 0, opensWithSecondsLeft);

        public ExtractionRuleKind Kind { get; }

        /// <summary>Credits charged when a paid extraction completes; 0 for other kinds.</summary>
        public long Cost { get; }

        /// <summary>Late-raid points open once the raid clock shows this many seconds or fewer; 0 for other kinds.</summary>
        public int OpensWithSecondsLeft { get; }

        /// <summary>The point's state for a player with this much raid time left and these credits.</summary>
        public ExtractionStatus Evaluate(int secondsLeft, long credits)
        {
            switch (Kind)
            {
                case ExtractionRuleKind.Paid:
                    return new ExtractionStatus(
                        Cost <= 0 || credits >= Cost ? ExtractionAvailability.PaidAffordable : ExtractionAvailability.PaidUnaffordable,
                        Cost, 0);

                case ExtractionRuleKind.LateRaid:
                {
                    int remaining = Math.Max(0, secondsLeft);
                    return remaining <= OpensWithSecondsLeft
                        ? new ExtractionStatus(ExtractionAvailability.Open, 0, 0)
                        : new ExtractionStatus(ExtractionAvailability.Closed, 0, remaining - OpensWithSecondsLeft);
                }

                default:
                    return new ExtractionStatus(ExtractionAvailability.Open, 0, 0);
            }
        }

        /// <summary>One-line description for menus and tooltips ("Paid exit: 2,000 cr").</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case ExtractionRuleKind.Paid: return $"Paid exit: {ExtractionStatus.FormatCredits(Cost)}";
                case ExtractionRuleKind.LateRaid: return $"Opens with {NavigationMath.FormatClock(OpensWithSecondsLeft)} left";
                default: return "Always open";
            }
        }

        public bool Equals(ExtractionRule other) =>
            Kind == other.Kind && Cost == other.Cost && OpensWithSecondsLeft == other.OpensWithSecondsLeft;

        public override bool Equals(object obj) => obj is ExtractionRule other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397 ^ Cost.GetHashCode()) * 397 ^ OpensWithSecondsLeft;
    }

    /// <summary>An extraction point's state for one player, with the text the HUD shows for it.</summary>
    public readonly struct ExtractionStatus
    {
        public ExtractionStatus(ExtractionAvailability availability, long cost, int secondsUntilOpen)
        {
            Availability = availability;
            Cost = Math.Max(0L, cost);
            SecondsUntilOpen = Math.Max(0, secondsUntilOpen);
        }

        public ExtractionAvailability Availability { get; }

        /// <summary>Fee charged on extraction (paid points), else 0.</summary>
        public long Cost { get; }

        /// <summary>Seconds of raid time until a closed point opens, else 0.</summary>
        public int SecondsUntilOpen { get; }

        /// <summary>Standing in the point extracts (a paid point charges <see cref="Cost"/> when it completes).</summary>
        public bool CanExtract =>
            Availability == ExtractionAvailability.Open || Availability == ExtractionAvailability.PaidAffordable;

        public bool IsPaid =>
            Availability == ExtractionAvailability.PaidAffordable || Availability == ExtractionAvailability.PaidUnaffordable;

        /// <summary>Short status for the tactical panel: "OPEN", "CLOSED (opens in 03:12)", "PAID 2,000 cr", "NEED 2,000 cr".</summary>
        public string Label
        {
            get
            {
                switch (Availability)
                {
                    case ExtractionAvailability.PaidAffordable: return $"PAID {FormatCredits(Cost)}";
                    case ExtractionAvailability.PaidUnaffordable: return $"NEED {FormatCredits(Cost)}";
                    case ExtractionAvailability.Closed: return $"CLOSED (opens in {NavigationMath.FormatClock(SecondsUntilOpen)})";
                    default: return "OPEN";
                }
            }
        }

        /// <summary>Why the player standing in the point is not extracting, or null when they are.</summary>
        public string BlockedReason
        {
            get
            {
                switch (Availability)
                {
                    case ExtractionAvailability.PaidUnaffordable: return $"Paid exit: you need {FormatCredits(Cost)}";
                    case ExtractionAvailability.Closed: return $"Exit closed: opens in {NavigationMath.FormatClock(SecondsUntilOpen)}";
                    default: return null;
                }
            }
        }

        /// <summary>"2,000 cr" (invariant digit grouping).</summary>
        public static string FormatCredits(long credits) =>
            credits.ToString("N0", CultureInfo.InvariantCulture) + " cr";
    }
}
