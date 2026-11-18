namespace ExtractionShooter.Core.Squad
{
    /// <summary>What the server does with a ping request.</summary>
    public enum PingDecision
    {
        /// <summary>Too soon after the last ping: ignored.</summary>
        Reject = 0,

        /// <summary>A new ping.</summary>
        Place = 1,

        /// <summary>The second press of a double press: the last ping becomes a danger ping (same id).</summary>
        Upgrade = 2,
    }

    /// <summary>
    /// Server side, one per player: the ping rate limit. New pings need <see cref="PingRules.MinInterval"/> since the
    /// last one; a danger request soon after a ping (<see cref="PingRules.UpgradeWindow"/>) upgrades that ping instead
    /// and is exempt, once per ping. Pure logic, unit-tested.
    /// </summary>
    public sealed class PingLimiter
    {
        private readonly double _minInterval;
        private readonly double _upgradeWindow;
        private bool _hasLast;
        private double _lastPlacedAt;
        private bool _lastIsDanger;

        public PingLimiter(double minInterval = PingRules.MinInterval, double upgradeWindow = PingRules.UpgradeWindow)
        {
            _minInterval = minInterval < 0.0 ? 0.0 : minInterval;
            _upgradeWindow = upgradeWindow < 0.0 ? 0.0 : upgradeWindow;
        }

        /// <summary>Decides a request at time <paramref name="now"/> (seconds) and records it when accepted.</summary>
        public PingDecision Evaluate(double now, bool danger)
        {
            if (danger && _hasLast && !_lastIsDanger && now - _lastPlacedAt <= _upgradeWindow)
            {
                _lastIsDanger = true;
                return PingDecision.Upgrade;
            }

            if (_hasLast && now - _lastPlacedAt < _minInterval) return PingDecision.Reject;

            _hasLast = true;
            _lastPlacedAt = now;
            _lastIsDanger = danger;
            return PingDecision.Place;
        }

        /// <summary>
        /// The upgrade had nothing to upgrade (the last ping already gone): lets the next danger request place a new
        /// ping right away instead of being rate-limited.
        /// </summary>
        public void Forget() => _hasLast = false;
    }

    /// <summary>
    /// Owner side: turns ping key presses into requests. A press sends a ping unless one went out less than
    /// <see cref="PingRules.MinInterval"/> ago; a second press within <see cref="PingRules.DoublePressWindow"/> of a
    /// sent one asks for danger (once; a third quick press is just rate-limited). Pure logic, unit-tested.
    /// </summary>
    public sealed class PingPressTracker
    {
        private readonly double _doublePressWindow;
        private readonly double _minInterval;
        private bool _hasSent;
        private double _lastSentAt;
        private bool _upgradeAvailable;

        public PingPressTracker(double doublePressWindow = PingRules.DoublePressWindow, double minInterval = PingRules.MinInterval)
        {
            _doublePressWindow = doublePressWindow;
            _minInterval = minInterval;
        }

        /// <summary>A press at <paramref name="now"/>. Returns whether to send a request, and whether it asks for danger.</summary>
        public bool TryPress(double now, out bool danger)
        {
            danger = false;
            if (_hasSent && _upgradeAvailable && now - _lastSentAt <= _doublePressWindow)
            {
                _upgradeAvailable = false;
                danger = true;
                return true;
            }

            if (_hasSent && now - _lastSentAt < _minInterval) return false;

            _hasSent = true;
            _lastSentAt = now;
            _upgradeAvailable = true;
            return true;
        }
    }
}
