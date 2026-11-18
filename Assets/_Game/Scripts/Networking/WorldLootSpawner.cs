using System.Collections.Generic;
using ExtractionShooter.Core.Matchmaking;
using ExtractionShooter.Core.Raid;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Raid;
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
    /// instance replaces them (their remaining contents go with them), with extra or fewer rolls for its difficulty.
    /// A point with a <see cref="LootPoint"/> gets that point's loot table and container name (high-tier crates,
    /// medical boxes); other points get the prefab's defaults.
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
        private int _rollBonus;

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

        /// <summary>A new raid instance: replace every container with one freshly filled for its difficulty.</summary>
        private void HandleRaidOpened(RaidInstance raid)
        {
            if (!_hasSpawned || raid == null) return;

            foreach (NetworkObject container in _containers)
                if (container != null && container.IsSpawned) _networkManager.ServerManager.Despawn(container);
            _rollBonus = _lobby != null ? _lobby.ModifiersFor(raid.Difficulty).LootRollBonus : 0;
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
                ApplyPointSettings(point, container);
                LootSpawner loot = container.GetComponent<LootSpawner>();
                if (loot != null) loot.ServerSetRollBonus(_rollBonus);
                _networkManager.ServerManager.Spawn(container);
                _containers.Add(container);
            }
        }

        /// <summary>Before spawning: the point's loot table and name, if it has a <see cref="LootPoint"/>.</summary>
        private static void ApplyPointSettings(Transform point, NetworkObject container)
        {
            LootPoint settings = point.GetComponent<LootPoint>();
            if (settings == null) return;

            if (settings.LootTable != null)
            {
                LootSpawner spawner = container.GetComponent<LootSpawner>();
                if (spawner != null) spawner.ServerOverrideLootTable(settings.LootTable);
            }

            ItemContainer inventory = container.GetComponent<ItemContainer>();
            if (inventory != null && !string.IsNullOrEmpty(settings.DisplayName))
            {
                inventory.ServerConfigure(inventory.Capacity, settings.DisplayName, inventory.GridWidth);
                container.name = settings.DisplayName;
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
