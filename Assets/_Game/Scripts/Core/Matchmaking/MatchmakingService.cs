using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Raid;

namespace ExtractionShooter.Core.Matchmaking
{
    /// <summary>Where a queued squad stands.</summary>
    public enum MatchmakingPhase : byte
    {
        /// <summary>Not queued.</summary>
        Idle = 0,

        /// <summary>Queued for the next raid; not enough players yet to start the countdown.</summary>
        Searching = 1,

        /// <summary>Enough players: the launch countdown is running.</summary>
        Starting = 2,

        /// <summary>A raid is running that this squad cannot join (late-join window over, or no room): waiting for the next one.</summary>
        WaitingForRaid = 3,
    }

    /// <summary>A queued squad's view of matchmaking, for the lobby UI.</summary>
    public readonly struct QueueStatus
    {
        public QueueStatus(MatchmakingPhase phase, int playersFound, int capacity, int secondsLeft, int position)
        {
            Phase = phase;
            PlayersFound = playersFound;
            Capacity = capacity;
            SecondsLeft = secondsLeft;
            Position = position;
        }

        public static QueueStatus Idle => default;

        public MatchmakingPhase Phase { get; }

        /// <summary>Searching/Starting: players in the raid being formed. Waiting: players in the running raid.</summary>
        public int PlayersFound { get; }

        public int Capacity { get; }

        /// <summary>Starting: seconds to launch. Waiting: estimated seconds until the next raid can start (an upper bound).</summary>
        public int SecondsLeft { get; }

        /// <summary>1-based place in the queue; 0 when not queued.</summary>
        public int Position { get; }
    }

    /// <summary>A squad sent into a raid by <see cref="MatchmakingService.Tick"/>.</summary>
    public readonly struct RaidAssignment
    {
        public RaidAssignment(RaidInstance raid, int squadId, int size, bool opensRaid)
        {
            Raid = raid;
            SquadId = squadId;
            Size = size;
            OpensRaid = opensRaid;
        }

        public RaidInstance Raid { get; }
        public int SquadId { get; }
        public int Size { get; }

        /// <summary>True for the first squad of a newly launched raid (reset the map before deploying it).</summary>
        public bool OpensRaid { get; }
    }

    /// <summary>
    /// Server-side matchmaking for one map: a FIFO queue of squads feeding raid instances that run one at a time.
    /// Pure logic, unit-tested; the caller supplies the clock and reports how many players are still in the raid.
    /// <list type="bullet">
    /// <item>Squads are never split. The raid is filled first-fit in queue order: the head of the queue always gets in,
    /// a later, smaller squad may take places a bigger one ahead of it cannot use (the bigger one keeps its place).</item>
    /// <item>With no raid running: once the squads that fit reach <see cref="MatchmakingSettings.MinPlayers"/> a countdown
    /// starts; the raid launches when it ends, or at once when the raid is full.</item>
    /// <item>While a raid runs, queued squads join it during its late-join window if they fit; otherwise they wait for the
    /// raid to close (time up, or everyone out) and form the next one.</item>
    /// <item>Each squad queues for one difficulty and only shares a raid with squads of the same difficulty. The next raid
    /// takes the difficulty of the squad at the head of the queue; squads queued for another difficulty keep their places
    /// and wait for a raid of theirs.</item>
    /// </list>
    /// </summary>
    public sealed class MatchmakingService
    {
        /// <summary>A raid closes when nobody is left in it, but not in its first seconds (players are still spawning).</summary>
        public const double EmptyCloseGraceSeconds = 5;

        /// <summary>After the timer runs out, the raid closes at the latest this much later (last characters being removed).</summary>
        public const double OvertimeCloseSeconds = 10;

        private readonly List<Entry> _queue = new List<Entry>();
        private readonly List<RaidAssignment> _assignments = new List<RaidAssignment>();
        private readonly List<int> _pack = new List<int>();
        private int _nextRaidId = 1;
        private int _playersInRaid;
        private double? _countdownEndsAt;

