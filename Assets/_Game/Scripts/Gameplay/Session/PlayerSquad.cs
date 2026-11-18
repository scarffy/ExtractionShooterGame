using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Matchmaking;
using ExtractionShooter.Core.Raid;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>Squad and matchmaking requests a player can make from the hideout.</summary>
    public enum SquadCommand : byte
    {
        Create = 0,
        Invite = 1,
        AcceptInvite = 2,
        DeclineInvite = 3,
        Join = 4,
        Leave = 5,
        Ready = 6,
        Unready = 7,
        FindMatch = 8,
        CancelMatch = 9,
        /// <summary>Leader: the difficulty for the next search (argument: "Easy", "Normal" or "Hard").</summary>
        SetDifficulty = 10,
    }

    /// <summary>
    /// A session's squad membership and matchmaking state, on the <see cref="PlayerSession"/> object.
    /// The owner sends intents (<see cref="SquadCommand"/>); the server's <see cref="SquadLobby"/> validates them,
    /// owns the squads and the queue, and writes the results back into these SyncVars.
    /// <list type="bullet">
    /// <item>Everyone sees <see cref="SquadId"/>, <see cref="IsLeader"/> and <see cref="IsReady"/>, so a client lists a
    /// squad's members by gathering the sessions with the same squad id (<see cref="GetSquad"/>).</item>
    /// <item>Only the owner sees the squad code, its pending invite and its queue status.</item>
    /// </list>
    /// In a raid, use <see cref="SquadMember"/> on the character instead: it carries the squad the character deployed with.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerSession))]
    public sealed class PlayerSquad : NetworkBehaviour
    {
        private static readonly List<PlayerSquad> ClientList = new List<PlayerSquad>();
        private static readonly List<PlayerSquad> ServerList = new List<PlayerSquad>();

        private readonly SyncVar<int> _squadId = new SyncVar<int>();
        private readonly SyncVar<bool> _isLeader = new SyncVar<bool>();
        private readonly SyncVar<bool> _isReady = new SyncVar<bool>();
        private readonly SyncVar<string> _squadCode = new SyncVar<string>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncVar<string> _inviteFrom = new SyncVar<string>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncVar<MatchmakingPhase> _queuePhase = new SyncVar<MatchmakingPhase>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncVar<int> _queuePlayers = new SyncVar<int>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncVar<int> _queueCapacity = new SyncVar<int>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncVar<int> _queueSeconds = new SyncVar<int>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncVar<RaidDifficulty> _difficulty = new SyncVar<RaidDifficulty>(RaidDifficulty.Normal,
            new SyncTypeSettings(ReadPermission.OwnerOnly));

        private PlayerSession _session;

        /// <summary>Client side: any session's squad, leader or ready state changed (refresh member lists).</summary>
        public static event Action AnyChanged;

        /// <summary>Client side: this session's squad state, invite or queue status changed.</summary>
        public event Action Changed;

        /// <summary>Owner side: feedback from the server ("Invited Kim.", "That squad is full.").</summary>
        public event Action<string> MessageReceived;

        /// <summary>Server: every spawned squad state, for the lobby.</summary>
        internal static IReadOnlyList<PlayerSquad> ServerAll => ServerList;

        public PlayerSession Session => _session;

        /// <summary>The squad this session is in, or <see cref="SquadRules.NoSquad"/> (0).</summary>
        public int SquadId => _squadId.Value;

        public bool InSquad => _squadId.Value != SquadRules.NoSquad;
        public bool IsLeader => _isLeader.Value;
        public bool IsReady => _isReady.Value;

        /// <summary>Owner side: the code others type to join this squad.</summary>
        public string SquadCode => _squadCode.Value ?? string.Empty;

        /// <summary>Owner side: name of the player whose invite is pending, or empty.</summary>
        public string InviteFrom => _inviteFrom.Value ?? string.Empty;

        public bool HasInvite => !string.IsNullOrEmpty(_inviteFrom.Value);

        /// <summary>Owner side: where this player's squad stands in matchmaking.</summary>
        public QueueStatus Queue => new QueueStatus(_queuePhase.Value, _queuePlayers.Value, _queueCapacity.Value, _queueSeconds.Value, 0);

        public bool IsSearching => _queuePhase.Value != MatchmakingPhase.Idle;

        /// <summary>Owner side: the difficulty the squad's leader picked (a player with no squad picks their own).</summary>
        public RaidDifficulty Difficulty => _difficulty.Value;

        /// <summary>Server: the player id used by the lobby (the owner's client id, cached at spawn).</summary>
        internal int ServerPlayerId { get; private set; } = -1;

        private void Awake()
        {
            _session = GetComponent<PlayerSession>();
            _squadId.OnChange += OnSharedIntChanged;
            _isLeader.OnChange += OnSharedBoolChanged;
            _isReady.OnChange += OnSharedBoolChanged;
            _squadCode.OnChange += OnStringChanged;
            _inviteFrom.OnChange += OnStringChanged;
            _queuePhase.OnChange += OnPhaseChanged;
            _queuePlayers.OnChange += OnIntChanged;
            _queueCapacity.OnChange += OnIntChanged;
            _queueSeconds.OnChange += OnIntChanged;
            _difficulty.OnChange += OnDifficultyChanged;
        }

        private void OnDestroy()
        {
            _squadId.OnChange -= OnSharedIntChanged;
            _isLeader.OnChange -= OnSharedBoolChanged;
            _isReady.OnChange -= OnSharedBoolChanged;
            _squadCode.OnChange -= OnStringChanged;
            _inviteFrom.OnChange -= OnStringChanged;
            _queuePhase.OnChange -= OnPhaseChanged;
            _queuePlayers.OnChange -= OnIntChanged;
            _queueCapacity.OnChange -= OnIntChanged;
            _queueSeconds.OnChange -= OnIntChanged;
            _difficulty.OnChange -= OnDifficultyChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerPlayerId = OwnerId;
            if (!ServerList.Contains(this)) ServerList.Add(this);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerList.Remove(this);
            // A disconnecting player leaves their squad, and the squad's search stops.
            if (SquadLobby.Instance != null) SquadLobby.Instance.ServerRemovePlayer(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!ClientList.Contains(this)) ClientList.Add(this);
            AnyChanged?.Invoke();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            ClientList.Remove(this);
            AnyChanged?.Invoke();
        }

        // ---------------- Queries ----------------

        /// <summary>
        /// Client side: the spawned sessions in <paramref name="squadId"/>, leader first, then in spawn order.
        /// Clears <paramref name="members"/> first; leaves it empty for <see cref="SquadRules.NoSquad"/>.
        /// </summary>
        public static void GetSquad(int squadId, List<PlayerSquad> members)
        {
            members.Clear();
            if (squadId == SquadRules.NoSquad) return;
            foreach (PlayerSquad squad in ClientList)
            {
                if (squad == null || squad._squadId.Value != squadId) continue;
                if (squad._isLeader.Value) members.Insert(0, squad);
                else members.Add(squad);
            }
        }

        /// <summary>Server: the lobby squad of a connected player (by client id), or <see cref="SquadRules.NoSquad"/>.</summary>
        public static int ServerSquadIdOf(int clientId)
        {
            foreach (PlayerSquad squad in ServerList)
                if (squad != null && squad.ServerPlayerId == clientId) return squad._squadId.Value;
            return SquadRules.NoSquad;
        }

        // ---------------- Owner requests ----------------

        public void RequestCreate() => Send(SquadCommand.Create);

        /// <param name="playerName">Name of a signed-in player.</param>
        public void RequestInvite(string playerName) => Send(SquadCommand.Invite, playerName);

        public void RequestAcceptInvite() => Send(SquadCommand.AcceptInvite);

        public void RequestDeclineInvite() => Send(SquadCommand.DeclineInvite);

        public void RequestJoin(string code) => Send(SquadCommand.Join, code);

        public void RequestLeave() => Send(SquadCommand.Leave);

        public void RequestSetReady(bool ready) => Send(ready ? SquadCommand.Ready : SquadCommand.Unready);

        /// <summary>
        /// Leader only: queues the squad (a player with no squad queues solo) at this difficulty. Every member must be ready.
        /// </summary>
        public void RequestFindMatch(RaidDifficulty difficulty) => Send(SquadCommand.FindMatch, difficulty.DisplayName());

        /// <summary>Leader (or a player with no squad): the difficulty for the next search; squadmates see it.</summary>
        public void RequestSetDifficulty(RaidDifficulty difficulty) => Send(SquadCommand.SetDifficulty, difficulty.DisplayName());

        public void RequestCancelMatch() => Send(SquadCommand.CancelMatch);

        private void Send(SquadCommand command, string argument = "")
        {
            if (IsOwner) ServerCommand(command, argument ?? string.Empty);
        }

        [ServerRpc]
        private void ServerCommand(SquadCommand command, string argument)
        {
            if (SquadLobby.Instance == null)
            {
                ServerSendMessage("Matchmaking is not available on this server.");
                return;
            }
            SquadLobby.Instance.ServerHandle(this, command, argument);
        }

        // ---------------- Server: state from the lobby ----------------

        /// <summary>Server, lobby: publishes this player's squad and queue state (unchanged values are not resent).</summary>
        internal void ServerApply(int squadId, string code, bool isLeader, bool isReady, string inviteFrom, QueueStatus queue,
            RaidDifficulty difficulty)
        {
            if (!IsServerInitialized) return;
            _difficulty.Value = difficulty;
            if (!IsServerInitialized) return;
            _squadId.Value = squadId;
            _squadCode.Value = code ?? string.Empty;
            _isLeader.Value = isLeader;
            _isReady.Value = isReady;
            _inviteFrom.Value = inviteFrom ?? string.Empty;
            _queuePhase.Value = queue.Phase;
            _queuePlayers.Value = queue.PlayersFound;
            _queueCapacity.Value = queue.Capacity;
            _queueSeconds.Value = queue.SecondsLeft;
        }

        /// <summary>Server: shows a line of feedback to the owner.</summary>
        internal void ServerSendMessage(string message)
        {
            if (IsServerInitialized && Owner != null && !string.IsNullOrEmpty(message)) TargetMessage(Owner, message);
        }

        [TargetRpc]
        private void TargetMessage(NetworkConnection connection, string message) => MessageReceived?.Invoke(message);

        // ---------------- Client notifications ----------------

        private void OnSharedIntChanged(int previous, int current, bool asServer)
        {
            if (asServer) return;
            Changed?.Invoke();
            AnyChanged?.Invoke();
        }

        private void OnSharedBoolChanged(bool previous, bool current, bool asServer)
        {
            if (asServer) return;
            Changed?.Invoke();
            AnyChanged?.Invoke();
        }

        private void OnStringChanged(string previous, string current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }

        private void OnPhaseChanged(MatchmakingPhase previous, MatchmakingPhase current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }

        private void OnIntChanged(int previous, int current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }

        private void OnDifficultyChanged(RaidDifficulty previous, RaidDifficulty current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ClientList.Clear();
            ServerList.Clear();
            AnyChanged = null;
        }
    }
}
