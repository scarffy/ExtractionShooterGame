using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace ExtractionShooter.Networking
{
    /// <summary>
    /// Server only: spawns a networked loot container at each child point when the server starts.
    /// Containers are spawned at runtime (not placed as scene objects) so every raid gets fresh loot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldLootSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        [Tooltip("Networked prefab with an ItemContainer and LootSpawner.")]
        [SerializeField] private NetworkObject _containerPrefab;

        [Tooltip("Spawn points. Uses this object's children if empty.")]
        [SerializeField] private Transform[] _spawnPoints;

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
                _hasSpawned = false; // the server despawns everything on stop
                return;
            }

            if (args.ConnectionState != LocalConnectionState.Started || _hasSpawned || _containerPrefab == null) return;

            _hasSpawned = true;
            foreach (Transform point in _spawnPoints)
            {
                if (point == null) continue;
                NetworkObject container = Instantiate(_containerPrefab, point.position, point.rotation);
                _networkManager.ServerManager.Spawn(container);
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
