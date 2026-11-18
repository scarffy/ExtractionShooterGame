using ExtractionShooter.Core.Squad;
using ExtractionShooter.Gameplay.AI;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Interaction;
using ExtractionShooter.Gameplay.Player;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>
    /// Squad pings for a player (middle mouse / Z; a quick double press marks danger). The owner sends its eye and aim;
    /// the server checks the player is alive and within the rate limit (<see cref="PingLimiter"/>), casts the ray
    /// itself (up to <see cref="PingRules.MaxDistance"/>), decides the kind from what it hit — an enemy player or bot,
    /// a lootable container, else a location; squadmates are seen through — and keeps at most
    /// <see cref="PingRules.MaxActivePerPlayer"/> per player. The ping then goes by target RPC to the pinger and their
    /// squadmates only, each on their own character, so enemies never learn of it. Received pings land on
    /// <see cref="SquadPings"/> for the HUD.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter), typeof(SquadMember))]
    public sealed class PlayerPings : NetworkBehaviour
    {
        private const float MaxOriginError = 1.5f;
        private const int HitBufferSize = 16;

        private static int _serverNextId;

        [Tooltip("Furthest a ping reaches from the eyes (metres).")]
        [SerializeField, Min(5f)] private float _maxDistance = PingRules.MaxDistance;

        [Tooltip("Seconds a ping stays up.")]
        [SerializeField, Min(1f)] private float _lifetime = PingRules.Lifetime;

        [Tooltip("Layers a ping ray stops on. Leave out Ignore Raycast (movement blockers).")]
        [SerializeField] private LayerMask _pingMask = ~(1 << 2);

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HitBufferSize];
        private readonly PingPressTracker _presses = new PingPressTracker();
        private readonly PingLimiter _serverLimiter = new PingLimiter();
        private readonly PingBoard _serverBoard = new PingBoard();

        private PlayerCharacter _character;
        private SquadMember _squad;
        private int _serverLastId = -1;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _squad = GetComponent<SquadMember>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner) SquadPings.ClientClear(); // a new raid: nothing left over from the last one
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner) SquadPings.ClientClear();
        }

        private void Update()
        {
            if (!IsOwner || !_character.Health.IsAlive || !_character.Input.PingPressed) return;
            if (!_presses.TryPress(Time.timeAsDouble, out bool danger)) return;
            ServerPing(_character.Look.EyePosition, _character.Look.AimDirection, danger);
        }

        // ---------------- Server ----------------

        [ServerRpc]
        private void ServerPing(Vector3 origin, Vector3 direction, bool danger)
        {
            if (!_character.Health.IsAlive || !IsFinite(direction) || direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();

            Vector3 eye = _character.Look.EyePosition;
            if (!IsFinite(origin) || Vector3.Distance(origin, eye) > MaxOriginError) origin = eye;

            bool hit = ServerTrace(origin, direction, out Vector3 point, out PingKind kind);
            if (!hit && !danger) return; // the sky: nothing to mark, and no rate limit spent

            double now = Time.timeAsDouble;
            _serverBoard.Prune(now);
            PingDecision decision = _serverLimiter.Evaluate(now, danger);
            SquadPing last = default;
            if (decision == PingDecision.Upgrade && !_serverBoard.TryGet(_serverLastId, out last))
            {
                // The ping to upgrade has gone: place a fresh danger ping instead.
                _serverLimiter.Forget();
                decision = hit ? _serverLimiter.Evaluate(now, danger) : PingDecision.Reject;
            }
            if (decision == PingDecision.Reject) return;
            if (decision == PingDecision.Place && !hit)
            {
                _serverLimiter.Forget();
                return;
            }

            Vector3 at = hit ? point : SquadPings.PositionOf(last);
            int id = decision == PingDecision.Upgrade ? last.Id : ++_serverNextId;
            PingKind placed = danger ? PingKind.Danger : kind;
            var ping = new SquadPing(id, OwnerId, ServerPlayerName(), placed, at.x, at.y, at.z, now, now + _lifetime);
            _serverBoard.Upsert(ping);
            _serverLastId = id;
            ServerSendToSquad(ping);
        }

        /// <summary>Server: the nearest thing the ray stops on, and what kind of ping it makes.</summary>
        private bool ServerTrace(Vector3 origin, Vector3 direction, out Vector3 point, out PingKind kind)
        {
            point = default;
            kind = PingKind.Location;
            bool found = false;
            float nearest = float.MaxValue;

            int count = Physics.RaycastNonAlloc(origin, direction, _hitBuffer, _maxDistance, _pingMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hitBuffer[i];
                if (hit.collider == null || hit.distance >= nearest || hit.collider.transform.IsChildOf(transform)) continue;
                if (!TryClassify(hit.collider, out PingKind hitKind)) continue;

                nearest = hit.distance;
                point = hit.point;
                kind = hitKind;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Server: the ping kind for a collider, or false to look past it — squadmates, and triggers that are not part
        /// of a character or lootable (extraction zones, smoke).
        /// </summary>
        private bool TryClassify(Collider collider, out PingKind kind)
        {
            kind = PingKind.Location;

            PlayerCharacter player = collider.GetComponentInParent<PlayerCharacter>();
            if (player != null)
            {
                if (player == _character || SquadMember.AreSquadmates(_character, player)) return false;
                if (player.Health.IsAlive) kind = PingKind.Enemy;
                return true;
            }

            BotBrain bot = collider.GetComponentInParent<BotBrain>();
            if (bot != null)
            {
                Health health = bot.GetComponent<Health>();
                if (health != null && health.IsAlive) kind = PingKind.Enemy;
                return true;
            }

            if (collider.GetComponentInParent<LootableContainer>() != null)
            {
                kind = PingKind.Loot;
                return true;
            }

            return !collider.isTrigger;
        }

        /// <summary>Server: sends the ping to the pinger and each squadmate, on their own character (its owner always observes it).</summary>
        private void ServerSendToSquad(in SquadPing ping)
        {
            Vector3 position = SquadPings.PositionOf(ping);
            foreach (PlayerCharacter character in PlayerCharacter.ServerActive)
            {
                if (character == null || (character != _character && !SquadMember.AreSquadmates(_character, character))) continue;
                PlayerPings receiver = character.GetComponent<PlayerPings>();
                NetworkConnection connection = character.Owner;
                if (receiver == null || connection == null || !connection.IsValid) continue;
                receiver.TargetReceive(connection, ping.Id, ping.OwnerId, ping.OwnerName, (byte)ping.Kind, position, _lifetime);
            }
        }

        private string ServerPlayerName() =>
            _squad != null && !string.IsNullOrEmpty(_squad.PlayerName) ? _squad.PlayerName : "Teammate";

        // ---------------- Client ----------------

        [TargetRpc]
        private void TargetReceive(NetworkConnection connection, int id, int ownerId, string ownerName, byte kind, Vector3 position, float lifetime)
        {
            if (!IsFinite(position)) return;
            double now = Time.timeAsDouble;
            float seconds = Mathf.Clamp(lifetime, 0.5f, 60f);
            SquadPings.ClientReceive(new SquadPing(id, ownerId, ownerName, PingRules.FromByte(kind),
                position.x, position.y, position.z, now, now + seconds));
        }

        private static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _serverNextId = 0;
    }
}
