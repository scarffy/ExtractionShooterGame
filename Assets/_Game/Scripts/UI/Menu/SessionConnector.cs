using System;
using ExtractionShooter.Core.Persistence;
using ExtractionShooter.Gameplay.Session;
using FishNet.Managing;
using FishNet.Transporting;

namespace ExtractionShooter.UI.Menu
{
    /// <summary>Where the local game is on the way from the main menu into the hideout.</summary>
    public enum ConnectionPhase
    {
        /// <summary>Not connected: the main menu shows.</summary>
        Offline,
        StartingServer,
        Connecting,

        /// <summary>Connected; waiting for the session and the server's answer to the sign-in.</summary>
        SigningIn,

        /// <summary>Signed in: hideout or raid.</summary>
        InGame,

        /// <summary>Development: a server without a local player.</summary>
        ServerOnly,
    }

    /// <summary>
    /// Starts and stops the FishNet connection for the main menu and signs in automatically with the chosen name.
    /// Host starts the server, then connects the local client to it; Join connects to an address and port.
    /// Failures (server could not start, connection refused or lost, name refused) end in
    /// <see cref="ConnectionPhase.Offline"/> with <see cref="Error"/> set. Plain C#: owned by <see cref="GameMenu"/>,
    /// which calls <see cref="Tick"/> every frame.
    /// </summary>
    public sealed class SessionConnector : IDisposable
    {
        private const string LocalAddress = "localhost";

        private readonly NetworkManager _network;
        private bool _hosting;
        private bool _clientWasStarted;
        private bool _stopping;
        private string _playerName = string.Empty;
        private PlayerSession _session;
        private bool _signInSent;

        public SessionConnector(NetworkManager network)
        {
            _network = network;
            _network.ClientManager.OnClientConnectionState += OnClientState;
            _network.ServerManager.OnServerConnectionState += OnServerState;
        }

        /// <summary>Phase or messages changed.</summary>
        public event Action Changed;

        public ConnectionPhase Phase { get; private set; } = ConnectionPhase.Offline;

        /// <summary>Why the last attempt ended, or empty.</summary>
        public string Error { get; private set; } = string.Empty;

        /// <summary>What is happening right now ("Connecting to …"), or empty.</summary>
        public string Status { get; private set; } = string.Empty;

        /// <summary>True when this machine runs the server (host or server only).</summary>
        public bool IsServer => _network.ServerManager.Started;

        /// <summary>The port the transport uses (Tugboat's configured port until changed).</summary>
        public ushort DefaultPort => _network.TransportManager.Transport != null ? _network.TransportManager.Transport.GetPort() : (ushort)7770;

        public void Dispose()
        {
            _network.ClientManager.OnClientConnectionState -= OnClientState;
            _network.ServerManager.OnServerConnectionState -= OnServerState;
            BindSession(null);
        }

        /// <summary>Validates a player name the way the server does. Returns the error to show, or null.</summary>
        public static string ValidateName(string name, out string normalized)
        {
            return ProfileNames.TryNormalize(name, out normalized)
                ? null
                : $"Player name: {ProfileNames.MinLength}–{ProfileNames.MaxLength} letters, digits, _ or -.";
        }

        /// <summary>Starts a server on <paramref name="port"/> and joins it as a player.</summary>
        public void Host(string playerName, ushort port)
        {
            if (Phase != ConnectionPhase.Offline) return;
            Begin(playerName, hosting: true);
            SetPhase(ConnectionPhase.StartingServer, $"Starting server on port {port}…");
            _network.TransportManager.Transport.SetPort(port);
            if (!_network.ServerManager.StartConnection(port)) Fail($"Could not start a server on port {port}.");
        }

        /// <summary>Connects to a server at <paramref name="address"/>:<paramref name="port"/>.</summary>
        public void Join(string playerName, string address, ushort port)
        {
            if (Phase != ConnectionPhase.Offline) return;
            Begin(playerName, hosting: false);
            SetPhase(ConnectionPhase.Connecting, $"Connecting to {address}:{port}…");
            if (!_network.ClientManager.StartConnection(address, port)) Fail($"Could not connect to {address}:{port}.");
        }

