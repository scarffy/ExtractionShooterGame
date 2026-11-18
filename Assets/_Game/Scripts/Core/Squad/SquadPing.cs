namespace ExtractionShooter.Core.Squad
{
    /// <summary>
    /// One marker on a <see cref="PingBoard"/>: who placed it, what it marks, where, and when it was placed and goes
    /// away (in the board owner's local clock, seconds).
    /// </summary>
    public readonly struct SquadPing
    {
        public SquadPing(int id, int ownerId, string ownerName, PingKind kind, float x, float y, float z, double placedAt, double expiresAt)
        {
            Id = id;
            OwnerId = ownerId;
            OwnerName = ownerName ?? string.Empty;
            Kind = kind;
            X = x;
            Y = y;
            Z = z;
            PlacedAt = placedAt;
            ExpiresAt = expiresAt;
        }

        /// <summary>Server-assigned id; an upgrade (danger double press) reuses it.</summary>
        public int Id { get; }

        /// <summary>The pinging player (network client id).</summary>
        public int OwnerId { get; }

        public string OwnerName { get; }
        public PingKind Kind { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public double PlacedAt { get; }
        public double ExpiresAt { get; }

        public bool IsExpired(double now) => now >= ExpiresAt;

        /// <summary>Seconds left, never negative.</summary>
        public double SecondsLeft(double now) => ExpiresAt > now ? ExpiresAt - now : 0.0;
    }
}
