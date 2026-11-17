using System.Collections.Generic;
using ExtractionShooter.Core.Matchmaking;
using ExtractionShooter.Gameplay.Session;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace ExtractionShooter.Networking
{
    /// <summary>
    /// Server only: spawns a networked loot container at each child point when the server starts.
    /// Containers are spawned at runtime (not placed as scene objects) so every raid gets fresh loot: each raid
    /// instance after the first replaces them (their remaining contents go with them).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldLootSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        [Tooltip("Networked prefab with an ItemContainer and LootSpawner.")]
        [SerializeField] private NetworkObject _containerPrefab;

        [Tooltip("Spawn points. Uses this object's children if empty.")]
        [SerializeField] private Transform[] _spawnPoints;

        [Tooltip("Optional: respawns every container when a new raid instance launches.")]
        [SerializeField] private SquadLobby _lobby;

        private readonly List<NetworkObject> _containers = new List<NetworkObject>();
        private bool _hasSpawned;

        private void Awake()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                _spawnPoints = new Transform[transform.childCount];
                for (int i = 0; i < transform.childCount; i++)
                    _spawnPoints[i] = transform.GetChild(i);
            }

            if (_networkManager == null)
                Debug.LogError($"[{nameof(WorldLootSpawner)}] No NetworkManager assigned.", this);
            if (_containerPrefab == null)
                Debug.LogError($"[{nameof(WorldLootSpawner)}] Container prefab is not assigned.", this);
        }

        // Start, not OnEnable: the NetworkManager initialises its managers in Awake.
        private void Start()
        {
            if (_networkManager == null) return;
            _networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
            if (_lobby != null) _lobby.ServerRaidOpened += HandleRaidOpened;
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
            if (_lobby != null) _lobby.ServerRaidOpened -= HandleRaidOpened;
        }

        /// <summary>A new raid instance after the first: replace every container with a freshly filled one.</summary>
        private void HandleRaidOpened(RaidInstance raid)
        {
            if (!_hasSpawned || raid == null || raid.Id <= 1) return;

            foreach (NetworkObject container in _containers)
                if (container != null && container.IsSpawned) _networkManager.ServerManager.Despawn(container);
            SpawnAll();
        }

        private void HandleServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                _hasSpawned = false; // the server despawns everything on stop
                _containers.Clear();
                return;
            }

            if (args.ConnectionState != LocalConnectionState.Started || _hasSpawned || _containerPrefab == null) return;

            _hasSpawned = true;
            SpawnAll();
        }

        private void SpawnAll()
        {
            _containers.Clear();
            foreach (Transform point in _spawnPoints)
            {
                if (point == null) continue;
                NetworkObject container = Instantiate(_containerPrefab, point.position, point.rotation);
                _networkManager.ServerManager.Spawn(container);
                _containers.Add(container);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f);
            foreach (Transform child in transform)
                Gizmos.DrawWireCube(child.position + Vector3.up * 0.4f, new Vector3(1.2f, 0.8f, 0.8f));
        }
#endif
    }
}
