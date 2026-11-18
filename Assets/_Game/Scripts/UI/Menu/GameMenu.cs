using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Hosting;
using ExtractionShooter.Core.Input;
using ExtractionShooter.Gameplay.Audio;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Settings;
using ExtractionShooter.UI.Hud;
using ExtractionShooter.UI.Inventory;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Menu
{
    /// <summary>
    /// The game's front end, drawn above every other screen:
    /// <list type="bullet">
    /// <item>Main menu (whenever not signed in): player name, Host game, Join game (address + port), Settings, Quit, and the
    /// connection status or error. Host / Join connect through <see cref="SessionConnector"/>, which signs in with the
    /// name automatically; a refused name or a lost connection comes back here with the reason.</item>
    /// <item>Pause menu (Escape in the hideout or a raid): Resume, Settings, Leave raid / Disconnect and Quit. Leaving
    /// a raid alive (or stopping a server others play on) asks first. While it is open the cursor is free, so gameplay
    /// input reads nothing, and the inventory ignores Tab.</item>
    /// <item>Settings (<see cref="SettingsPanel"/>), from either menu.</item>
    /// <item>Server browser (<see cref="ServerBrowserPanel"/>), from the main menu: dedicated servers listed on the
    /// master server; Join connects with the name field.</item>
    /// </list>
    /// Escape closes the innermost thing first: a key being rebound, the confirm box, settings, the server browser, the pause menu, then the
    /// inventory or map; with nothing open in game it opens the pause menu. Headless servers (batch mode or
    /// UNITY_SERVER) never show it: <c>ServerBootstrap</c> starts the server there.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameMenu : MonoBehaviour
    {
        private const string HiddenClass = "hidden";
        // Same key as the hideout's sign-in panel, so both remember the same name.
        private const string NameKey = "ExtractionShooter.PlayerName";
        private const string AddressKey = "ExtractionShooter.JoinAddress";
        private const string PortKey = "ExtractionShooter.Port";

        [SerializeField] private NetworkManager _networkManager;

        [Tooltip("Closed by Escape before the pause menu opens.")]
        [SerializeField] private InventoryScreen _inventoryScreen;

        [Tooltip("Closed by Escape before the pause menu opens.")]
        [SerializeField] private MapView _mapView;

        [Tooltip("Address the Join field starts with until the player types another.")]
        [SerializeField] private string _defaultAddress = "localhost";

        [Tooltip("Master server the server browser reads until the player saves another address.")]
        [SerializeField] private string _defaultMasterUrl = "http://localhost:8080";

        private readonly List<(Button Button, Action Handler)> _handlers = new List<(Button, Action)>();

        private VisualElement _root;
        private VisualElement _mainMenu;
        private VisualElement _mainButtons;
        private VisualElement _statusRow;
        private Label _statusLabel;
        private Label _errorLabel;
        private Button _cancelButton;
        private Button _serverButton;
        private TextField _nameField;
        private TextField _addressField;
        private TextField _portField;
        private VisualElement _pauseMenu;
        private Label _pauseContext;
        private Button _leaveButton;
        private VisualElement _confirm;
        private Label _confirmTitle;
        private Label _confirmMessage;
        private Button _confirmOkButton;

        private SessionConnector _connector;
        private SettingsPanel _settings;
        private ServerBrowserPanel _browser;
        private InputAction _pauseAction;
        private AudioSource _clickSource;
        private Action _confirmAction;
        private bool _pauseOpen;
        private string _localError = string.Empty;
        private bool _started;

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

        private bool InGame => _connector != null && _connector.Phase == ConnectionPhase.InGame;

        // Set up in Start: the NetworkManager creates its client and server managers in its own Awake.
        private void Start()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            VisualElement menuRoot = _root != null ? _root.Q<VisualElement>("menu-root") : null;

            if (IsHeadless)
            {
                SetVisible(menuRoot, false);
                enabled = false;
                return;
            }

            if (menuRoot == null || _networkManager == null)
            {
                Debug.LogError($"[{nameof(GameMenu)}] UXML 'menu-root' or the NetworkManager is missing.", this);
                enabled = false;
                return;
            }

            QueryElements();
            CreateClickSound();
            SettingsApplier.Apply(GameSettingsStore.Current);
            GameSettingsStore.Changed += SettingsApplier.Apply;

            _connector = new SessionConnector(_networkManager);
            _connector.Changed += Refresh;
            _settings = new SettingsPanel(_root, transform, PlayClick);
            _settings.BackRequested += CloseSettings;
            _browser = new ServerBrowserPanel(_root, PlayClick, _defaultMasterUrl, Application.version);
            _browser.BackRequested += CloseBrowser;
            _browser.JoinRequested += JoinListedServer;
            CursorController.LockChanged += OnCursorLockChanged;

            _pauseAction = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            _pauseAction.AddBinding("<Gamepad>/start");
            _pauseAction.Enable();

            _nameField.value = PlayerPrefs.GetString(NameKey, string.Empty);
            _addressField.value = PlayerPrefs.GetString(AddressKey, _defaultAddress);
            _portField.value = PlayerPrefs.GetInt(PortKey, _connector.DefaultPort).ToString();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SetVisible(_serverButton, true);
#else
            SetVisible(_serverButton, false);
#endif
            _started = true;
            Refresh();
        }

        private void OnDestroy()
        {
            if (!_started) return;
            GameSettingsStore.Changed -= SettingsApplier.Apply;
            CursorController.LockChanged -= OnCursorLockChanged;
            foreach ((Button button, Action handler) in _handlers)
                if (button != null) button.clicked -= handler;
            _handlers.Clear();
            if (_settings != null)
            {
                _settings.BackRequested -= CloseSettings;
                _settings.Dispose();
            }
            if (_browser != null)
            {
                _browser.BackRequested -= CloseBrowser;
                _browser.JoinRequested -= JoinListedServer;
                _browser.Dispose();
            }
            if (_connector != null)
            {
                _connector.Changed -= Refresh;
                _connector.Dispose();
            }
            _pauseAction?.Dispose();
            MenuState.SetOpen(false);
        }

        private void Update()
        {
            if (!_started) return;
            _connector.Tick();
            _browser.Tick();
            if (_pauseOpen) RefreshPauseText();
            if (_pauseAction.WasPressedThisFrame()) OnEscape();
        }

        // ---------------- Escape ----------------

        private void OnEscape()
        {
            // That Escape cancelled a key rebind.
            if (_settings.IsCapturingInput || _settings.LastRebindEndFrame == Time.frameCount) return;

            if (_confirm != null && _confirmAction != null) { CloseConfirm(); return; }
            if (_settings.IsOpen) { CloseSettings(); return; }
            if (_browser.IsOpen) { CloseBrowser(); return; }
            if (!InGame) return; // main menu: nothing to go back to

            if (_pauseOpen) { Resume(); return; }
            if (_inventoryScreen != null && _inventoryScreen.IsOpen) { _inventoryScreen.Close(); return; }
            if (_mapView != null && _mapView.IsOpen) { _mapView.Close(); return; }
            OpenPause();
        }

        // ---------------- Main menu ----------------

        private void Host()
        {
            if (!TryReadName(out string playerName) || !TryReadPort(out ushort port)) return;
            Remember(playerName, port);
            _connector.Host(playerName, port);
        }

        private void Join()
        {
            if (!TryReadName(out string playerName) || !TryReadPort(out ushort port)) return;
            string address = _addressField.value != null ? _addressField.value.Trim() : string.Empty;
            if (address.Length == 0)
            {
                ShowLocalError("Enter the host's address (localhost on this computer).");
                return;
            }
            Remember(playerName, port);
            PlayerPrefs.SetString(AddressKey, address);
            PlayerPrefs.Save();
            _connector.Join(playerName, address, port);
        }

        /// <summary>Joins a server picked in the server browser, with the name typed in the main menu.</summary>
        private void JoinListedServer(ServerEntry server)
        {
            if (server == null || string.IsNullOrEmpty(server.host) || server.port < 1 || server.port > 65535) return;
            string error = SessionConnector.ValidateName(_nameField.value, out string playerName);
            if (error != null)
            {
                _browser.ShowMessage(error + " Type it in the main menu (Back).");
                return;
            }

            var port = (ushort)server.port;
            _browser.Close();
            Remember(playerName, port);
            _addressField.value = server.host;
            _portField.value = port.ToString();
            PlayerPrefs.SetString(AddressKey, server.host);
            PlayerPrefs.Save();
            _connector.Join(playerName, server.host, port);
        }

        /// <summary>Development: a server with no local player (other editors or builds join it).</summary>
        private void StartServerOnly()
        {
            if (!TryReadPort(out ushort port)) return;
            PlayerPrefs.SetInt(PortKey, port);
            PlayerPrefs.Save();
            _connector.StartServerOnly(port);
        }

        private bool TryReadName(out string playerName)
        {
            string error = SessionConnector.ValidateName(_nameField.value, out playerName);
            if (error == null) return true;
            ShowLocalError(error);
            return false;
        }

        private bool TryReadPort(out ushort port)
        {
            if (ushort.TryParse(_portField.value != null ? _portField.value.Trim() : string.Empty, out port) && port > 0) return true;
            ShowLocalError("Port: a number from 1 to 65535.");
            return false;
        }

        private void Remember(string playerName, ushort port)
        {
            _localError = string.Empty;
            PlayerPrefs.SetString(NameKey, playerName);
            PlayerPrefs.SetInt(PortKey, port);
            PlayerPrefs.Save();
        }

        private void ShowLocalError(string error)
        {
            _localError = error;
            Refresh();
        }

        // ---------------- Pause menu ----------------

        private void OpenPause()
        {
            _pauseOpen = true;
            CursorController.SetLocked(false);
            RefreshPauseText();
            Refresh();
        }

        private void Resume()
        {
            _pauseOpen = false; // first: locking the cursor below raises LockChanged
            _confirmAction = null;
            Refresh();
            PlayerCharacter local = PlayerCharacter.Local;
            if (local != null && local.Health.IsAlive) CursorController.SetLocked(true);
        }

        private void Leave()
        {
            // In a raid: give it up and go back to the hideout (the connection stays). Otherwise: disconnect.
            if (IsAliveInRaid)
            {
                Confirm("Leave the raid?", "Everything you brought into the raid is lost, except your safe box and knife.",
                    "Leave raid", LeaveRaid);
                return;
            }
            string warning = LeaveWarning();
            if (warning == null) _connector.Disconnect();
            else Confirm("Disconnect?", warning, "Disconnect", _connector.Disconnect);
        }

        private void LeaveRaid()
        {
            PlayerCharacter local = PlayerCharacter.Local;
            if (local != null && local.Raid != null) local.Raid.RequestLeaveRaid();
            // Close the menu but keep the cursor free: the character dies and the hideout takes over.
            _pauseOpen = false;
            Refresh();
        }

        private void QuitFromPause()
        {
            string warning = LeaveWarning();
            if (warning == null) QuitGame();
            else Confirm("Quit the game?", warning, "Quit", QuitGame);
        }

        /// <summary>What leaving now costs, or null when nothing is lost.</summary>
        private string LeaveWarning()
        {
            string warning = null;
            if (IsAliveInRaid) warning = "Everything you brought into the raid is lost, except your safe box.";
            if (_connector.IsServer)
                warning = (warning != null ? warning + "\n" : string.Empty) + "You are hosting: every player on your server is disconnected.";
            return warning;
        }

        private static bool IsAliveInRaid => PlayerCharacter.Local != null && PlayerCharacter.Local.Health.IsAlive;

        private void RefreshPauseText()
        {
            bool inRaid = PlayerCharacter.Local != null;
            SetText(_pauseContext, (inRaid ? "In raid" : "Hideout") + (_connector.IsServer ? "  ·  Hosting" : string.Empty));
            if (_leaveButton != null)
            {
                string text = inRaid ? "Leave raid" : "Disconnect";
                if (_leaveButton.text != text) _leaveButton.text = text;
            }
        }

        private void OnCursorLockChanged(bool locked)
        {
            // Something gave the cursor back to the game (a raid started while paused in the hideout): close the menus.
            if (!locked || (!_pauseOpen && !_settings.IsOpen) || !InGame) return;
            _settings.Close();
            _pauseOpen = false;
            _confirmAction = null;
            Refresh();
        }

        // ---------------- Server browser ----------------

        private void OpenBrowser()
        {
            _localError = string.Empty;
            _browser.Open();
            Refresh();
        }

        private void CloseBrowser()
        {
            _browser.Close();
            Refresh();
        }

        // ---------------- Settings / confirm / quit ----------------

        private void OpenSettings()
        {
            _settings.Open();
            Refresh();
        }

        private void CloseSettings()
        {
            _settings.Close();
            Refresh();
        }

        private void Confirm(string title, string message, string okText, Action onConfirm)
        {
            SetText(_confirmTitle, title);
            SetText(_confirmMessage, message);
            if (_confirmOkButton != null) _confirmOkButton.text = okText;
            _confirmAction = onConfirm;
            Refresh();
        }

        private void CloseConfirm()
        {
            _confirmAction = null;
            Refresh();
        }

        private void OnConfirmed()
        {
            Action action = _confirmAction;
            _confirmAction = null;
            action?.Invoke();
            Refresh();
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            Debug.Log($"[{nameof(GameMenu)}] Quit is ignored in the editor.");
#else
            Application.Quit();
#endif
        }

        // ---------------- View ----------------

        private void Refresh()
        {
            if (!_started) return;

            bool inGame = InGame;
            if (!inGame) _pauseOpen = false;
            bool settingsOpen = _settings.IsOpen;
            if (inGame && _browser.IsOpen) _browser.Close();
            bool browserOpen = _browser.IsOpen;

            SetVisible(_mainMenu, !inGame && !settingsOpen && !browserOpen);
            SetVisible(_pauseMenu, inGame && _pauseOpen && !settingsOpen);
            SetVisible(_confirm, _confirmAction != null);

            ConnectionPhase phase = _connector.Phase;
            bool busy = phase != ConnectionPhase.Offline;
            SetVisible(_mainButtons, !busy);
            SetVisible(_statusRow, busy);
            SetText(_statusLabel, _connector.Status);
            if (_cancelButton != null) _cancelButton.text = phase == ConnectionPhase.ServerOnly ? "Stop server" : "Cancel";
            SetText(_errorLabel, busy ? string.Empty : _localError.Length > 0 ? _localError : _connector.Error);

            bool open = !inGame || _pauseOpen || settingsOpen;
            MenuState.SetOpen(open);
            if (!inGame) CursorController.SetLocked(false);
        }

        private void QueryElements()
        {
            _mainMenu = _root.Q<VisualElement>("main-menu");
            _mainButtons = _root.Q<VisualElement>("menu-main-buttons");
            _statusRow = _root.Q<VisualElement>("menu-status");
            _statusLabel = _root.Q<Label>("menu-status-label");
            _errorLabel = _root.Q<Label>("menu-error");
            _cancelButton = _root.Q<Button>("menu-cancel-button");
            _serverButton = _root.Q<Button>("menu-server-button");
            _nameField = _root.Q<TextField>("menu-name-field") ?? new TextField();
            _addressField = _root.Q<TextField>("menu-address-field") ?? new TextField();
            _portField = _root.Q<TextField>("menu-port-field") ?? new TextField();
            _pauseMenu = _root.Q<VisualElement>("pause-menu");
            _pauseContext = _root.Q<Label>("pause-context");
            _leaveButton = _root.Q<Button>("pause-leave-button");
            _confirm = _root.Q<VisualElement>("confirm-dialog");
            _confirmTitle = _root.Q<Label>("confirm-title");
            _confirmMessage = _root.Q<Label>("confirm-message");
            _confirmOkButton = _root.Q<Button>("confirm-ok-button");

            Listen(_root.Q<Button>("menu-host-button"), Host);
            Listen(_root.Q<Button>("menu-join-button"), Join);
            Listen(_root.Q<Button>("menu-browse-button"), OpenBrowser);
            Listen(_serverButton, StartServerOnly);
            Listen(_root.Q<Button>("menu-settings-button"), OpenSettings);
            Listen(_root.Q<Button>("menu-quit-button"), QuitGame);
            Listen(_cancelButton, () => _connector.Disconnect());
            Listen(_root.Q<Button>("pause-resume-button"), Resume);
            Listen(_root.Q<Button>("pause-settings-button"), OpenSettings);
            Listen(_leaveButton, Leave);
            Listen(_root.Q<Button>("pause-quit-button"), QuitFromPause);
            Listen(_root.Q<Button>("confirm-cancel-button"), CloseConfirm);
            Listen(_confirmOkButton, OnConfirmed);
        }

        private void CreateClickSound()
        {
            _clickSource = gameObject.AddComponent<AudioSource>();
            _clickSource.playOnAwake = false;
            _clickSource.spatialBlend = 0f;
        }

        private void PlayClick()
        {
            if (_clickSource != null) _clickSource.PlayOneShot(ProceduralAudio.UiClick(), SoundVolumes.Ui);
        }

        private void Listen(Button button, Action action)
        {
            if (button == null) return;
            Action handler = () =>
            {
                PlayClick();
                action();
            };
            button.clicked += handler;
            _handlers.Add((button, handler));
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null) element.EnableInClassList(HiddenClass, !visible);
        }

        private static void SetText(Label label, string text)
        {
            if (label != null) label.text = text ?? string.Empty;
        }
    }
}
