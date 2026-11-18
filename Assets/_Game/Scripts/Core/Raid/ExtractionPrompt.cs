using System;
using System.Globalization;

namespace ExtractionShooter.Core.Raid
{
    /// <summary>What the centre extraction prompt is showing.</summary>
    public enum ExtractionPromptKind : byte
    {
        /// <summary>Nothing: the player is not in an extraction zone.</summary>
        None = 0,

        /// <summary>Counting down in an open zone.</summary>
        Extracting = 1,

        /// <summary>Standing in a zone that will not let them out (closed, or a fee they cannot pay).</summary>
        Blocked = 2,

        /// <summary>They left the zone before the countdown finished (shown for a moment).</summary>
        Cancelled = 3,
    }

    /// <summary>The centre extraction prompt: a title, a detail line, a countdown and the ring's fill (0..1).</summary>
    public readonly struct ExtractionPrompt
    {
        public ExtractionPrompt(ExtractionPromptKind kind, string title, string detail, string countdown, float progress)
        {
            Kind = kind;
            Title = title ?? string.Empty;
            Detail = detail ?? string.Empty;
            Countdown = countdown ?? string.Empty;
            Progress = progress < 0f ? 0f : progress > 1f ? 1f : progress;
        }

        public static ExtractionPrompt None => default;

        public ExtractionPromptKind Kind { get; }
        public string Title { get; }
        public string Detail { get; }

        /// <summary>Seconds left while extracting ("4.2"); empty otherwise.</summary>
        public string Countdown { get; }

        /// <summary>How full the countdown ring is (0..1).</summary>
        public float Progress { get; }

        public bool IsVisible => Kind != ExtractionPromptKind.None;
    }

    /// <summary>
    /// Turns the local player's extraction state into the centre HUD prompt, frame by frame, and remembers a
    /// countdown that was broken off (the player stepped out) for <see cref="CancelledSeconds"/>. Pure logic, unit-tested.
    /// </summary>
    public sealed class ExtractionPromptTracker
    {
        /// <summary>How long "Extraction cancelled" stays up.</summary>
        public const double CancelledSeconds = 2.0;

        private bool _wasExtracting;
        private string _lastZone = string.Empty;
        private double _cancelledUntil = double.NegativeInfinity;

        /// <param name="now">Any steadily increasing clock, in seconds.</param>
        /// <param name="alive">False once dead or out of the raid: nothing is shown and nothing counts as cancelled.</param>
        /// <param name="zoneName">The zone the player stands in, or null/empty.</param>
        /// <param name="status">That zone's status for the player (unused without a zone).</param>
        /// <param name="serverZone">The zone the server counts them as extracting at, or null/empty.</param>
        /// <param name="progress">The server's extraction progress (0..1).</param>
        /// <param name="extractSeconds">Seconds a full extraction takes.</param>
        public ExtractionPrompt Update(double now, bool alive, string zoneName, ExtractionStatus status,
            string serverZone, float progress, float extractSeconds)
        {
            if (!alive)
            {
                Reset();
                return ExtractionPrompt.None;
            }

            bool inZone = !string.IsNullOrEmpty(zoneName);
            bool serverExtracting = !string.IsNullOrEmpty(serverZone);

            // The server is the authority; standing in an open zone it has not caught up with yet counts too (latency).
            if (serverExtracting || inZone && status.CanExtract)
            {
                string zone = serverExtracting ? serverZone : zoneName;
                _wasExtracting = true;
                _lastZone = zone;
                _cancelledUntil = double.NegativeInfinity;
                float shown = serverExtracting ? progress : 0f;
                return Extracting(zone, shown, extractSeconds, inZone && status.IsPaid ? status.Cost : 0L);
            }

            if (_wasExtracting)
            {
                _wasExtracting = false;
                _cancelledUntil = now + CancelledSeconds;
            }

            if (inZone)
            {
                _cancelledUntil = double.NegativeInfinity; // the zone's own reason says more than "cancelled"
                return new ExtractionPrompt(ExtractionPromptKind.Blocked, Upper(zoneName), status.BlockedReason, string.Empty, 0f);
            }

            if (now < _cancelledUntil)
                return new ExtractionPrompt(ExtractionPromptKind.Cancelled, "EXTRACTION CANCELLED",
                    $"You left {_lastZone}", string.Empty, 0f);

            return ExtractionPrompt.None;
        }

        /// <summary>Forgets everything (a new raid, or the player died).</summary>
        public void Reset()
        {
            _wasExtracting = false;
            _lastZone = string.Empty;
            _cancelledUntil = double.NegativeInfinity;
        }

        private static ExtractionPrompt Extracting(string zone, float progress, float extractSeconds, long fee)
        {
            float clamped = progress < 0f ? 0f : progress > 1f ? 1f : progress;
            double left = Math.Max(0.0, Math.Max(0f, extractSeconds) * (1.0 - clamped));
            string detail = fee > 0 ? $"{ExtractionStatus.FormatCredits(fee)} will be charged" : "Stay in the zone";
            return new ExtractionPrompt(ExtractionPromptKind.Extracting, "EXTRACTING  ·  " + Upper(zone), detail,
                FormatSeconds(left), clamped);
        }

        /// <summary>"4.2" (one decimal, invariant), rounded up so it reaches "0.0" only at the end.</summary>
        public static string FormatSeconds(double seconds)
        {
            double tenths = Math.Max(0.0, Math.Ceiling(Math.Max(0.0, seconds) * 10.0 - 1e-6)) / 10.0; // never "-0.0"
            return tenths.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Upper(string text) => (text ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>The coloured smoke an extraction point gives off.</summary>
    public enum ExtractionSmokeTone : byte
    {
        /// <summary>No smoke: the point is not open yet.</summary>
        Off = 0,

        /// <summary>Green: open.</summary>
        Open = 1,

        /// <summary>Amber: open for a fee.</summary>
        Paid = 2,
    }

    public static class ExtractionSignals
    {
        /// <summary>
        /// The smoke a point shows: green when open, amber for a paid exit, none while a late-raid point is still shut.
        /// <paramref name="secondsLeft"/> is the raid clock, or null when it is unknown (then a late-raid point shows none).
        /// </summary>
        public static ExtractionSmokeTone SmokeTone(ExtractionRule rule, int? secondsLeft)
        {
            switch (rule.Kind)
            {
                case ExtractionRuleKind.Paid:
                    return ExtractionSmokeTone.Paid;
                case ExtractionRuleKind.LateRaid:
                    return secondsLeft.HasValue && rule.Evaluate(secondsLeft.Value, 0).CanExtract
                        ? ExtractionSmokeTone.Open
                        : ExtractionSmokeTone.Off;
                default:
                    return ExtractionSmokeTone.Open;
            }
        }
    }
}
