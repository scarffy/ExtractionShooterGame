using System.Collections.Generic;
using ExtractionShooter.Gameplay.AI;
using ExtractionShooter.Gameplay.Player;
using FishNet.Managing;
using FishNet.Transporting;
using Unity.AI.Navigation;
using UnityEngine;

namespace ExtractionShooter.Networking
{
    /// <summary>
    /// Server only: builds the NavMesh when the server starts, then keeps up to <see cref="_maxBots"/> bots
    /// alive. A dead bot is replaced after <see cref="_respawnSeconds"/>, at a spawn point no player is near.
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

        [SerializeField, Min(0)] private int _maxBots = 6;
        [SerializeField, Min(0f)] private float _respawnSeconds = 45f;

        [Tooltip("A bot never appears closer than this to a living player.")]
        [SerializeField, Min(0f)] private float _minPlayerDistance = 20f;

        private readonly List<BotBrain> _bots = new List<BotBrain>();
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
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
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
            for (int i = 0; i < _maxBots; i++) TrySpawnBot();
            _nextSpawnAt = Time.time + _respawnSeconds;
        }

        private void Update()
        {
            if (!_running) return;

            _bots.RemoveAll(bot => bot == null || !bot.IsSpawned);
            if (_bots.Count >= _maxBots)
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
