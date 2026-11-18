using System;
using System.Collections.Generic;
using System.IO;
using ExtractionShooter.Core.Hosting;
using ExtractionShooter.Gameplay.Session;
using FishNet.Managing;
using UnityEngine;

namespace ExtractionShooter.Networking
{
    /// <summary>
    /// Starts the server automatically in headless / dedicated server builds, with the settings of
    /// <see cref="ServerConfig"/>: an optional <c>server.cfg</c> (working folder, then the executable's folder, or
    /// <c>-config &lt;path&gt;</c>) and the command line over it, e.g. <c>-port 7770 -name "Kim's server"</c>.
    /// Headless, it also caps the frame rate so an idle server does not spin a whole CPU core, applies the
    /// <c>maxPlayers</c> override to the <see cref="SquadLobby"/> and logs the effective settings (never the token).
    /// <see cref="MasterServerAnnouncer"/> reads <see cref="Config"/> to list the server.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ServerBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private bool _autoStartWhenHeadless = true;

        [Tooltip("Receives the config's maxPlayers override. Found on this object when empty.")]
        [SerializeField] private SquadLobby _lobby;

        [Tooltip("Frame rate of a headless server (VSync is turned off). The network tick rate is set on the TimeManager.")]
        [SerializeField, Range(10, 240)] private int _headlessFrameRate = 60;

        private static readonly List<string> Warnings = new List<string>();

        /// <summary>True in a dedicated server build (UNITY_SERVER) or a player started with -batchmode.</summary>
        public static bool IsHeadless
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return Application.isBatchMode;
#endif
            }
        }

        /// <summary>The server settings, read in Awake (also outside headless mode, for the editor's dev server).</summary>
        public ServerConfig Config { get; private set; }

        private void Awake()
        {
            if (_networkManager == null) _networkManager = GetComponent<NetworkManager>();
            if (_lobby == null) _lobby = GetComponent<SquadLobby>();
            Config = LoadConfig();
        }

        private void Start()
        {
            if (!_autoStartWhenHeadless || !IsHeadless || _networkManager == null) return;

            // Nothing is drawn: without a cap the main loop runs as fast as it can.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = _headlessFrameRate;

            if (Config.Port != 0) _networkManager.TransportManager.Transport.SetPort(Config.Port);
            if (Config.MaxPlayers > 0)
            {
                if (_lobby != null) _lobby.SetMaxPlayersPerRaid(Config.MaxPlayers);
                else Debug.LogWarning($"[{nameof(ServerBootstrap)}] maxPlayers is set but there is no SquadLobby to apply it to.", this);
            }

            Debug.Log($"[{nameof(ServerBootstrap)}] Headless mode, starting server. {Config.ToLogString()} " +
                      $"port in use={_networkManager.TransportManager.Transport.GetPort()} frameRate={_headlessFrameRate}");
            _networkManager.ServerManager.StartConnection();
        }

        /// <summary>Reads server.cfg and the command line. Problems are logged as warnings.</summary>
        private ServerConfig LoadConfig()
        {
            Warnings.Clear();
            ServerConfig config = ServerConfig.Load(Environment.GetCommandLineArgs(), SearchDirectories(), File.Exists, File.ReadAllText, Warnings);
            foreach (string warning in Warnings)
                Debug.LogWarning($"[{nameof(ServerBootstrap)}] Server config: {warning}", this);
            return config;
        }

        /// <summary>Where server.cfg may be: the working folder, then the executable's folder (the project folder in the editor).</summary>
        private static IEnumerable<string> SearchDirectories()
        {
            yield return Environment.CurrentDirectory;
            string dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath)) yield break;
            // Linux / Windows: <folder>/<Game>_Data. Editor: <project>/Assets. macOS: <Game>.app/Contents.
            string parent = Path.GetDirectoryName(dataPath);
            if (string.IsNullOrEmpty(parent)) yield break;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            parent = Path.GetDirectoryName(parent); // the folder holding the .app
            if (string.IsNullOrEmpty(parent)) yield break;
#endif
            yield return parent;
        }
    }
}
