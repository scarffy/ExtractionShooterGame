using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Hosting;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Menu
{
    /// <summary>
    /// The server browser, opened from the main menu: reads the list of dedicated servers from the master server
    /// (<c>GET /api/servers</c>, see Docs/DedicatedServer.md) and joins one.
    /// <list type="bullet">
    /// <item>Master server address field (saved in PlayerPrefs), Refresh, search, region, sort (players / ping) and
    /// "Show other versions". The list refreshes itself every <see cref="AutoRefreshSeconds"/> while open.</item>
    /// <item>Rows: name, map, difficulties, players, raid status (time left counts down between refreshes), region,
    /// ping (<see cref="ServerPinger"/>) and Join. Servers on another game version are listed only on request and
    /// cannot be joined.</item>
    /// <item>Loading, empty and error states ("Could not reach the server list").</item>
    /// </list>
    /// Join raises <see cref="JoinRequested"/>; <see cref="GameMenu"/> connects with the player name.
    /// Plain C#: owned by <see cref="GameMenu"/>, which calls <see cref="Tick"/> every frame.
    /// </summary>
    public sealed class ServerBrowserPanel : IDisposable
    {
        /// <summary>Seconds between automatic refreshes while the panel is open.</summary>
        public const float AutoRefreshSeconds = 15f;

        private const int RequestTimeoutSeconds = 5;
        private const float PingResortSeconds = 1f;
        private const string HiddenClass = "hidden";
        private const string RowClass = "browser-row";
        private const string CellClass = "browser-cell";
        private const string OtherVersionClass = "browser-row--other-version";
        private const string ErrorClass = "browser-status--error";
        private const string AllRegions = "All regions";
        private static readonly List<string> SortChoices = new List<string> { "Most players", "Lowest ping" };

        private readonly VisualElement _screen;
        private readonly TextField _masterField;
        private readonly TextField _searchField;
        private readonly DropdownField _regionField;
        private readonly DropdownField _sortField;
        private readonly Toggle _versionsToggle;
        private readonly ScrollView _list;
        private readonly Label _countLabel;
        private readonly Label _statusLabel;
        private readonly Action _onClick;
        private readonly string _defaultMasterUrl;
        private readonly string _version;
        private readonly List<(Button Button, Action Handler)> _handlers = new List<(Button, Action)>();
        private readonly EventCallback<ChangeEvent<string>> _onFilterText;
        private readonly EventCallback<ChangeEvent<bool>> _onFilterToggle;
        private readonly ServerPinger _pinger = new ServerPinger();

        private readonly List<ServerEntry> _servers = new List<ServerEntry>();
        private readonly Dictionary<string, List<Label>> _pingLabels = new Dictionary<string, List<Label>>();
        private readonly List<(Label Label, ServerEntry Entry)> _raidLabels = new List<(Label, ServerEntry)>();

        private UnityWebRequest _request;
        private string _masterUrl = string.Empty;
        private float _fetchedAt;
        private bool _hasList;
        private string _error = string.Empty;
        private string _message = string.Empty;
        private float _nextRefreshAt;
        private float _nextClockUpdate;
        private float _nextPingResort;
        private bool _pingsChanged;

        /// <param name="root">The menu document's root.</param>
        /// <param name="onClick">Plays the menu click.</param>
        /// <param name="defaultMasterUrl">Master server address used until the player saves another.</param>
        /// <param name="version">This game's version (Application.version): only matching servers can be joined.</param>
        public ServerBrowserPanel(VisualElement root, Action onClick, string defaultMasterUrl, string version)
        {
            _onClick = onClick;
            _defaultMasterUrl = defaultMasterUrl ?? string.Empty;
            _version = version ?? string.Empty;

            _screen = root.Q<VisualElement>("server-browser");
            _masterField = root.Q<TextField>("browser-master-field") ?? new TextField();
            _searchField = root.Q<TextField>("browser-search-field") ?? new TextField();
            _regionField = root.Q<DropdownField>("browser-region-field");
            _sortField = root.Q<DropdownField>("browser-sort-field");
            _versionsToggle = root.Q<Toggle>("browser-versions-toggle");
            _list = root.Q<ScrollView>("browser-list");
            _countLabel = root.Q<Label>("browser-count");
            _statusLabel = root.Q<Label>("browser-status");

            Listen(root.Q<Button>("browser-refresh-button"), () => Refresh(true, readField: true));
            Listen(root.Q<Button>("browser-back-button"), () => BackRequested?.Invoke());

            _onFilterText = _ => Rebuild();
            _onFilterToggle = _ => Rebuild();
            _searchField.RegisterValueChangedCallback(_onFilterText);
            if (_regionField != null)
            {
                _regionField.choices = new List<string> { AllRegions };
                _regionField.SetValueWithoutNotify(AllRegions);
                _regionField.RegisterValueChangedCallback(_onFilterText);
            }
            if (_sortField != null)
            {
                _sortField.choices = new List<string>(SortChoices);
                _sortField.SetValueWithoutNotify(SortChoices[0]);
                _sortField.RegisterValueChangedCallback(_onFilterText);
            }
            _versionsToggle?.RegisterValueChangedCallback(_onFilterToggle);

            SetVisible(_screen, false);
        }

        /// <summary>Back was pressed (or Escape): the owner closes the panel.</summary>
        public event Action BackRequested;

        /// <summary>Join was pressed on a server of this game version.</summary>
        public event Action<ServerEntry> JoinRequested;

        public bool IsOpen { get; private set; }

        public void Open()
        {
            IsOpen = true;
            _message = string.Empty;
            _masterField.SetValueWithoutNotify(PlayerPrefs.GetString(MasterServerApi.MasterUrlPrefsKey, _defaultMasterUrl));
            SetVisible(_screen, true);
            _screen?.BringToFront();
            Refresh(false, readField: true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            AbortRequest();
            _pinger.Cancel();
            SetVisible(_screen, false);
        }

        /// <summary>Shows a message above the list (e.g. why Join cannot go ahead) until the next refresh.</summary>
        public void ShowMessage(string message)
        {
            _message = message ?? string.Empty;
            UpdateStatus();
        }

        public void Dispose()
        {
            foreach ((Button button, Action handler) in _handlers)
                if (button != null) button.clicked -= handler;
            _handlers.Clear();
            _searchField.UnregisterValueChangedCallback(_onFilterText);
            _regionField?.UnregisterValueChangedCallback(_onFilterText);
            _sortField?.UnregisterValueChangedCallback(_onFilterText);
            _versionsToggle?.UnregisterValueChangedCallback(_onFilterToggle);
            AbortRequest();
            _pinger.Dispose();
        }

        /// <summary>Polls the list request and the pings, refreshes on schedule and counts raid timers down.</summary>
        public void Tick()
        {
            if (!IsOpen) return;
            float now = Time.unscaledTime;

            if (_request != null && _request.isDone) FinishRequest(now);
            if (_request == null && now >= _nextRefreshAt) Refresh(false, readField: false);

            if (_pinger.Tick(now))
            {
                UpdatePingLabels();
                _pingsChanged = true;
            }
            if (_pingsChanged && SortOrder == ServerSortOrder.Ping && now >= _nextPingResort)
            {
                _nextPingResort = now + PingResortSeconds;
                Rebuild();
            }
            if (SortOrder != ServerSortOrder.Ping) _pingsChanged = false;

            if (now >= _nextClockUpdate)
            {
                _nextClockUpdate = now + 1f;
                foreach ((Label label, ServerEntry entry) in _raidLabels) SetText(label, ServerBrowserList.RaidText(entry, now - _fetchedAt));
            }
        }

        // ---------------- Fetching ----------------

        /// <summary>
        /// Fetches the list. <paramref name="userAction"/>: the Refresh button (clears messages). <paramref name="readField"/>:
        /// take the address from the field (opening, Refresh) rather than the last one used (auto-refresh, so a half-typed
        /// address is not tried or saved).
        /// </summary>
        private void Refresh(bool userAction, bool readField)
        {
            float now = Time.unscaledTime;
            _nextRefreshAt = now + AutoRefreshSeconds;
            if (userAction) _message = string.Empty;

            string input = readField || _masterUrl.Length == 0 ? _masterField.value : _masterUrl;
            if (!MasterServerApi.TryNormalizeBaseUrl(input, out string masterUrl))
            {
                AbortRequest();
                _error = "Master server: enter an http:// or https:// address, e.g. " + _defaultMasterUrl;
                UpdateStatus();
                return;
            }
            _masterUrl = masterUrl;
            if (readField && _masterField.value != masterUrl) _masterField.SetValueWithoutNotify(masterUrl);
            if (PlayerPrefs.GetString(MasterServerApi.MasterUrlPrefsKey, string.Empty) != masterUrl)
            {
                PlayerPrefs.SetString(MasterServerApi.MasterUrlPrefsKey, masterUrl);
                PlayerPrefs.Save();
            }

            AbortRequest();
            UnityWebRequest request = UnityWebRequest.Get(MasterServerApi.ListUrl(masterUrl));
            request.timeout = RequestTimeoutSeconds;
            try
            {
                request.SendWebRequest();
            }
            catch (InvalidOperationException exception)
            {
                request.Dispose();
                _error = "Could not reach the server list: " + exception.Message +
                         " (plain http:// needs Player Settings → 'Allow downloads over HTTP'; or use https://)";
                UpdateStatus();
                return;
            }
            _request = request;
            _error = string.Empty;
            UpdateStatus();
        }

        private void FinishRequest(float now)
        {
            UnityWebRequest request = _request;
            _request = null;
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string detail = request.result == UnityWebRequest.Result.ProtocolError ? $"HTTP {request.responseCode}" : request.error;
                    _error = "Could not reach the server list" + (string.IsNullOrEmpty(detail) ? "." : $" ({detail}).");
                    UpdateStatus();
                    return;
                }

                ServerListResponse response;
                try
                {
                    response = JsonUtility.FromJson<ServerListResponse>(request.downloadHandler.text);
                }
                catch (ArgumentException)
                {
                    response = null;
                }
                if (response == null)
                {
                    _error = "Could not reach the server list (the answer was not a server list: check the address).";
                    UpdateStatus();
                    return;
                }

                _servers.Clear();
                if (response.servers != null)
                    foreach (ServerEntry entry in response.servers)
                        if (entry != null && !string.IsNullOrEmpty(entry.host) && entry.port > 0 && entry.port <= 65535) _servers.Add(entry);
                _hasList = true;
                _fetchedAt = now;
                _error = string.Empty;
                foreach (ServerEntry entry in _servers) _pinger.Request(entry.host);
                RefreshRegions();
                Rebuild();
            }
            finally
            {
                request.Dispose();
            }
        }

        private void AbortRequest()
        {
            if (_request == null) return;
            _request.Abort();
            _request.Dispose();
            _request = null;
        }

        // ---------------- List ----------------

        private ServerSortOrder SortOrder => _sortField != null && _sortField.index == 1 ? ServerSortOrder.Ping : ServerSortOrder.Players;

        private ServerBrowserFilter Filter => new ServerBrowserFilter
        {
            Search = _searchField.value,
            Region = _regionField != null && _regionField.index > 0 ? _regionField.value : string.Empty,
            ShowOtherVersions = _versionsToggle != null && _versionsToggle.value,
            Version = _version,
        };

        /// <summary>"All regions" plus the regions in the list; keeps the chosen one when it is still there.</summary>
        private void RefreshRegions()
        {
            if (_regionField == null) return;
            string chosen = _regionField.index > 0 ? _regionField.value : AllRegions;
            var choices = new List<string> { AllRegions };
            foreach (string region in ServerBrowserList.Regions(_servers)) choices.Add(ServerBrowserList.RegionText(region));
            if (!choices.Contains(chosen)) choices.Add(chosen); // still filter by it until the player changes it
            _regionField.choices = choices;
            _regionField.SetValueWithoutNotify(chosen);
        }

        private void Rebuild()
        {
            _pingsChanged = false;
            _pingLabels.Clear();
            _raidLabels.Clear();
            if (_list == null) return;
            _list.Clear();

            List<ServerEntry> shown = ServerBrowserList.Apply(_servers, Filter, SortOrder, e => _pinger.Get(e.host));
            double age = Time.unscaledTime - _fetchedAt;
            foreach (ServerEntry entry in shown) _list.Add(BuildRow(entry, age));

            int hidden = _servers.Count - shown.Count;
            SetText(_countLabel, !_hasList ? string.Empty
                : $"{shown.Count} server{(shown.Count == 1 ? "" : "s")}" + (hidden > 0 ? $"  ·  {hidden} hidden by filters" : string.Empty));
            UpdateStatus();
        }

        private VisualElement BuildRow(ServerEntry entry, double age)
        {
            bool joinable = ServerBrowserList.IsVersionMatch(entry, _version);
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            row.EnableInClassList(OtherVersionClass, !joinable);

            var nameCell = new VisualElement();
            nameCell.AddToClassList(CellClass);
            nameCell.AddToClassList("browser-col--name");
            nameCell.Add(NewLabel(entry.name, "browser-name"));
            nameCell.Add(NewLabel(joinable ? $"{entry.host}:{entry.port}" : $"Version {entry.version} (you have {_version})", "browser-detail"));
            row.Add(nameCell);

            row.Add(Cell(entry.map, "browser-col--map"));
            row.Add(Cell(ServerBrowserList.DifficultiesText(entry.difficulties), "browser-col--difficulty"));
            row.Add(Cell(ServerBrowserList.PlayersText(entry), "browser-col--players"));

            Label raid = Cell(ServerBrowserList.RaidText(entry, age), "browser-col--raid");
            _raidLabels.Add((raid, entry));
            row.Add(raid);

            row.Add(Cell(ServerBrowserList.RegionText(entry.region), "browser-col--region"));

            Label ping = Cell(ServerBrowserList.PingText(_pinger.Get(entry.host)), "browser-col--ping");
            if (!_pingLabels.TryGetValue(entry.host, out List<Label> labels)) _pingLabels[entry.host] = labels = new List<Label>();
            labels.Add(ping);
            row.Add(ping);

            var join = new Button { text = joinable ? "Join" : "Other version" };
            join.AddToClassList("menu-button");
            join.AddToClassList("menu-button--primary");
            join.AddToClassList("browser-join");
            join.SetEnabled(joinable);
            if (!joinable) join.tooltip = $"This server runs version {entry.version}; you have {_version}.";
            // Not tracked in _handlers: the row and its button are dropped on the next rebuild.
            join.clicked += () =>
            {
                _onClick?.Invoke();
                JoinRequested?.Invoke(entry);
            };
            row.Add(join);
            return row;
        }

        private void UpdatePingLabels()
        {
            foreach (KeyValuePair<string, List<Label>> pair in _pingLabels)
            {
                string text = ServerBrowserList.PingText(_pinger.Get(pair.Key));
                foreach (Label label in pair.Value) SetText(label, text);
            }
        }

        /// <summary>The line under the list: a message, the error, loading, or the empty state.</summary>
        private void UpdateStatus()
        {
            string text;
            bool error = false;
            if (_message.Length > 0) { text = _message; error = true; }
            else if (_error.Length > 0) { text = _error + (_hasList && _servers.Count > 0 ? " Showing the last list." : string.Empty); error = true; }
            else if (_request != null) text = _hasList ? "Refreshing…" : "Loading the server list…";
            else if (!_hasList) text = string.Empty;
            else if (_servers.Count == 0) text = "No servers are online right now. Host a game, or start a dedicated server.";
            else if (_list != null && _list.childCount == 0) text = "No servers match the filters.";
            else text = string.Empty;

            SetText(_statusLabel, text);
            _statusLabel?.EnableInClassList(ErrorClass, error);
        }

        // ---------------- Helpers ----------------

        private static Label Cell(string text, string columnClass)
        {
            Label label = NewLabel(text, CellClass);
            label.AddToClassList(columnClass);
            return label;
        }

        private static Label NewLabel(string text, string className)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList(className);
            return label;
        }

        private void Listen(Button button, Action action)
        {
            if (button == null) return;
            Action handler = () =>
            {
                _onClick?.Invoke();
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
            if (label != null && label.text != text) label.text = text ?? string.Empty;
        }
    }
}
