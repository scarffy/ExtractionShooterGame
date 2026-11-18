using System;
using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Threading;
using ExtractionShooter.Core.Hosting;
using ExtractionShooter.Core.Matchmaking;
using ExtractionShooter.Gameplay.Session;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace ExtractionShooter.Networking
{
    /// <summary>
    /// Lists this server on the master server (Docs/DedicatedServer.md) so players find it in the server browser.
    /// Server only, and only for dedicated servers: a headless build, or (editor / development builds) a server
    /// started with the main menu's "Dedicated server (dev)" button. A host's own game is never listed.
    /// <list type="bullet">
    /// <item>While the server runs it POSTs a heartbeat every <c>heartbeatSeconds</c> (<see cref="ServerConfig"/>):
    /// name, address override, port, region, map (active scene), the map's difficulties, signed-in players, raid size,
    /// the running raid's difficulty and time left, and the game version (Player Settings → Version).</item>
    /// <item>Failures back off (interval doubling up to <see cref="_maxBackoffSeconds"/>) and are logged once per
    /// failure streak; a rejected token is reported clearly and retried at the ceiling.</item>
    /// <item>When the server stops or the application quits, the entry is removed (best effort: the master also
    /// forgets a silent server after 30 s).</item>
    /// </list>
    /// The master address and token come from <see cref="ServerBootstrap.Config"/>; a dev server without one uses the
    /// address saved by the server browser. Each process announces under a new id (GUID).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MasterServerAnnouncer : MonoBehaviour
    {
        private const string JsonContentType = "application/json";

        [SerializeField] private NetworkManager _networkManager;

        [Tooltip("Holds the server config (master address, token, name, region). Found on this object when empty.")]
        [SerializeField] private ServerBootstrap _bootstrap;

        [Tooltip("Source of the raid size, difficulties and running raid. Found on this object when empty.")]
        [SerializeField] private SquadLobby _lobby;

        [Tooltip("Editor / development builds: also list a server started with 'Dedicated server (dev)'. " +
                 "Uses the server browser's master address when server.cfg names none.")]
        [SerializeField] private bool _announceDevServers = true;

        [Tooltip("Seconds before a heartbeat or removal request gives up.")]
        [SerializeField, Range(1, 30)] private int _requestTimeoutSeconds = 5;

        [Tooltip("Longest wait between heartbeats while the master server cannot be reached.")]
        [SerializeField, Range(10f, 300f)] private float _maxBackoffSeconds = 60f;

        [Tooltip("Wait after the server starts before the first heartbeat, so a host's local client has connected " +
                 "(hosts are never listed).")]
        [SerializeField, Range(0f, 5f)] private float _startDelaySeconds = 1f;

        [Tooltip("On quit, how long to hold the process so the removal request can leave.")]
        [SerializeField, Range(0f, 2f)] private float _quitWaitSeconds = 0.5f;

        private string _serverId;
        private int _generation;
        private bool _subscribed;
        private bool _listed;
        private string _listedMasterUrl;
        private string _listedToken;
        private UnityWebRequest _pendingRemoval;

        /// <summary>This process's id on the master server.</summary>
        public string ServerId => _serverId;

        /// <summary>True after a heartbeat was accepted, until the entry is removed.</summary>
        public bool IsListed => _listed;

        private static bool DevServersAllowed
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        private void Awake()
        {
            if (_networkManager == null) _networkManager = GetComponent<NetworkManager>();
            if (_bootstrap == null) _bootstrap = GetComponent<ServerBootstrap>();
            if (_lobby == null) _lobby = GetComponent<SquadLobby>();
            _serverId = MasterServerApi.NewServerId();
        }

        // Start, not OnEnable: the NetworkManager creates its ServerManager in its own Awake.
        private void Start()
        {
            if (_networkManager == null || _networkManager.ServerManager == null) return;
            _networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
            _subscribed = true;
            // ServerBootstrap.Start may have started the server before this Start ran.
            if (_networkManager.ServerManager.Started) Begin();
        }

        private void OnDestroy()
        {
            if (_subscribed && _networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
            _generation++;
        }

        private void OnApplicationQuit()
        {
            _generation++;
            SendRemoval();
            // The process ends right after this: give the request a moment to leave.
            UnityWebRequest pending = _pendingRemoval;
            if (pending == null || _quitWaitSeconds <= 0f) return;
            var clock = Stopwatch.StartNew();
            while (_pendingRemoval == pending && !pending.isDone && clock.Elapsed.TotalSeconds < _quitWaitSeconds) Thread.Sleep(10);
        }

        private void HandleServerConnectionState(ServerConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    Begin();
                    break;
                case LocalConnectionState.Stopped:
                    _generation++; // ends the heartbeat loop
                    SendRemoval();
                    break;
            }
        }

        private void Begin()
        {
            int generation = ++_generation;
            StartCoroutine(HeartbeatLoop(generation));
        }

        // ---------------- Heartbeats ----------------

        private IEnumerator HeartbeatLoop(int generation)
        {
            if (_startDelaySeconds > 0f) yield return new WaitForSeconds(_startDelaySeconds);
            if (generation != _generation || !_networkManager.ServerManager.Started) yield break;

            ServerConfig config = _bootstrap != null ? _bootstrap.Config : null;
            if (!TryGetTarget(config, out string masterUrl, out string token)) yield break;

            float interval = config != null ? config.HeartbeatSeconds : ServerConfig.DefaultHeartbeatSeconds;
            Debug.Log($"[{nameof(MasterServerAnnouncer)}] Listing this server on {masterUrl} every {interval:0.#} s (id {_serverId}).", this);

            int failures = 0;
            bool tokenRejected = false;
            while (generation == _generation && _networkManager.ServerManager.Started)
            {
                // A local client joined (this is a host now): hosts are not listed.
                if (_networkManager.ClientManager.Started)
                {
                    SendRemoval();
                    yield break;
                }

                string json = JsonUtility.ToJson(MasterServerApi.BuildHeartbeat(_serverId, CurrentStatus(config)), false);
                bool accepted;
                string failure;
                long code;
                using (UnityWebRequest request = CreateRequest(MasterServerApi.HeartbeatUrl(masterUrl), UnityWebRequest.kHttpVerbPOST, json, token))
                {
                    UnityWebRequestAsyncOperation operation = TrySend(request, out failure);
                    if (operation != null) yield return operation;
                    code = operation != null ? request.responseCode : 0;
                    accepted = operation != null && request.result == UnityWebRequest.Result.Success;
                    if (operation != null && !accepted) failure = DescribeFailure(request);
                }

                if (accepted)
                {
                    _listed = true;
                    _listedMasterUrl = masterUrl;
                    _listedToken = token;
                    if (failures > 0)
                        Debug.Log($"[{nameof(MasterServerAnnouncer)}] Master server reachable again after {failures} failed heartbeat(s).", this);
                    failures = 0;
                    tokenRejected = false;
                }
                else
                {
                    failures++;
                    bool unauthorized = code == 401;
                    if (unauthorized && !tokenRejected)
                        Debug.LogError($"[{nameof(MasterServerAnnouncer)}] The master server rejected the token (401). Check 'token' in " +
                                       "server.cfg against the master's SERVER_TOKEN. Retrying slowly.", this);
                    else if (failures == 1 && !unauthorized)
                        Debug.LogWarning($"[{nameof(MasterServerAnnouncer)}] Heartbeat to {masterUrl} failed: {failure}. Retrying with backoff.", this);
                    tokenRejected |= unauthorized;
                }

                // Stopped while the request was out: the stop handler sent the removal before this heartbeat landed.
                if (generation != _generation)
                {
                    if (accepted) SendRemoval();
                    yield break;
                }

                float delay = tokenRejected
                    ? HeartbeatSchedule.CeilingSeconds(interval, _maxBackoffSeconds)
                    : HeartbeatSchedule.NextDelaySeconds(interval, failures, _maxBackoffSeconds);
                yield return new WaitForSeconds(delay);
            }
        }

        /// <summary>Whether this server is listed, and where. False for hosts, release non-headless players and when no master is set.</summary>
        private bool TryGetTarget(ServerConfig config, out string masterUrl, out string token)
        {
            masterUrl = config != null ? config.MasterUrl : string.Empty;
            token = config != null ? config.Token : string.Empty;
            if (_networkManager.ClientManager.Started) return false; // a host's own game

            if (!ServerBootstrap.IsHeadless)
            {
                if (!_announceDevServers || !DevServersAllowed) return false;
                if (string.IsNullOrEmpty(masterUrl)
                    && MasterServerApi.TryNormalizeBaseUrl(PlayerPrefs.GetString(MasterServerApi.MasterUrlPrefsKey, string.Empty), out string saved))
                    masterUrl = saved;
            }

            if (!string.IsNullOrEmpty(masterUrl)) return true;
            if (ServerBootstrap.IsHeadless)
                Debug.Log($"[{nameof(MasterServerAnnouncer)}] No master server configured ('master' in server.cfg or -master): " +
                          "this server is not listed in the server browser; players join it by address.", this);
            return false;
        }

        private ServerStatus CurrentStatus(ServerConfig config)
        {
            RaidInstance raid = _lobby != null ? _lobby.ActiveRaid : null;
            double now = Time.timeAsDouble;
            bool raidRunning = raid != null && now < raid.EndTime;
            int maxPlayers = _lobby != null ? _lobby.MaxPlayersPerRaid : config != null && config.MaxPlayers > 0 ? config.MaxPlayers : 1;
            Transport transport = _networkManager.TransportManager.Transport;

            return new ServerStatus
            {
                Name = config != null ? config.Name : ServerConfig.DefaultName,
                Host = config != null ? config.PublicHost : string.Empty,
                Port = transport != null ? transport.GetPort() : 0,
                Region = config != null ? config.Region : string.Empty,
                Map = SceneManager.GetActiveScene().name,
                Difficulties = _lobby != null ? _lobby.Difficulties : default,
                Players = PlayerSession.ServerSignedInCount,
                MaxPlayers = maxPlayers,
                RaidRunning = raidRunning,
                RaidDifficulty = raid != null ? raid.Difficulty : default,
                RaidSecondsLeft = raidRunning ? raid.EndTime - now : 0d,
                Version = Application.version,
            };
        }

        // ---------------- Removal ----------------

        /// <summary>Asks the master to drop this server now (fire and forget). Does nothing when not listed.</summary>
        private void SendRemoval()
        {
            if (!_listed) return;
            _listed = false;

            UnityWebRequest request = CreateRequest(MasterServerApi.ServerUrl(_listedMasterUrl, _serverId), UnityWebRequest.kHttpVerbDELETE,
                null, _listedToken);
            UnityWebRequestAsyncOperation operation = TrySend(request, out string failure);
            if (operation == null)
            {
                Debug.LogWarning($"[{nameof(MasterServerAnnouncer)}] Could not remove this server from the list: {failure}", this);
                request.Dispose();
                return;
            }

            _pendingRemoval = request;
            operation.completed += _ =>
            {
                if (_pendingRemoval == request) _pendingRemoval = null;
                request.Dispose();
            };
            Debug.Log($"[{nameof(MasterServerAnnouncer)}] Removing this server from the list.", this);
        }

        // ---------------- HTTP ----------------

        private UnityWebRequest CreateRequest(string url, string method, string json, string token)
        {
            UploadHandler upload = null;
            if (json != null)
                upload = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)) { contentType = JsonContentType };

            var request = new UnityWebRequest(url, method, new DownloadHandlerBuffer(), upload) { timeout = _requestTimeoutSeconds };
            if (json != null) request.SetRequestHeader("Content-Type", JsonContentType);
            if (!string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", "Bearer " + token);
            return request;
        }

        /// <summary>Sends the request; null with a reason when Unity refuses it (e.g. plain http:// not allowed in Player Settings).</summary>
        private static UnityWebRequestAsyncOperation TrySend(UnityWebRequest request, out string failure)
        {
            failure = null;
            try
            {
                return request.SendWebRequest();
            }
            catch (InvalidOperationException exception)
            {
                failure = exception.Message + " (for an http:// master set Player Settings → Other Settings → " +
                          "'Allow downloads over HTTP' to 'Always allowed', or use https://)";
                return null;
            }
            catch (ArgumentException exception)
            {
                failure = exception.Message;
                return null;
            }
        }

        private static string DescribeFailure(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.ProtocolError) return request.error ?? "no answer";

            string reason = null;
            string body = request.downloadHandler != null ? request.downloadHandler.text : null;
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    MasterServerReply reply = JsonUtility.FromJson<MasterServerReply>(body);
                    if (reply != null && !string.IsNullOrEmpty(reply.error)) reason = reply.error;
                }
                catch (ArgumentException)
                {
                    // Not JSON (a proxy's error page): the status code says enough.
                }
            }
            return $"HTTP {request.responseCode}" + (reason != null ? $" ({reason})" : string.Empty);
        }
    }
}
