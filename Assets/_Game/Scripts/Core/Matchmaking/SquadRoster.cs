using System;
using System.Collections.Generic;
using System.Text;

namespace ExtractionShooter.Core.Matchmaking
{
    /// <summary>Outcome of a <see cref="SquadRoster"/> request.</summary>
    public enum SquadResult : byte
    {
        Ok = 0,
        NotInSquad = 1,
        NotFound = 2,
        Full = 3,
        NotLeader = 4,

        /// <summary>The squad is searching for a match: nobody can join until the search stops.</summary>
        Locked = 5,
        NoInvite = 6,
        InvalidTarget = 7,
        AlreadyMember = 8,
    }

    /// <summary>
    /// A group of players who queue and deploy together. The first member is the leader; when the leader
    /// leaves, the next member in join order takes over.
    /// </summary>
    public sealed class Squad
    {
        private readonly List<int> _members = new List<int>();
        private readonly HashSet<int> _ready = new HashSet<int>();

        internal Squad(int id, string code)
        {
            Id = id;
            Code = code;
        }

        /// <summary>Unique for the server's lifetime, never 0.</summary>
        public int Id { get; }

        /// <summary>Short code other players type to join.</summary>
        public string Code { get; }

        /// <summary>Members in join order; the first is the leader.</summary>
        public IReadOnlyList<int> Members => _members;

        public int Leader => _members.Count > 0 ? _members[0] : 0;
        public int Count => _members.Count;

        /// <summary>Set by the lobby while the squad is in the matchmaking queue: nobody may join.</summary>
        public bool Locked { get; set; }

        public bool Contains(int player) => _members.Contains(player);
        public bool IsReady(int player) => _ready.Contains(player);

        public bool AllReady
        {
            get
            {
                foreach (int member in _members)
                    if (!_ready.Contains(member)) return false;
                return _members.Count > 0;
            }
        }

        internal void Add(int player) => _members.Add(player);

        internal void Remove(int player)
        {
            _members.Remove(player);
            _ready.Remove(player);
        }

        internal void SetReady(int player, bool ready)
        {
            if (ready) _ready.Add(player);
            else _ready.Remove(player);
        }

        internal void ClearReady() => _ready.Clear();
    }

    /// <summary>
    /// Server-side squads: create, join by code, invite and accept, leave, ready up. Players are identified by any
    /// stable int (the connection's client id). A player is in at most one squad; joining another one
    /// leaves the current one first. Pure logic, unit-tested.
    /// </summary>
    public sealed class SquadRoster
    {
        /// <summary>No 0/O or 1/I, so codes read back unambiguously.</summary>
        public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int CodeLength = 5;

        private readonly Dictionary<int, Squad> _squads = new Dictionary<int, Squad>();
        private readonly Dictionary<string, Squad> _byCode = new Dictionary<string, Squad>(StringComparer.Ordinal);
        private readonly Dictionary<int, Squad> _byPlayer = new Dictionary<int, Squad>();
        private readonly Dictionary<int, int> _invites = new Dictionary<int, int>();
        private readonly Random _random;
        private int _nextId = 1;

        public SquadRoster(int maxSize = 4, int seed = 0)
        {
            MaxSize = Math.Max(1, maxSize);
            _random = seed != 0 ? new Random(seed) : new Random();
        }

        public int MaxSize { get; }

        public IEnumerable<Squad> Squads => _squads.Values;

        /// <summary>The player's squad, or null.</summary>
        public Squad SquadOf(int player) => _byPlayer.TryGetValue(player, out Squad squad) ? squad : null;

        public Squad Find(int squadId) => _squads.TryGetValue(squadId, out Squad squad) ? squad : null;

        /// <summary>The squad with this code (case and surrounding spaces ignored), or null.</summary>
        public Squad FindByCode(string code)
        {
            string key = NormalizeCode(code);
            return key != null && _byCode.TryGetValue(key, out Squad squad) ? squad : null;
        }

