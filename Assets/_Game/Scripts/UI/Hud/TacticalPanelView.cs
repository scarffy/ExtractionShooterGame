using System.Collections.Generic;
using ExtractionShooter.Core.Navigation;
using ExtractionShooter.Core.Raid;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Raid;
using ExtractionShooter.Gameplay.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// Delta Force-style tactical side panel, shown on the left of the screen while the <see cref="MapView"/> is open:
    /// raid time and extraction state, every extraction point with direction, distance and status, the listed value
    /// of the gear being carried (equipment, chest rig, backpack, pockets), health and injuries, and the squad
    /// (from <see cref="TeammateRoster"/>; the local player alone until squads register a source). Built in code.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class TacticalPanelView : MonoBehaviour
    {
        private const float HealthBarHeight = 6f;

        [Tooltip("The map this panel accompanies; the panel is shown while it is open.")]
        [SerializeField] private MapView _map;

        [Tooltip("Item values for the carried gear total.")]
        [SerializeField] private ItemDatabase _itemDatabase;

        [Tooltip("Panel width in pixels; keep it equal to the map's side panel width.")]
        [SerializeField, Min(200f)] private float _width = 380f;

        [Tooltip("Seconds between recounts of the carried gear value.")]
        [SerializeField, Min(0.05f)] private float _valueRefreshSeconds = 0.5f;

        [SerializeField] private Color _background = new Color(0.06f, 0.07f, 0.08f, 0.96f);
        [SerializeField] private Color _textColor = new Color(0.92f, 0.92f, 0.86f, 1f);
        [SerializeField] private Color _accentColor = new Color(0.95f, 0.75f, 0.35f, 1f);
        [SerializeField] private Color _extractColor = new Color(0.35f, 0.9f, 0.45f, 1f);
        [SerializeField] private Color _warningColor = new Color(0.95f, 0.35f, 0.3f, 1f);

        private readonly List<ExtractionRow> _extractionRows = new List<ExtractionRow>();
        private readonly List<SquadRow> _squadRows = new List<SquadRow>();
        private readonly List<TeammateInfo> _teammates = new List<TeammateInfo>();

        private VisualElement _root;
        private VisualElement _extractionList;
        private VisualElement _squadList;
        private Label _operator;
        private Label _time;
        private Label _raidStatus;
        private Label _gearValue;
        private Label _healthText;
        private VisualElement _healthFill;
        private Label _injuries;
        private float _nextValueRefresh;
        private bool _visible;
        private readonly List<string> _injuryLines = new List<string>(3);

        private sealed class ExtractionRow
        {
            public VisualElement Root;
            public Label Name;
            public Label Direction;
            public Label Status;
        }

        private sealed class SquadRow
        {
            public VisualElement Root;
            public Label Name;
            public Label Detail;
        }

        private Color DimText => new Color(_textColor.r, _textColor.g, _textColor.b, 0.6f);

        private void OnEnable()
        {
            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;

            _root = MapUi.Box("tactical-panel");
            _root.pickingMode = PickingMode.Position;
            _root.style.left = 0f;
            _root.style.top = 0f;
            _root.style.bottom = 0f;
            _root.style.width = _width;
            _root.style.backgroundColor = _background;
            _root.style.borderRightWidth = 2f;
            _root.style.borderRightColor = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.6f);
            MapUi.Padding(_root, 20f, 18f);
            documentRoot.Add(_root);

            _root.Add(MapUi.Text("TACTICAL", 13f, _accentColor));
            _operator = MapUi.Text(string.Empty, 20f, _textColor);
            _operator.style.marginBottom = 6f;
            _root.Add(_operator);

            Section("RAID");
            _time = MapUi.Text("00:00", 34f, _textColor);
            _root.Add(_time);
            _raidStatus = MapUi.Text(string.Empty, 14f, DimText, false);
            _root.Add(_raidStatus);

            Section("EXTRACTION");
            _extractionList = MapUi.Group();
            _root.Add(_extractionList);

            Section("CARRIED VALUE");
            _gearValue = MapUi.Text(string.Empty, 22f, _accentColor);
            _root.Add(_gearValue);

            Section("STATUS");
            _healthText = MapUi.Text(string.Empty, 15f, _textColor);
            _root.Add(_healthText);
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.height = HealthBarHeight;
            track.style.marginTop = 4f;
            track.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            _healthFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _healthFill.style.height = HealthBarHeight;
            _healthFill.style.backgroundColor = _textColor;
            track.Add(_healthFill);
            _root.Add(track);
            _injuries = MapUi.Text(string.Empty, 14f, _textColor, false);
            _injuries.style.marginTop = 6f;
            _root.Add(_injuries);

            Section("SQUAD");
            _squadList = MapUi.Group();
            _root.Add(_squadList);

            MapUi.Show(_root, false);
        }

        private void OnDisable()
        {
            _root?.RemoveFromHierarchy();
            _root = null;
            _visible = false;
            _extractionRows.Clear();
            _squadRows.Clear();
        }

        private void LateUpdate()
        {
            if (_root == null) return;

            PlayerCharacter player = PlayerCharacter.Local;
            bool visible = _map != null && _map.IsOpen && player != null && player.Raid != null;
            bool wasVisible = _visible;
            _visible = visible;
            MapUi.Show(_root, visible);
            if (!visible) return;
            if (!wasVisible)
            {
                _root.BringToFront();
                _nextValueRefresh = 0f; // fresh numbers as soon as it opens
            }

            _teammates.Clear();
            TacticalSquad.Collect(player, _teammates);
            _operator.text = _teammates.Count > 0 && _teammates[0].IsLocal ? _teammates[0].Name : player.name;

            RefreshRaid(player.Raid);
            RefreshExtractions(player);
            RefreshStatus(player);
            RefreshSquad();

            if (Time.time >= _nextValueRefresh)
            {
                _nextValueRefresh = Time.time + _valueRefreshSeconds;
                _gearValue.text = PlayerTrading.FormatCredits(CarriedValue(player.Equipment));
            }
        }

        // ---------------- Sections ----------------

        private void RefreshRaid(PlayerRaid raid)
        {
            int seconds = raid.SecondsLeft;
            _time.text = NavigationMath.FormatClock(seconds);
            _time.style.color = seconds <= 120 ? _warningColor : _textColor;
            _raidStatus.text = raid.IsExtracting
                ? $"Extracting at {raid.ZoneName}  {Mathf.RoundToInt(raid.ExtractProgress * 100f)}%"
                : "Reach an extraction point before time runs out";
        }

        private void RefreshExtractions(PlayerCharacter player)
        {
            IReadOnlyList<ExtractionZone> zones = ExtractionZone.All;
            while (_extractionRows.Count < zones.Count) _extractionRows.Add(CreateExtractionRow());

            Vector3 position = player.transform.position;
            PlayerRaid raid = player.Raid;
            long credits = LocalCredits();
            for (int i = 0; i < _extractionRows.Count; i++)
            {
                ExtractionRow row = _extractionRows[i];
                ExtractionZone zone = i < zones.Count ? zones[i] : null;
                MapUi.Show(row.Root, zone != null);
                if (zone == null) continue;

                Vector3 target = zone.transform.position;
                float bearing = NavigationMath.BearingTo(position.x, position.z, target.x, target.z);
                float distance = NavigationMath.GroundDistance(position.x, position.z, target.x, target.z);
                row.Name.text = zone.DisplayName;
                row.Direction.text = $"{NavigationMath.CompassLabel(bearing)} {NavigationMath.FormatBearing(bearing)}  ·  {NavigationMath.FormatDistance(distance)}";

                bool here = raid.IsExtracting && raid.ZoneName == zone.DisplayName;
                ExtractionStatus status = zone.StatusFor(raid.SecondsLeft, credits);
                row.Status.text = here ? $"EXTRACTING {Mathf.RoundToInt(raid.ExtractProgress * 100f)}%" : status.Label;
                row.Status.style.color = here ? _accentColor : StatusColor(status);
                row.Name.style.color = status.CanExtract ? _extractColor : DimText;
            }
        }

        /// <summary>Open: green; paid and affordable: accent; can't afford: warning; closed: dim.</summary>
        private Color StatusColor(in ExtractionStatus status)
        {
            switch (status.Availability)
            {
                case ExtractionAvailability.PaidAffordable: return _accentColor;
                case ExtractionAvailability.PaidUnaffordable: return _warningColor;
                case ExtractionAvailability.Closed: return DimText;
                default: return _extractColor;
            }
        }

        /// <summary>The local player's credits (paid extractions); 0 before the session is known.</summary>
        private static long LocalCredits()
        {
            PlayerSession session = PlayerSession.Local;
            return session != null && session.Trading != null ? session.Trading.Money : 0L;
        }

        private void RefreshStatus(PlayerCharacter player)
        {
            Health health = player.Health;
            float health01 = health.Max > 0 ? Mathf.Clamp01((float)health.Current / health.Max) : 0f;
            _healthText.text = $"Health  {health.Current} / {health.Max}";
            _healthFill.style.width = (_width - 40f) * health01;
            _healthFill.style.backgroundColor = health01 <= 0.3f ? _warningColor : _textColor;

            PlayerInjuries injuries = player.Injuries;
            List<string> lines = _injuryLines;
            lines.Clear();
            if (injuries != null)
            {
                if (injuries.IsBleeding) lines.Add("Bleeding");
                if (injuries.IsFractured) lines.Add("Fractured leg");
                if (injuries.IsTreating) lines.Add($"Using {injuries.TreatmentName} {Mathf.RoundToInt(injuries.TreatmentProgress * 100f)}%");
            }
            bool injured = injuries != null && (injuries.IsBleeding || injuries.IsFractured);
            _injuries.text = lines.Count > 0 ? string.Join("  ·  ", lines) : "No injuries";
            _injuries.style.color = injured ? _warningColor : DimText;
        }

        private void RefreshSquad()
        {
            while (_squadRows.Count < _teammates.Count) _squadRows.Add(CreateSquadRow());

            TeammateInfo self = _teammates.Count > 0 ? _teammates[0] : default;
            for (int i = 0; i < _squadRows.Count; i++)
            {
                SquadRow row = _squadRows[i];
                bool used = i < _teammates.Count;
                MapUi.Show(row.Root, used);
                if (!used) continue;

                TeammateInfo mate = _teammates[i];
                row.Name.text = mate.IsLocal ? $"{mate.Name}  (you)" : mate.Name;
                row.Name.style.color = mate.IsLocal ? _accentColor : _textColor;
                string health = mate.IsAlive ? $"{Mathf.RoundToInt(mate.Health01 * 100f)}%" : "DOWN";
                if (mate.IsLocal || !self.IsLocal)
                {
                    row.Detail.text = health;
                }
                else
                {
                    float distance = NavigationMath.GroundDistance(self.WorldX, self.WorldZ, mate.WorldX, mate.WorldZ);
                    row.Detail.text = $"{health}  ·  {NavigationMath.FormatDistance(distance)}";
                }
                row.Detail.style.color = mate.IsAlive ? DimText : _warningColor;
            }
        }

        private long CarriedValue(PlayerEquipment equipment)
        {
            if (equipment == null || _itemDatabase == null) return 0;
            return GearValue.Of(equipment.Container, _itemDatabase) + GearValue.Of(equipment.ChestRigStorage, _itemDatabase) +
                   GearValue.Of(equipment.BackpackStorage, _itemDatabase) + GearValue.Of(equipment.Pockets, _itemDatabase);
        }

        // ---------------- Elements ----------------

        private void Section(string title)
        {
            Label label = MapUi.Text(title, 12f, _accentColor);
            label.style.marginTop = 16f;
            label.style.marginBottom = 4f;
            label.style.paddingBottom = 2f;
            label.style.borderBottomWidth = 1f;
            label.style.borderBottomColor = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.35f);
            _root.Add(label);
        }

        private ExtractionRow CreateExtractionRow()
        {
            var row = new ExtractionRow { Root = MapUi.Group() };
            row.Root.style.marginBottom = 6f;

            VisualElement top = MapUi.Group(FlexDirection.Row);
            top.style.justifyContent = Justify.SpaceBetween;
            row.Name = MapUi.Text(string.Empty, 16f, _extractColor);
            row.Status = MapUi.Text(string.Empty, 13f, _extractColor);
            top.Add(row.Name);
            top.Add(row.Status);
            row.Root.Add(top);

            row.Direction = MapUi.Text(string.Empty, 13f, DimText, false);
            row.Root.Add(row.Direction);

            _extractionList.Add(row.Root);
            return row;
        }

        private SquadRow CreateSquadRow()
        {
            var row = new SquadRow { Root = MapUi.Group(FlexDirection.Row) };
            row.Root.style.justifyContent = Justify.SpaceBetween;
            row.Root.style.marginBottom = 4f;
            row.Name = MapUi.Text(string.Empty, 15f, _textColor);
            row.Detail = MapUi.Text(string.Empty, 13f, DimText, false);
            row.Root.Add(row.Name);
            row.Root.Add(row.Detail);
            _squadList.Add(row.Root);
            return row;
        }
    }
}
