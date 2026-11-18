using System.Collections.Generic;
using ExtractionShooter.Gameplay.AI;
using ExtractionShooter.Core.Matchmaking;
using ExtractionShooter.Core.Raid;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Session;
using FishNet.Managing;
using FishNet.Transporting;
using Unity.AI.Navigation;
using UnityEngine;

namespace ExtractionShooter.Networking
{
    /// <summary>
    /// Server only: builds the NavMesh when the server starts. Each raid instance starts with a fresh set of bots sized
    /// and sharpened for its difficulty (<see cref="SquadLobby.ModifiersFor"/>), and the set is kept full: a dead bot is
    /// replaced after <see cref="_respawnSeconds"/>, at a spawn point no player is near.
    /// Without a lobby, <see cref="_maxBots"/> Normal bots spawn when the server starts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private BotBrain _botPrefab;

        [Tooltip("Built at server start from the level's colliders.")]
        [SerializeField] private NavMeshSurface _navMesh;

        [Tooltip("Spawn points. Uses this object's children if empty.")]
        [SerializeField] private Transform[] _spawnPoints;

        [Tooltip("Bots alive at once on Normal; easier and harder raids scale this.")]
        [SerializeField, Min(0)] private int _maxBots = 6;
        [SerializeField, Min(0f)] private float _respawnSeconds = 45f;

        [Tooltip("A bot never appears closer than this to a living player.")]
        [SerializeField, Min(0f)] private float _minPlayerDistance = 20f;

        [Tooltip("Optional: replaces every bot when a new raid instance launches.")]
        [SerializeField] private SquadLobby _lobby;

        private readonly List<BotBrain> _bots = new List<BotBrain>();
        private DifficultyModifiers _difficulty = DifficultyModifiers.Neutral;
        private int _targetBots;
        private bool _running;
        private float _nextSpawnAt;
        private int _nextPoint;

        private void Awake()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                _spawnPoints = new Transform[transform.childCount];
                for (int i = 0; i < transform.childCount; i++)
                    _spawnPoints[i] = transform.GetChild(i);
            }

            if (_networkManager == null) Debug.LogError($"[{nameof(BotSpawner)}] No NetworkManager assigned.", this);
            if (_botPrefab == null) Debug.LogError($"[{nameof(BotSpawner)}] Bot prefab is not assigned.", this);
            if (_navMesh == null) Debug.LogError($"[{nameof(BotSpawner)}] NavMesh surface is not assigned.", this);
        }

        // Start, not OnEnable: the NetworkManager initialises its managers in Awake.
        private void Start()
        {
            if (_networkManager != null)
                _networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
            if (_lobby != null) _lobby.ServerRaidOpened += HandleRaidOpened;
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
            if (_lobby != null) _lobby.ServerRaidOpened -= HandleRaidOpened;
        }

        /// <summary>A new raid instance: despawn the previous instance's bots and spawn a full set for its difficulty.</summary>
        private void HandleRaidOpened(RaidInstance raid)
        {
            if (!_running || raid == null) return;

            foreach (BotBrain bot in _bots)
                if (bot != null && bot.IsSpawned) _networkManager.ServerManager.Despawn(bot.NetworkObject);
            _bots.Clear();

            _difficulty = _lobby != null ? _lobby.ModifiersFor(raid.Difficulty) : DifficultyModifiers.Neutral;
            _targetBots = _difficulty.BotCount(_maxBots);
            for (int i = 0; i < _targetBots; i++) TrySpawnBot();
            _nextSpawnAt = Time.time + _respawnSeconds;
            Debug.Log($"[{nameof(BotSpawner)}] Raid #{raid.Id} ({raid.Difficulty.DisplayName()}): {_bots.Count}/{_targetBots} bots.");
        }

        private void HandleServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                _running = false;
                _bots.Clear(); // the server despawns everything on stop
                return;
            }
            if (args.ConnectionState != LocalConnectionState.Started || _running) return;

            if (_navMesh != null) _navMesh.BuildNavMesh();
            _running = true;
            // With a lobby, bots arrive with each raid (their number and skill depend on its difficulty).
            _difficulty = DifficultyModifiers.Neutral;
            _targetBots = _lobby != null ? 0 : _maxBots;
            for (int i = 0; i < _targetBots; i++) TrySpawnBot();
            _nextSpawnAt = Time.time + _respawnSeconds;
        }

        private void Update()
        {
            if (!_running) return;

            _bots.RemoveAll(bot => bot == null || !bot.IsSpawned);
            if (_bots.Count >= _targetBots)
            {
                _nextSpawnAt = Time.time + _respawnSeconds;
                return;
            }

            if (Time.time >= _nextSpawnAt && TrySpawnBot())
                _nextSpawnAt = Time.time + _respawnSeconds;
        }

        private bool TrySpawnBot()
        {
            if (_botPrefab == null || _spawnPoints.Length == 0) return false;

            for (int attempt = 0; attempt < _spawnPoints.Length; attempt++)
            {
                Transform point = _spawnPoints[_nextPoint];
                _nextPoint = (_nextPoint + 1) % _spawnPoints.Length;
                if (point == null || IsNearPlayer(point.position)) continue;

                BotBrain bot = Instantiate(_botPrefab, point.position, point.rotation);
                bot.ServerApplyDifficulty(_difficulty);
                _networkManager.ServerManager.Spawn(bot.NetworkObject);
                _bots.Add(bot);
                return true;
            }
            return false;
        }

        private bool IsNearPlayer(Vector3 position)
        {
            float minSquared = _minPlayerDistance * _minPlayerDistance;
            foreach (PlayerCharacter player in PlayerCharacter.ServerActive)
            {
                if (player != null && player.Health.IsAlive && (player.transform.position - position).sqrMagnitude < minSquared)
                    return true;
            }
            return false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f);
            foreach (Transform child in transform)
                Gizmos.DrawWireSphere(child.position + Vector3.up, 0.5f);
        }
#endif
    }
}