        public MatchmakingService(MatchmakingSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public MatchmakingSettings Settings { get; }

        /// <summary>The raid currently running, or null.</summary>
        public RaidInstance ActiveRaid { get; private set; }

        public int QueuedSquads => _queue.Count;

        /// <summary>When the launch countdown ends, or null when none is running.</summary>
        public double? CountdownEndsAt => _countdownEndsAt;

        /// <summary>Queues a squad for a difficulty. Refused when already queued or when its size is not 1..MaxPlayers.</summary>
        public bool Enqueue(int squadId, int size, RaidDifficulty difficulty = RaidDifficulty.Normal)
        {
            if (size < 1 || size > Settings.MaxPlayers || IndexOf(squadId) >= 0) return false;
            _queue.Add(new Entry(squadId, size, difficulty));
            return true;
        }

        /// <summary>The difficulty a squad is queued for, or null when it is not queued.</summary>
        public RaidDifficulty? QueuedDifficulty(int squadId)
        {
            int index = IndexOf(squadId);
            return index < 0 ? (RaidDifficulty?)null : _queue[index].Difficulty;
        }

        /// <summary>Takes a squad out of the queue. False when it was not queued.</summary>
        public bool Cancel(int squadId)
        {
            int index = IndexOf(squadId);
            if (index < 0) return false;
            _queue.RemoveAt(index);
            if (_queue.Count == 0) _countdownEndsAt = null;
            return true;
        }

        public bool IsQueued(int squadId) => IndexOf(squadId) >= 0;

        /// <summary>How many players are still inside the active raid (alive or not yet returned). Report before each tick.</summary>
        public void ReportPlayersInRaid(int count) => _playersInRaid = Math.Max(0, count);

        /// <summary>
        /// Advances matchmaking: closes a finished raid, admits late joiners, runs the countdown and launches raids.
        /// Returns the squads to deploy now (valid until the next call), first squad of a new raid first.
        /// </summary>
        public IReadOnlyList<RaidAssignment> Tick(double now)
        {
            _assignments.Clear();

            RaidInstance raid = ActiveRaid;
            if (raid != null && IsOver(raid, now)) ActiveRaid = raid = null;

            if (raid != null)
            {
                _countdownEndsAt = null;
                if (!raid.IsJoinable(now)) return _assignments;

                for (int i = 0; i < _queue.Count && raid.FreePlaces > 0; i++)
                {
                    if (_queue[i].Size > raid.FreePlaces || _queue[i].Difficulty != raid.Difficulty) continue;
                    Entry entry = _queue[i];
                    _queue.RemoveAt(i--);
                    raid.Admit(entry.SquadId, entry.Size);
                    _assignments.Add(new RaidAssignment(raid, entry.SquadId, entry.Size, false));
                }
                return _assignments;
            }

            int packed = BuildPack();
            if (packed < Settings.MinPlayers)
            {
                _countdownEndsAt = null;
                return _assignments;
            }

            if (_countdownEndsAt == null) _countdownEndsAt = now + Settings.CountdownSeconds;
            if (packed < Settings.MaxPlayers && now < _countdownEndsAt.Value) return _assignments;

            raid = new RaidInstance(_nextRaidId++, now, Settings.RaidDurationSeconds, Settings.MaxPlayers, Settings.LateJoinSeconds,
                _queue[_pack[0]].Difficulty);
            ActiveRaid = raid;
            _countdownEndsAt = null;
            _playersInRaid = 0;

            for (int p = 0; p < _pack.Count; p++)
            {
                Entry entry = _queue[_pack[p]];
                raid.Admit(entry.SquadId, entry.Size);
                _assignments.Add(new RaidAssignment(raid, entry.SquadId, entry.Size, p == 0));
            }
            // Pack indices are ascending: remove from the back so the earlier ones stay valid.
            for (int p = _pack.Count - 1; p >= 0; p--) _queue.RemoveAt(_pack[p]);
            return _assignments;
        }

        /// <summary>A squad's place in matchmaking right now, for display.</summary>
        public QueueStatus GetStatus(int squadId, double now)
        {
            int index = IndexOf(squadId);
            if (index < 0) return QueueStatus.Idle;

            int capacity = Settings.MaxPlayers;
            RaidInstance raid = ActiveRaid;
            if (raid != null)
                return new QueueStatus(MatchmakingPhase.WaitingForRaid, raid.PlayerCount, capacity,
                    Seconds(raid.EndTime - now), index + 1);
            // (A squad of the running raid's difficulty that fits is admitted by Tick before anyone asks.)

            int packed = BuildPack();
            if (!_pack.Contains(index))
            {
                // Did not make the raid being formed: wait for it to run its course.
                double launch = _countdownEndsAt ?? now + Settings.CountdownSeconds;
                return new QueueStatus(MatchmakingPhase.WaitingForRaid, packed, capacity,
                    Seconds(launch + Settings.RaidDurationSeconds - now), index + 1);
            }

            return _countdownEndsAt.HasValue && packed >= Settings.MinPlayers
                ? new QueueStatus(MatchmakingPhase.Starting, packed, capacity, Seconds(_countdownEndsAt.Value - now), index + 1)
                : new QueueStatus(MatchmakingPhase.Searching, packed, capacity, 0, index + 1);
        }

        private bool IsOver(RaidInstance raid, double now)
        {
            // Everyone is out (dead, extracted or gone), or the timer ran out and the last characters had time to be removed.
            if (now >= raid.EndTime + OvertimeCloseSeconds) return true;
            return _playersInRaid == 0 && now >= raid.StartTime + EmptyCloseGraceSeconds;
        }

        /// <summary>
        /// First-fit in queue order up to capacity, among squads of the head squad's difficulty; fills <see cref="_pack"/>
        /// with queue indices and returns the player total.
        /// </summary>
        private int BuildPack()
        {
            _pack.Clear();
            if (_queue.Count == 0) return 0;
            RaidDifficulty difficulty = _queue[0].Difficulty;
            int total = 0;
            for (int i = 0; i < _queue.Count && total < Settings.MaxPlayers; i++)
            {
                if (_queue[i].Difficulty != difficulty || total + _queue[i].Size > Settings.MaxPlayers) continue;
                _pack.Add(i);
                total += _queue[i].Size;
            }
            return total;
        }

        private int IndexOf(int squadId)
        {
            for (int i = 0; i < _queue.Count; i++)
                if (_queue[i].SquadId == squadId) return i;
            return -1;
        }

        private static int Seconds(double value) => value <= 0 ? 0 : (int)Math.Ceiling(value - 1e-9);

        private readonly struct Entry
        {
            public Entry(int squadId, int size, RaidDifficulty difficulty)
            {
                SquadId = squadId;
                Size = size;
                Difficulty = difficulty;
            }

            public int SquadId { get; }
            public int Size { get; }
            public RaidDifficulty Difficulty { get; }
        }
    }
}