        /// <summary>Development: a server with no local player.</summary>
        public void StartServerOnly(ushort port)
        {
            if (Phase != ConnectionPhase.Offline) return;
            Begin(string.Empty, hosting: false);
            SetPhase(ConnectionPhase.StartingServer, $"Starting server on port {port}…");
            _network.TransportManager.Transport.SetPort(port);
            if (!_network.ServerManager.StartConnection(port)) Fail($"Could not start a server on port {port}.");
        }

        /// <summary>Leaves: stops the client and, when this machine is the server, the server (everyone is disconnected).</summary>
        public void Disconnect() => Stop(string.Empty);

        /// <summary>Polls the local session: sends the sign-in once it exists and notices when the hideout is reached.</summary>
        public void Tick()
        {
            if (Phase != ConnectionPhase.SigningIn && Phase != ConnectionPhase.InGame) return;

            if (_session != PlayerSession.Local) BindSession(PlayerSession.Local);
            if (_session == null) return;

            if (_session.State == SessionState.SigningIn)
            {
                if (!_signInSent)
                {
                    _signInSent = true;
                    _session.RequestSignIn(_playerName);
                    SetPhase(ConnectionPhase.SigningIn, $"Signing in as {_playerName}…");
                }
                return;
            }

            if (Phase != ConnectionPhase.InGame) SetPhase(ConnectionPhase.InGame, string.Empty);
        }

        // ---------------- Internals ----------------

        private void Begin(string playerName, bool hosting)
        {
            _playerName = playerName ?? string.Empty;
            _hosting = hosting;
            _clientWasStarted = false;
            _signInSent = false;
            _stopping = false;
            Error = string.Empty;
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started when Phase == ConnectionPhase.StartingServer:
                    if (_hosting)
                    {
                        ushort port = _network.TransportManager.Transport.GetPort();
                        SetPhase(ConnectionPhase.Connecting, "Joining your server…");
                        if (!_network.ClientManager.StartConnection(LocalAddress, port)) Fail("Could not join your own server.");
                    }
                    else
                    {
                        SetPhase(ConnectionPhase.ServerOnly, $"Server running on port {_network.TransportManager.Transport.GetPort()} (no local player).");
                    }
                    break;

                case LocalConnectionState.Stopped when !_stopping && Phase != ConnectionPhase.Offline:
                    Stop(Phase == ConnectionPhase.StartingServer
                        ? $"Could not start a server on port {_network.TransportManager.Transport.GetPort()} (is the port in use?)."
                        : "The server stopped.");
                    break;
            }
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    _clientWasStarted = true;
                    if (Phase == ConnectionPhase.Connecting) SetPhase(ConnectionPhase.SigningIn, "Connected. Signing in…");
                    break;

                case LocalConnectionState.Stopped when !_stopping && Phase != ConnectionPhase.Offline && Phase != ConnectionPhase.ServerOnly:
                    string address = _network.TransportManager.Transport.GetClientAddress();
                    Stop(_clientWasStarted
                        ? "Disconnected from the server."
                        : $"Could not connect to {address}:{_network.TransportManager.Transport.GetPort()}.");
                    break;
            }
        }

        private void OnSignInFailed(string reason) => Stop(string.IsNullOrEmpty(reason) ? "Sign-in was refused." : reason);

        private void Fail(string error) => Stop(error);

        /// <summary>Stops whatever is running and returns to <see cref="ConnectionPhase.Offline"/> with <paramref name="error"/>.</summary>
        private void Stop(string error)
        {
            if (_stopping) return;
            _stopping = true; // the stop calls below raise state events: ignore them
            try
            {
                BindSession(null);
                if (_network.ClientManager.Started) _network.ClientManager.StopConnection();
                if (_network.ServerManager.Started) _network.ServerManager.StopConnection(true);
            }
            finally
            {
                _stopping = false;
            }

            _hosting = false;
            _signInSent = false;
            Error = error ?? string.Empty;
            SetPhase(ConnectionPhase.Offline, string.Empty);
        }

        private void BindSession(PlayerSession session)
        {
            if (_session != null) _session.SignInFailed -= OnSignInFailed;
            _session = session;
            if (_session != null) _session.SignInFailed += OnSignInFailed;
        }

        private void SetPhase(ConnectionPhase phase, string status)
        {
            Phase = phase;
            Status = status ?? string.Empty;
            Changed?.Invoke();
        }
    }
}
