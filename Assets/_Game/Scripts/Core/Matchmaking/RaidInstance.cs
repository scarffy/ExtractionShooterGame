using System.Collections.Generic;

namespace ExtractionShooter.Core.Matchmaking
{
    /// <summary>
    /// One raid session on the shared map: who was sent in (by squad), when it started and when it ends.
    /// Instances run one after another, never side by side, so players in different instances never meet.
    /// </summary>
    public sealed class RaidInstance
    {
        private readonly List<int> _squads = new List<int>();

        public RaidInstance(int id, double startTime, double durationSeconds, int maxPlayers, double lateJoinSeconds)
        {
            Id = id;
            StartTime = startTime;
            DurationSeconds = durationSeconds;
            MaxPlayers = maxPlayers;
            LateJoinSeconds = lateJoinSeconds;
        }

        public int Id { get; }
        public double StartTime { get; }
        public double DurationSeconds { get; }
        public int MaxPlayers { get; }
        public double LateJoinSeconds { get; }

        /// <summary>When the raid timer runs out (same clock as <see cref="StartTime"/>).</summary>
        public double EndTime => StartTime + DurationSeconds;

        /// <summary>Players sent in so far (late joiners included; leaving does not free a place).</summary>
        public int PlayerCount { get; private set; }

        public int FreePlaces => MaxPlayers - PlayerCount;

        /// <summary>Squad ids sent into this raid, in the order they joined.</summary>
        public IReadOnlyList<int> Squads => _squads;

        /// <summary>Still in its late-join window, before the end, and not full.</summary>
        public bool IsJoinable(double now) => now < StartTime + LateJoinSeconds && now < EndTime && PlayerCount < MaxPlayers;

        internal void Admit(int squadId, int size)
        {
            _squads.Add(squadId);
            PlayerCount += size;
        }
    }
}
