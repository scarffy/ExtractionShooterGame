using System.Collections;
using System.Collections.Generic;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Matchmaking;
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
    /// scene. When the <see cref="SquadLobby"/> sends a squad into a raid instance, spawns its members together at the
    /// spawn point farthest from everyone else (other squads, bots), tags them with their squad, gives them the
    /// instance's end time and hands them their loadouts. A player's raid ends on extraction (gear returns to the
    /// loadout) or death (gear stays on the corpse); the character is then removed and the player is back in the hideout.
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

        [Tooltip("Squads and matchmaking: raises the deploy requests.")]
        [SerializeField] private SquadLobby _lobby;

        [Tooltip("Metres between squadmates spawning at the same point.")]
        [SerializeField, Min(0.5f)] private float _squadSpacing = 1.5f;

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
            if (_lobby == null) Debug.LogError($"[{nameof(RaidDirector)}] Squad lobby is not assigned: nobody can deploy.", this);
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
            if (_lobby != null) _lobby.ServerSquadDeploying += DeploySquad;
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _networkManager.SceneManager != null)
                _networkManager.SceneManager.OnClientLoadedStartScenes -= HandleClientLoadedStartScenes;
            if (_lobby != null) _lobby.ServerSquadDeploying -= DeploySquad;

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
        }

        /// <summary>
        /// One squad into the raid: the spawn point farthest from every living player and bot (squads deployed
        /// earlier this tick are already standing at theirs), members side by side around it.
        /// </summary>
        private void DeploySquad(RaidInstance raid, int squadId, IReadOnlyList<PlayerSession> members)
        {
            if (raid == null || members == null || members.Count == 0 || _playerPrefab == null || _spawnPoints == null) return;
            ForgetDespawned();

            Pose point = _spawnPoints.GetSafestSpawnPose(CollectThreats());
            int slot = 0;
            foreach (PlayerSession session in members)
            {
                Vector3 offset = point.rotation * SpawnSelection.SquadOffset(slot, _squadSpacing);
                if (Deploy(session, new Pose(point.position + offset, point.rotation), raid, squadId)) slot++;
            }
        }

        private bool Deploy(PlayerSession session, Pose pose, RaidInstance raid, int squadId)
        {
            if (session == null || !session.IsSpawned || session.State != SessionState.Hideout || _raids.ContainsValue(session)) return false;

            PlayerCharacter character = Instantiate(_playerPrefab, pose.position, pose.rotation);
            _networkManager.ServerManager.Spawn(character.NetworkObject, session.Owner);

            _raids[character] = session;
            character.Health.ServerDied += OnCharacterDied;
            character.Raid.ServerExtracted += OnCharacterExtracted;
            character.Raid.ServerSetRaidEnd(raid.EndTime);
            SquadMember squad = character.GetComponent<SquadMember>();
            if (squad != null) squad.ServerAssign(squadId, session.PlayerName);

            session.ServerBeginRaid(character);
            Debug.Log($"[{nameof(RaidDirector)}] {session.PlayerName} deployed into raid #{raid.Id} (squad {squadId}) at {pose.position}.");
            return true;
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
