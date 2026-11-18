using System.Collections.Generic;
using ExtractionShooter.Core.Matchmaking;
using ExtractionShooter.Gameplay.Interaction;
using ExtractionShooter.Gameplay.Raid;
using ExtractionShooter.Gameplay.Session;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace ExtractionShooter.Networking
{
    /// <summary>
    /// Server only: spawns a networked <see cref="LockedDoor"/> at each child <see cref="DoorPoint"/> when the server
    /// starts (fitted to the point's doorway, with its lock), and locks them all again when a new raid instance opens,
    /// the same moment the world loot is replaced. Doors are spawned at runtime like the loot crates, so the scene holds
    /// no networked objects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LockedDoorSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        [Tooltip("Networked door prefab.")]
        [SerializeField] private LockedDoor _doorPrefab;

        [Tooltip("Door points. Uses this object's children if empty.")]
        [SerializeField] private DoorPoint[] _doorPoints;

        [Tooltip("Optional: locks every door again when a new raid instance launches.")]
        [SerializeField] private SquadLobby _lobby;

        private readonly List<LockedDoor> _doors = new List<LockedDoor>();
        private bool _hasSpawned;

        private void Awake()
        {
            if (_doorPoints == null || _doorPoints.Length == 0) _doorPoints = GetComponentsInChildren<DoorPoint>();

            if (_networkManager == null)
                Debug.LogError($"[{nameof(LockedDoorSpawner)}] No NetworkManager assigned.", this);
            if (_doorPrefab == null)
                Debug.LogError($"[{nameof(LockedDoorSpawner)}] Door prefab is not assigned.", this);
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

        /// <summary>A new raid instance after the first: every door is closed and locked again.</summary>
        private void HandleRaidOpened(RaidInstance raid)
        {
            if (!_hasSpawned || raid == null || raid.Id <= 1) return;
            foreach (LockedDoor door in _doors)
                if (door != null && door.IsSpawned) door.ServerReset();
        }

        private void HandleServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                _hasSpawned = false; // the server despawns everything on stop
                _doors.Clear();
                return;
            }

            if (args.ConnectionState != LocalConnectionState.Started || _hasSpawned || _doorPrefab == null) return;

            _hasSpawned = true;
            SpawnAll();
        }

        private void SpawnAll()
        {
            _doors.Clear();
            foreach (DoorPoint point in _doorPoints)
            {
                if (point == null) continue;
                LockedDoor door = Instantiate(_doorPrefab, point.transform.position, point.transform.rotation);
                door.name = point.Settings.DisplayName;
                door.ServerConfigure(point.Settings);
                _networkManager.ServerManager.Spawn(door.NetworkObject);
                _doors.Add(door);
            }
        }
    }
}
