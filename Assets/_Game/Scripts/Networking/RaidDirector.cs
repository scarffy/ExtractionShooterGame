using System.Collections;
using System.Collections.Generic;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Spawning;
using ExtractionShooter.Gameplay.AI;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Raid;
using ExtractionShooter.Gameplay.Session;
using FishNet.Connection;
using FishNet.Managing;
using UnityEngine;

namespace ExtractionShooter.Networking
{
    /// <summary>
    /// Server-side raid loop. Gives every client a <see cref="PlayerSession"/> once it has loaded the
    /// scene; when a session deploys, spawns a player character at a spawn point and hands it the loadout.
    /// The raid ends on extraction (gear returns to the loadout) or death (gear stays on the corpse);
    /// the character is then removed and the player is back in the hideout.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaidDirector : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private PlayerSession _sessionPrefab;
        [SerializeField] private PlayerCharacter _playerPrefab;

        [Tooltip("Component implementing ISpawnPointProvider.")]
        [SerializeField] private MonoBehaviour _spawnPointSource;

        [Tooltip("Seconds a dead player watches before returning to the hideout.")]
        [SerializeField, Min(0f)] private float _deathDelay = 4f;

        private readonly Dictionary<PlayerCharacter, PlayerSession> _raids = new Dictionary<PlayerCharacter, PlayerSession>();
        private readonly HashSet<PlayerCharacter> _ending = new HashSet<PlayerCharacter>();
        private readonly List<Vector3> _threats = new List<Vector3>();
        private ISpawnPointProvider _spawnPoints;

        private void Awake()
        {
            if (_networkManager == null) _networkManager = GetComponent<NetworkManager>();
            _spawnPoints = _spawnPointSource as ISpawnPointProvider;

            if (_networkManager == null) Debug.LogError($"[{nameof(RaidDirector)}] No NetworkManager assigned.", this);
            if (_sessionPrefab == null) Debug.LogError($"[{nameof(RaidDirector)}] Session prefab is not assigned.", this);
            if (_playerPrefab == null) Debug.LogError($"[{nameof(RaidDirector)}] Player prefab is not assigned.", this);
            if (_spawnPoints == null)
                Debug.LogError($"[{nameof(RaidDirector)}] Spawn point source must implement {nameof(ISpawnPointProvider)}.", this);
        }

        private void OnValidate()
        {
            if (_spawnPointSource != null && _spawnPointSource is not ISpawnPointProvider)
            {
                Debug.LogWarning($"[{nameof(RaidDirector)}] {_spawnPointSource.name} does not implement {nameof(ISpawnPointProvider)}.", this);
                _spawnPointSource = null;
            }
        }

        // Start, not OnEnable: the NetworkManager initialises its managers in Awake.
        private void Start()
        {
            if (_networkManager != null)
                _networkManager.SceneManager.OnClientLoadedStartScenes += HandleClientLoadedStartScenes;
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _networkManager.SceneManager != null)
                _networkManager.SceneManager.OnClientLoadedStartScenes -= HandleClientLoadedStartScenes;

            foreach (PlayerCharacter character in new List<PlayerCharacter>(_raids.Keys))
                Unsubscribe(character);
            _raids.Clear();
        }

        private void HandleClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (!asServer || _sessionPrefab == null) return;

            PlayerSession session = Instantiate(_sessionPrefab);
            session.name = $"Session {connection.ClientId}";
            _networkManager.ServerManager.Spawn(session.NetworkObject, connection);
            session.ServerDeployRequested += Deploy;
        }

        private void Deploy(PlayerSession session)
        {
            if (session == null || !session.IsSpawned || _playerPrefab == null || _spawnPoints == null) return;
            ForgetDespawned();
            if (_raids.ContainsValue(session)) return;

            Pose pose = _spawnPoints.GetSafestSpawnPose(CollectThreats());
            PlayerCharacter character = Instantiate(_playerPrefab, pose.position, pose.rotation);
            _networkManager.ServerManager.Spawn(character.NetworkObject, session.Owner);

            _raids[character] = session;
            character.Health.ServerDied += OnCharacterDied;
            character.Raid.ServerExtracted += OnCharacterExtracted;

            session.ServerBeginRaid(character);
            Debug.Log($"[{nameof(RaidDirector)}] {session.PlayerName} deployed at {pose.position}.");
        }

        /// <summary>Positions of living players and bots, to keep new arrivals away from them.</summary>
        private List<Vector3> CollectThreats()
        {
            _threats.Clear();
            foreach (PlayerCharacter player in PlayerCharacter.ServerActive)
                if (player != null && player.Health.IsAlive) _threats.Add(player.transform.position);
            foreach (BotBrain bot in BotBrain.ServerActive)
                if (bot != null && bot.IsAlive) _threats.Add(bot.transform.position);
            return _threats;
        }

        private void OnCharacterExtracted(PlayerRaid raid)
        {
            PlayerCharacter character = raid.GetComponent<PlayerCharacter>();
            EndRaid(character, extracted: true);
        }

        private void OnCharacterDied(DamageInfo cause)
        {
            // The event does not say which character died: find dead ones still in a raid.
            foreach (PlayerCharacter character in new List<PlayerCharacter>(_raids.Keys))
            {
                if (character != null && !character.Health.IsAlive && _ending.Add(character))
                {
                    string killer = cause.InstigatorClientId == DamageInfo.NoInstigator ? "a bot or the world" : $"client {cause.InstigatorClientId}";
                    Debug.Log($"[{nameof(RaidDirector)}] {_raids[character].PlayerName} was killed by {killer}.");
                    Unsubscribe(character);
                    StartCoroutine(EndRaidAfterDelay(character));
                }
            }
        }

        private IEnumerator EndRaidAfterDelay(PlayerCharacter character)
        {
            yield return new WaitForSeconds(_deathDelay);
            EndRaid(character, extracted: false);
        }

        private void EndRaid(PlayerCharacter character, bool extracted)
        {
            if (character == null || !_raids.TryGetValue(character, out PlayerSession session)) return;

            Unsubscribe(character);
            _raids.Remove(character);
            _ending.Remove(character);

            if (extracted && session != null) Debug.Log($"[{nameof(RaidDirector)}] {session.PlayerName} extracted.");

            // Gear moves first: despawning the character destroys whatever it still carries.
            if (session != null && session.IsSpawned) session.ServerEndRaid(extracted);
            if (character.IsSpawned) _networkManager.ServerManager.Despawn(character.NetworkObject);
        }

        /// <summary>Drops raids whose character was despawned without ending (the owner disconnected).</summary>
        private void ForgetDespawned()
        {
            foreach (PlayerCharacter character in new List<PlayerCharacter>(_raids.Keys))
            {
                if (character != null && character.IsSpawned) continue;
                Unsubscribe(character);
                _raids.Remove(character);
                _ending.Remove(character);
            }
        }

        private void Unsubscribe(PlayerCharacter character)
        {
            if (character == null) return;
            character.Health.ServerDied -= OnCharacterDied;
            if (character.Raid != null) character.Raid.ServerExtracted -= OnCharacterExtracted;
        }
    }
}