        /// <summary>Squad id of the player's pending invite, or 0.</summary>
        public int PendingInvite(int player) => _invites.TryGetValue(player, out int squadId) && _squads.ContainsKey(squadId) ? squadId : 0;

        /// <summary>A new squad led by the player (who leaves any current squad first).</summary>
        public Squad Create(int player)
        {
            Leave(player);
            var squad = new Squad(_nextId++, NewCode());
            _squads.Add(squad.Id, squad);
            _byCode.Add(squad.Code, squad);
            AddMember(squad, player);
            return squad;
        }

        /// <summary>The player's squad, creating a squad of one if they have none.</summary>
        public Squad Ensure(int player) => SquadOf(player) ?? Create(player);

        public SquadResult Join(int player, string code)
        {
            Squad squad = FindByCode(code);
            return squad == null ? SquadResult.NotFound : JoinSquad(player, squad);
        }

        /// <summary>Invites a player into the inviter's squad. The invitee keeps one pending invite: the latest.</summary>
        public SquadResult Invite(int from, int to)
        {
            Squad squad = SquadOf(from);
            if (squad == null) return SquadResult.NotInSquad;
            if (to == from) return SquadResult.InvalidTarget;
            if (squad.Contains(to)) return SquadResult.AlreadyMember;
            if (squad.Count >= MaxSize) return SquadResult.Full;
            if (squad.Locked) return SquadResult.Locked;
            _invites[to] = squad.Id;
            return SquadResult.Ok;
        }

        public SquadResult AcceptInvite(int player)
        {
            int squadId = PendingInvite(player);
            _invites.Remove(player);
            Squad squad = Find(squadId);
            return squad == null ? SquadResult.NoInvite : JoinSquad(player, squad);
        }

        public void DeclineInvite(int player) => _invites.Remove(player);

        /// <summary>Leaves the squad (a squad left empty is disbanded). Also drops the player's pending invite.</summary>
        public SquadResult Leave(int player)
        {
            _invites.Remove(player);
            Squad squad = SquadOf(player);
            if (squad == null) return SquadResult.NotInSquad;

            squad.Remove(player);
            _byPlayer.Remove(player);
            if (squad.Count == 0)
            {
                _squads.Remove(squad.Id);
                _byCode.Remove(squad.Code);
            }
            return SquadResult.Ok;
        }

        public SquadResult SetReady(int player, bool ready)
        {
            Squad squad = SquadOf(player);
            if (squad == null) return SquadResult.NotInSquad;
            squad.SetReady(player, ready);
            return SquadResult.Ok;
        }

        /// <summary>Everyone in the squad back to not ready (after a launch).</summary>
        public void ClearReady(int squadId) => Find(squadId)?.ClearReady();

        /// <summary>Upper case without spaces, or null when it cannot be a code.</summary>
        public static string NormalizeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            string key = code.Trim().ToUpperInvariant();
            if (key.Length != CodeLength) return null;
            foreach (char c in key)
                if (CodeAlphabet.IndexOf(c) < 0) return null;
            return key;
        }

        private SquadResult JoinSquad(int player, Squad squad)
        {
            if (squad.Contains(player)) return SquadResult.AlreadyMember;
            if (squad.Count >= MaxSize) return SquadResult.Full;
            if (squad.Locked) return SquadResult.Locked;

            Leave(player);
            AddMember(squad, player);
            return SquadResult.Ok;
        }

        private void AddMember(Squad squad, int player)
        {
            squad.Add(player);
            _byPlayer[player] = squad;
            _invites.Remove(player);
        }

        private string NewCode()
        {
            var builder = new StringBuilder(CodeLength);
            while (true)
            {
                builder.Clear();
                for (int i = 0; i < CodeLength; i++) builder.Append(CodeAlphabet[_random.Next(CodeAlphabet.Length)]);
                string code = builder.ToString();
                if (!_byCode.ContainsKey(code)) return code;
            }
        }
    }
}
