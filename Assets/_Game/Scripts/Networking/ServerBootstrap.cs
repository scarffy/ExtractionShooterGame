using System;
using FishNet.Managing;
using UnityEngine;

namespace ExtractionShooter.Networking
{
    /// <summary>
    /// Starts the server automatically in headless / dedicated server builds.
    /// Optional command line: -port 7770
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ServerBootstrap : MonoBehaviour
    {
        private const string PortArgument = "-port";

        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private bool _autoStartWhenHeadless = true;

        private static bool IsHeadless
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

        private void Awake()
        {
            if (_networkManager == null) _networkManager = GetComponent<NetworkManager>();
        }

        private void Start()
        {
            if (!_autoStartWhenHeadless || !IsHeadless || _networkManager == null) return;

            if (TryGetPortArgument(out ushort port))
                _networkManager.TransportManager.Transport.SetPort(port);

            Debug.Log($"[{nameof(ServerBootstrap)}] Headless mode, starting server.");
            _networkManager.ServerManager.StartConnection();
        }

        private static bool TryGetPortArgument(out ushort port)
        {
            port = 0;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], PortArgument, StringComparison.OrdinalIgnoreCase))
                    return ushort.TryParse(args[i + 1], out port);
            }
            return false;
        }
    }
}
