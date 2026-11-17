using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Matchmaking;
using ExtractionShooter.Gameplay.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>
    /// The squad a player character deployed with, visible to every peer (maps, nameplates and the like can tell
    /// teammates from enemies with <see cref="IsSquadmate"/>). Set by the raid director right after spawning.
    /// <para>Also the friendly-fire rule: as an <see cref="IDamageModifier"/> it cancels hits from squadmates unless the
    /// lobby's friendly fire is on (<see cref="SquadRules.AllowsDamage"/>). It must come before armor in the component
    /// order, so friendly hits do not wear armor down either.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter))]
    public sealed class SquadMember : NetworkBehaviour, IDamageModifier
    {
        private static readonly Dictionary<int, SquadMember> ServerByClient = new Dictionary<int, SquadMember>();

        private static readonly List<SquadMember> ClientAll = new List<SquadMember>();

        private readonly SyncVar<int> _squadId = new SyncVar<int>();
        private readonly SyncVar<string> _playerName = new SyncVar<string>();

        private int _serverClientId = -1;

        /// <summary>Client side: a character's squad id arrived or changed.</summary>
        public static event Action<SquadMember> AnyChanged;

        /// <summary>The squad this character deployed with, or <see cref="SquadRules.NoSquad"/> (0).</summary>
        public int SquadId => _squadId.Value;

        /// <summary>The player's profile name, for teammate lists.</summary>
        public string PlayerName => _playerName.Value;

        /// <summary>Client side: every spawned character with a squad tag.</summary>
        public static IReadOnlyList<SquadMember> ClientAllMembers => ClientAll;

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!ClientAll.Contains(this)) ClientAll.Add(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            ClientAll.Remove(this);
        }

        private void Awake() => _squadId.OnChange += OnSquadChanged;

        private void OnDestroy() => _squadId.OnChange -= OnSquadChanged;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _serverClientId = OwnerId;
            if (_serverClientId >= 0) ServerByClient[_serverClientId] = this;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (ServerByClient.TryGetValue(_serverClientId, out SquadMember current) && current == this)
                ServerByClient.Remove(_serverClientId);
        }

        /// <summary>Server, raid director: tags the character with the squad it deployed with.</summary>
        public void ServerAssign(int squadId, string playerName = null)
        {
            if (!IsServerInitialized) return;
            _squadId.Value = squadId;
            if (playerName != null) _playerName.Value = playerName;
        }

        /// <summary>Same squad (both in one). A character is not its own squadmate.</summary>
        public bool IsSquadmate(SquadMember other) =>
            other != null && other != this && _squadId.Value != SquadRules.NoSquad && other._squadId.Value == _squadId.Value;

        /// <summary>Whether two characters deployed in the same squad.</summary>
        public static bool AreSquadmates(PlayerCharacter a, PlayerCharacter b)
        {
            SquadMember first = a != null ? a.GetComponent<SquadMember>() : null;
            return first != null && b != null && first.IsSquadmate(b.GetComponent<SquadMember>());
        }

        /// <summary>Server: squad of the player who caused a hit — their character's, else their lobby squad (grenade thrown before dying).</summary>
        private static int ServerSquadOfAttacker(int clientId)
        {
            if (clientId == DamageInfo.NoInstigator) return SquadRules.NoSquad;
            if (ServerByClient.TryGetValue(clientId, out SquadMember member) && member != null) return member._squadId.Value;
            return PlayerSquad.ServerSquadIdOf(clientId);
        }

        public DamageInfo ModifyIncomingDamage(in DamageInfo damage)
        {
            if (damage.InstigatorClientId == DamageInfo.NoInstigator || _squadId.Value == SquadRules.NoSquad) return damage;

            bool friendlyFire = SquadLobby.Instance != null && SquadLobby.Instance.FriendlyFire;
            bool self = damage.InstigatorClientId == _serverClientId;
            return SquadRules.AllowsDamage(ServerSquadOfAttacker(damage.InstigatorClientId), _squadId.Value, self, friendlyFire)
                ? damage
                : damage.WithAmount(0);
        }

        private void OnSquadChanged(int previous, int current, bool asServer)
        {
            if (!asServer) AnyChanged?.Invoke(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ServerByClient.Clear();
            ClientAll.Clear();
            AnyChanged = null;
        }
    }
}
