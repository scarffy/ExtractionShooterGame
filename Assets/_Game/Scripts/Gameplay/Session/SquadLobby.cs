using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Matchmaking;
using ExtractionShooter.Core.Persistence;
using ExtractionShooter.Core.Raid;
using ExtractionShooter.Gameplay.Raid;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>
    /// Server-side squads and matchmaking for this server's one map. Owns a <see cref="SquadRoster"/> and a
    /// <see cref="MatchmakingService"/>, handles the requests players send through <see cref="PlayerSquad"/>, ticks
    /// the queue every frame and publishes each player's squad and queue state back to their <see cref="PlayerSquad"/>.
    /// <list type="bullet">
    /// <item>Raids are instances that run one at a time on the shared map (see <see cref="RaidInstance"/>): queued squads
    /// join the running one during its late-join window, otherwise they wait for the next.</item>
    /// <item>When squads are admitted the lobby raises <see cref="ServerRaidOpened"/> (new instance: reset the map) and
    /// <see cref="ServerSquadDeploying"/> per squad; the raid director spawns the characters.</item>
    /// <item>Any change to a searching squad (a member leaves, un-readies or disconnects) stops its search.</item>
    /// <item>The map offers two difficulties (<see cref="MapDifficulties"/>). The leader picks one; squads only share a raid
    /// with squads of the same difficulty, and the raid's bots and loot follow it (<see cref="ModifiersFor"/>).</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SquadLobby : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        [Header("Squads")]
        [Tooltip("Most players in one squad.")]
        [SerializeField, Range(1, 8)] private int _maxSquadSize = 4;

        [Tooltip("Whether squadmates can hurt each other.")]
        [SerializeField] private bool _friendlyFire;

        [Header("Difficulty")]
        [Tooltip("The two difficulties this map offers: Easy/Normal or Normal/Hard.")]
        [SerializeField] private MapDifficulties _difficulties = MapDifficulties.EasyNormal;

        [Tooltip("Bot and loot changes per difficulty. Built-in values are used when empty.")]
        [SerializeField] private RaidDifficultySettings _difficultySettings;

        [Header("Matchmaking")]
        [Tooltip("Most players in one raid instance.")]
        [SerializeField, Min(1)] private int _maxPlayersPerRaid = 12;

        [Tooltip("Queued players needed before the launch countdown starts.")]
        [SerializeField, Min(1)] private int _minPlayersToStart = 1;

        [Tooltip("Seconds from reaching the minimum to launching; a full raid launches at once.")]
        [SerializeField, Min(0f)] private float _countdownSeconds = 15f;

        [Tooltip("Seconds after a raid starts during which queued squads may still join it while it has room.")]
        [SerializeField, Min(0f)] private float _lateJoinSeconds = 120f;

        [Tooltip("Length of a raid instance. Characters still inside when it ends are missing in action.")]
        [SerializeField, Min(10f)] private float _raidDurationSeconds = 1200f;

        private readonly Dictionary<int, string> _inviteNames = new Dictionary<int, string>();
        private readonly Dictionary<int, RaidDifficulty> _soloDifficulty = new Dictionary<int, RaidDifficulty>();
        private readonly List<RaidAssignment> _admitted = new List<RaidAssignment>();
        private readonly List<PlayerSession> _members = new List<PlayerSession>();
        private SquadRoster _roster;
        private MatchmakingService _matchmaking;

        /// <summary>The scene's lobby, or null.</summary>
        public static SquadLobby Instance { get; private set; }

        /// <summary>Server: a new raid instance launched; reset the map before its squads are deployed.</summary>
        public event Action<RaidInstance> ServerRaidOpened;

        /// <summary>Server: deploy these members of one squad (squad id) into the raid, together.</summary>
        public event Action<RaidInstance, int, IReadOnlyList<PlayerSession>> ServerSquadDeploying;

        public bool FriendlyFire => _friendlyFire;

        /// <summary>The two difficulties this map offers (the same on server and clients: it is scene data).</summary>
        public MapDifficulties Difficulties => _difficulties;

        /// <summary>What a raid at this difficulty changes (bots, loot).</summary>
        public DifficultyModifiers ModifiersFor(RaidDifficulty difficulty) =>
            _difficultySettings != null ? _difficultySettings.For(difficulty) : DifficultyModifiers.DefaultFor(difficulty);

        /// <summary>Server: the raid instance running now, or null.</summary>
        public RaidInstance ActiveRaid => _matchmaking != null ? _matchmaking.ActiveRaid : null;

        private bool IsServer => _networkManager != null && _networkManager.IsServerStarted;

        private void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"[{nameof(SquadLobby)}] More than one lobby in the scene; using the newest.", this);
            Instance = this;
            if (_networkManager == null) _networkManager = GetComponent<NetworkManager>();
            if (_networkManager == null) Debug.LogError($"[{nameof(SquadLobby)}] No NetworkManager assigned.", this);
            ResetState();
        }

        // Start, not OnEnable: the NetworkManager initialises its managers in Awake.
        private void Start()
        {
            if (_networkManager != null)
                _networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
            if (Instance == this) Instance = null;
        }

        private void HandleServerConnectionState(ServerConnectionStateArgs args)
        {
            // A restarted server starts with no squads and no queue.
            if (args.ConnectionState == LocalConnectionState.Stopped) ResetState();
        }

        private void ResetState()
        {
            _roster = new SquadRoster(_maxSquadSize);
            _matchmaking = new MatchmakingService(new MatchmakingSettings(_maxPlayersPerRaid, _minPlayersToStart,
                _countdownSeconds, _lateJoinSeconds, _raidDurationSeconds));
            _inviteNames.Clear();
            _soloDifficulty.Clear();
        }

        // ---------------- Tick ----------------

        private void Update()
        {
            if (!IsServer) return;

            double now = Time.timeAsDouble;
            _matchmaking.ReportPlayersInRaid(CountPlayersInRaid());

            // Copy first: deploying runs other code that could touch the queue.
            _admitted.Clear();
            _admitted.AddRange(_matchmaking.Tick(now));
            foreach (RaidAssignment assignment in _admitted) Deploy(assignment);

            Publish(now);
        }

        /// <summary>Only one raid instance runs at a time, so every session in a raid is in the active one.</summary>
        private static int CountPlayersInRaid()
        {
            int count = 0;
            foreach (PlayerSquad player in PlayerSquad.ServerAll)
                if (player != null && player.Session != null && player.Session.State == SessionState.InRaid) count++;
            return count;
        }

        private void Deploy(RaidAssignment assignment)
        {
            if (assignment.OpensRaid)
            {
                Debug.Log($"[{nameof(SquadLobby)}] Raid #{assignment.Raid.Id} ({assignment.Raid.Difficulty.DisplayName()}) launched.");
                ServerRaidOpened?.Invoke(assignment.Raid);
            }

            Squad squad = _roster.Find(assignment.SquadId);
            if (squad == null) return;

            squad.Locked = false;
            _members.Clear();
            foreach (int member in squad.Members)
            {
                PlayerSquad player = FindPlayer(member);
                if (player != null && player.Session.State == SessionState.Hideout) _members.Add(player.Session);
            }
            _roster.ClearReady(squad.Id);

            Debug.Log($"[{nameof(SquadLobby)}] Squad {squad.Code} ({_members.Count}) → raid #{assignment.Raid.Id}.");
            if (_members.Count > 0) ServerSquadDeploying?.Invoke(assignment.Raid, squad.Id, _members);
        }

        /// <summary>Writes every player's squad, invite and queue state into their <see cref="PlayerSquad"/>.</summary>
        private void Publish(double now)
        {
            foreach (PlayerSquad player in PlayerSquad.ServerAll)
            {
                if (player == null) continue;
                int id = player.ServerPlayerId;
                Squad squad = _roster.SquadOf(id);
                string invite = _roster.PendingInvite(id) != 0 && _inviteNames.TryGetValue(id, out string from) ? from : string.Empty;
                QueueStatus queue = squad != null ? _matchmaking.GetStatus(squad.Id, now) : QueueStatus.Idle;
                player.ServerApply(squad != null ? squad.Id : SquadRules.NoSquad, squad != null ? squad.Code : string.Empty,
                    squad != null && squad.Leader == id, squad != null && squad.IsReady(id), invite, queue, DifficultyOf(id, squad));
            }
        }

        // ---------------- Requests ----------------

        /// <summary>Server: a request from a player's <see cref="PlayerSquad"/>.</summary>
        internal void ServerHandle(PlayerSquad player, SquadCommand command, string argument)
        {
            if (player == null || player.Session == null) return;
            if (player.Session.State != SessionState.Hideout)
            {
                player.ServerSendMessage("Squads are managed from the hideout.");
                return;
            }

            int id = player.ServerPlayerId;
            Squad before = _roster.SquadOf(id);
            switch (command)
            {
                case SquadCommand.Create:
                    if (before != null && before.Count == 1)
                    {
                        player.ServerSendMessage("You already lead a squad.");
                        return;
                    }
                    StopSearchFor(before, $"{player.Session.PlayerName} left the squad.");
                    _roster.Create(id);
                    player.ServerSendMessage("Squad created. Share the code or invite players by name.");
                    break;

                case SquadCommand.Invite:
                    Invite(player, argument);
                    break;

                case SquadCommand.AcceptInvite:
                    JoinResult(player, before, _roster.Find(_roster.PendingInvite(id)), _roster.AcceptInvite(id));
                    break;

                case SquadCommand.DeclineInvite:
                    _roster.DeclineInvite(id);
                    _inviteNames.Remove(id);
                    break;

                case SquadCommand.Join:
                    JoinResult(player, before, _roster.FindByCode(argument), _roster.Join(id, argument));
                    break;

                case SquadCommand.Leave:
                    if (before == null) return;
                    StopSearchFor(before, $"{player.Session.PlayerName} left the squad.");
                    _roster.Leave(id);
                    player.ServerSendMessage("You left the squad.");
                    break;

                case SquadCommand.Ready:
                case SquadCommand.Unready:
                    bool ready = command == SquadCommand.Ready;
                    if (before == null)
                    {
                        player.ServerSendMessage(SquadRules.Describe(SquadResult.NotInSquad));
                        return;
                    }
                    if (!ready) StopSearchFor(before, $"{player.Session.PlayerName} is no longer ready.");
                    _roster.SetReady(id, ready);
                    break;

                case SquadCommand.FindMatch:
                    FindMatch(player, argument);
                    break;

                case SquadCommand.SetDifficulty:
                    SetDifficulty(player, before, argument);
                    break;

                case SquadCommand.CancelMatch:
                    if (before != null && _matchmaking.IsQueued(before.Id))
                        StopSearchFor(before, $"{player.Session.PlayerName} cancelled the search.");
                    break;
            }
            Publish(Time.timeAsDouble);
        }

        /// <summary>Server: a player disconnected (their session is despawning). They leave their squad.</summary>
        internal void ServerRemovePlayer(PlayerSquad player)
        {
            if (player == null || _roster == null) return;
            int id = player.ServerPlayerId;
            Squad squad = _roster.SquadOf(id);
            string name = player.Session != null ? player.Session.PlayerName : "A player";
            StopSearchFor(squad, $"{name} disconnected.");
            _roster.Leave(id);
            _inviteNames.Remove(id);
            _soloDifficulty.Remove(id);
        }

        private void Invite(PlayerSquad player, string targetName)
        {
            PlayerSquad target = FindPlayerByName(targetName);
            if (target == null)
            {
                player.ServerSendMessage($"No player named '{(targetName ?? string.Empty).Trim()}' is online.");
                return;
            }
            if (target == player)
            {
                player.ServerSendMessage(SquadRules.Describe(SquadResult.InvalidTarget));
                return;
            }

            // Inviting with no squad creates one.
            _roster.Ensure(player.ServerPlayerId);
            SquadResult result = _roster.Invite(player.ServerPlayerId, target.ServerPlayerId);
            if (result != SquadResult.Ok)
            {
                player.ServerSendMessage(SquadRules.Describe(result));
                return;
            }

            _inviteNames[target.ServerPlayerId] = player.Session.PlayerName;
            player.ServerSendMessage($"Invited {target.Session.PlayerName}.");
        }

        private void JoinResult(PlayerSquad player, Squad before, Squad target, SquadResult result)
        {
            if (result != SquadResult.Ok)
            {
                player.ServerSendMessage(SquadRules.Describe(result));
                return;
            }

            _inviteNames.Remove(player.ServerPlayerId);
            if (before != null && before != target) StopSearchFor(before, $"{player.Session.PlayerName} left the squad.");
            player.ServerSendMessage(target != null ? $"Joined squad {target.Code}." : "Joined the squad.");
            if (target != null) Broadcast(target, $"{player.Session.PlayerName} joined the squad.", player);
        }

        /// <summary>The difficulty shown to a player: their squad's, or their own pick when they have no squad.</summary>
        private RaidDifficulty DifficultyOf(int playerId, Squad squad)
        {
            if (squad != null) return _difficulties.Clamp(squad.Difficulty);
            return _soloDifficulty.TryGetValue(playerId, out RaidDifficulty own) ? _difficulties.Clamp(own) : RaidDifficulty.Normal;
        }

        private void SetDifficulty(PlayerSquad player, Squad squad, string argument)
        {
            if (!RaidDifficulties.TryParse(argument, out RaidDifficulty difficulty)) return;
            if (!_difficulties.Allows(difficulty))
            {
                player.ServerSendMessage($"This map has no {difficulty.DisplayName()} mode.");
                return;
            }

            int id = player.ServerPlayerId;
            if (squad == null)
            {
                _soloDifficulty[id] = difficulty;
                return;
            }
            if (squad.Leader != id)
            {
                player.ServerSendMessage("Only the squad leader picks the difficulty.");
                return;
            }
            if (_matchmaking.IsQueued(squad.Id))
            {
                player.ServerSendMessage("Cancel the search to change the difficulty.");
                return;
            }
            if (squad.Difficulty == difficulty) return;
            squad.Difficulty = difficulty;
            Broadcast(squad, $"Difficulty: {difficulty.DisplayName()}.", player);
        }

        private void FindMatch(PlayerSquad player, string argument)
        {
            int id = player.ServerPlayerId;
            Squad existing = _roster.SquadOf(id);
            Squad squad = _roster.Ensure(id);
            if (squad.Leader != id)
            {
                player.ServerSendMessage("Only the squad leader can start the search.");
                return;
            }
            if (_matchmaking.IsQueued(squad.Id)) return;

            // The search names its difficulty (a solo player's pick lives outside the squad made for them here).
            if (RaidDifficulties.TryParse(argument, out RaidDifficulty picked)) squad.Difficulty = picked;
            else if (existing == null && _soloDifficulty.TryGetValue(id, out RaidDifficulty own)) squad.Difficulty = own;
            if (!_difficulties.Allows(squad.Difficulty))
            {
                player.ServerSendMessage($"This map has no {squad.Difficulty.DisplayName()} mode.");
                squad.Difficulty = _difficulties.Clamp(squad.Difficulty);
                return;
            }

            // Pressing Find match readies the leader.
            _roster.SetReady(id, true);
            int notReady = 0;
            foreach (int member in squad.Members)
            {
                PlayerSquad other = FindPlayer(member);
                if (other == null || other.Session.State != SessionState.Hideout)
                {
                    player.ServerSendMessage("Every squadmate must be in the hideout.");
                    return;
                }
                if (!squad.IsReady(member)) notReady++;
            }
            if (notReady > 0)
            {
                player.ServerSendMessage(notReady == 1 ? "Waiting for 1 squadmate to ready up." : $"Waiting for {notReady} squadmates to ready up.");
                return;
            }

            if (!_matchmaking.Enqueue(squad.Id, squad.Count, squad.Difficulty))
            {
                player.ServerSendMessage($"A squad of {squad.Count} cannot be matched on this server.");
                return;
            }
            squad.Locked = true;
            Broadcast(squad, $"Searching for a {squad.Difficulty.DisplayName()} raid…");
        }

        /// <summary>Takes a searching squad out of the queue and tells its members why. No-op if it is not searching.</summary>
        private void StopSearchFor(Squad squad, string reason)
        {
            if (squad == null || !_matchmaking.Cancel(squad.Id)) return;
            squad.Locked = false;
            Broadcast(squad, $"Search stopped: {reason}");
        }

        private void Broadcast(Squad squad, string message, PlayerSquad except = null)
        {
            foreach (int member in squad.Members)
            {
                PlayerSquad player = FindPlayer(member);
                if (player != null && player != except) player.ServerSendMessage(message);
            }
        }

        private static PlayerSquad FindPlayer(int playerId)
        {
            foreach (PlayerSquad player in PlayerSquad.ServerAll)
                if (player != null && player.ServerPlayerId == playerId) return player;
            return null;
        }

        /// <summary>A signed-in player by name (case-insensitive), or null.</summary>
        private static PlayerSquad FindPlayerByName(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName)) return null;
            string key = ProfileNames.ToKey(playerName.Trim());
            foreach (PlayerSquad player in PlayerSquad.ServerAll)
            {
                PlayerSession session = player != null ? player.Session : null;
                if (session != null && session.State != SessionState.SigningIn && !string.IsNullOrEmpty(session.PlayerName) &&
                    ProfileNames.ToKey(session.PlayerName) == key)
                    return player;
            }
            return null;
        }
    }
}
