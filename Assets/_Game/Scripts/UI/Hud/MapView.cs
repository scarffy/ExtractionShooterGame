using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Input;
using ExtractionShooter.Core.Navigation;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Raid;
using ExtractionShooter.UI.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// Full-screen top-down map of the raid area, toggled with M (Esc also closes, like the inventory).
    /// The play area and the cover on it are read at runtime from the renderers under the environment root
    /// (the one named <see cref="_groundName"/> is the ground and sets the map bounds; every other renderer is drawn
    /// as an obstacle footprint). Shows a 10 m grid, the extraction zones (labelled with name and distance),
    /// squad members from <see cref="TeammateRoster"/>, the local player as an arrow facing their heading, and the
    /// raid time left. The left side is kept free for the <see cref="TacticalPanelView"/>.
    /// Never open together with the inventory: opening the inventory closes the map. Built in code.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapView : MonoBehaviour
    {
        private const float HeaderHeight = 52f;
        private const float ArrowSize = 22f;
        private const float TeammateDotSize = 10f;
        private const float ZoneLabelWidth = 200f;

        [Tooltip("Root of the level geometry. Its child renderers are the ground and the cover drawn on the map.")]
        [SerializeField] private Transform _environmentRoot;

        [Tooltip("Name of the ground object under the environment root; its bounds are the map area.")]
        [SerializeField] private string _groundName = "Ground";

        [Tooltip("Map area (centred on the world origin) used when no ground is found.")]
        [SerializeField, Min(10f)] private float _fallbackWorldSize = 60f;

        [Tooltip("The inventory screen: the map closes when it opens, and cannot open over it.")]
        [SerializeField] private InventoryScreen _inventoryScreen;

        [Tooltip("Width kept free on the left of the screen for the tactical panel, in pixels.")]
        [SerializeField, Min(0f)] private float _sidePanelWidth = 380f;

        [Tooltip("Empty space around the map, in pixels.")]
        [SerializeField, Min(0f)] private float _padding = 28f;

        [Tooltip("Grid spacing in metres.")]
        [SerializeField, Min(1f)] private float _gridSpacing = 10f;

        [SerializeField] private Color _overlayColor = new Color(0.03f, 0.04f, 0.05f, 0.88f);
        [SerializeField] private Color _groundColor = new Color(0.22f, 0.25f, 0.21f, 1f);
        [SerializeField] private Color _gridColor = new Color(1f, 1f, 1f, 0.07f);
        [SerializeField] private Color _coverColor = new Color(0.55f, 0.56f, 0.52f, 1f);
        [SerializeField] private Color _extractColor = new Color(0.35f, 0.9f, 0.45f, 1f);
        [SerializeField] private Color _playerColor = new Color(0.98f, 0.82f, 0.35f, 1f);
        [SerializeField] private Color _teammateColor = new Color(0.4f, 0.7f, 1f, 1f);
        [SerializeField] private Color _textColor = new Color(0.92f, 0.92f, 0.86f, 1f);

        private readonly List<WorldRect> _obstacles = new List<WorldRect>();
        private readonly List<VisualElement> _obstacleViews = new List<VisualElement>();
        private readonly List<ZoneView> _zoneViews = new List<ZoneView>();
        private readonly List<TeammateView> _teammateViews = new List<TeammateView>();
        private readonly List<TeammateInfo> _teammates = new List<TeammateInfo>();

        private VisualElement _overlay;
        private VisualElement _mapArea;
        private VisualElement _ground;
        private VisualElement _arrow;
        private Label _title;
        private Label _timeLeft;

        private WorldRect _worldBounds;
        private bool _discovered;
        private MapProjection _projection;
        private PlayerCharacter _player;
        private bool _isOpen;

        /// <summary>True while the map is shown.</summary>
        public bool IsOpen => _isOpen;

        /// <summary>Raised with the new state when the map opens or closes.</summary>
        public event Action<bool> OpenChanged;

        private readonly struct WorldRect
        {
            public WorldRect(float centerX, float centerZ, float sizeX, float sizeZ)
            {
                CenterX = centerX;
                CenterZ = centerZ;
                SizeX = sizeX;
                SizeZ = sizeZ;
            }

            public float CenterX { get; }
            public float CenterZ { get; }
            public float SizeX { get; }
            public float SizeZ { get; }
        }

        private sealed class ZoneView
        {
            public VisualElement Area;
            public Label Label;
        }

        private sealed class TeammateView
        {
            public VisualElement Dot;
            public Label Name;
        }

        private void OnEnable()
        {
            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;

            _overlay = MapUi.Box("map-overlay");
            _overlay.pickingMode = PickingMode.Position; // nothing underneath takes clicks while the map is up
            _overlay.style.left = 0f;
            _overlay.style.top = 0f;
            _overlay.style.right = 0f;
            _overlay.style.bottom = 0f;
            _overlay.style.backgroundColor = _overlayColor;
            documentRoot.Add(_overlay);

            VisualElement header = MapUi.Box("map-header");
            header.style.left = _sidePanelWidth + _padding;
            header.style.right = _padding;
            header.style.top = 14f;
            header.style.height = HeaderHeight - 14f;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            _title = MapUi.Text("MAP", 22f, _textColor);
            _timeLeft = MapUi.Text(string.Empty, 20f, _textColor);
            Label hint = MapUi.Text("[M] / [Esc]  close", 13f, new Color(_textColor.r, _textColor.g, _textColor.b, 0.6f), false);
            header.Add(_title);
            header.Add(_timeLeft);
            header.Add(hint);
            _overlay.Add(header);

            _mapArea = MapUi.Box("map-area");
            _mapArea.style.left = _sidePanelWidth;
            _mapArea.style.top = HeaderHeight;
            _mapArea.style.right = 0f;
            _mapArea.style.bottom = 0f;
            _overlay.Add(_mapArea);

            _ground = MapUi.Box("map-ground");
            _ground.style.backgroundColor = _groundColor;
            MapUi.Border(_ground, 1f, new Color(1f, 1f, 1f, 0.35f));
            _ground.generateVisualContent = DrawGrid;
            _mapArea.Add(_ground);

            // Children of the map area, bottom to top: ground, cover (inserted after the ground once read),
            // extraction zones and teammates (added as needed), then the player arrow, always kept in front.
            _arrow = MapUi.Arrow(ArrowSize, _playerColor, new Color(0f, 0f, 0f, 0.8f));
            _mapArea.Add(_arrow);

            MapUi.Show(_overlay, false);
            CursorController.LockChanged += OnCursorLockChanged;
            PlayerCharacter.LocalPlayerChanged += Bind;
            Bind(PlayerCharacter.Local);
        }

        private void OnDisable()
        {
            CursorController.LockChanged -= OnCursorLockChanged;
            PlayerCharacter.LocalPlayerChanged -= Bind;
            Bind(null);
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
            _obstacleViews.Clear();
            _zoneViews.Clear();
            _teammateViews.Clear();
            _discovered = false;
        }

        private void Update()
        {
            if (_player == null) return;

            if (_player.Input.ToggleMapPressed)
            {
                if (_isOpen) Close();
                else Open();
            }
        }

        /// <summary>Shows the map and frees the cursor. Ignored while dead, out of a raid or in another menu.</summary>
        public void Open()
        {
            if (_isOpen || _overlay == null || _player == null || _player.Raid == null || !_player.Health.IsAlive) return;
            // Only from gameplay: not over the inventory, the pause state or any other menu that owns the cursor.
            if (!CursorController.IsLocked || IsInventoryOpen) return;

            if (!_discovered) DiscoverEnvironment();
            _isOpen = true;
            MapUi.Show(_overlay, true);
            _overlay.BringToFront();
            CursorController.SetLocked(false);
            OpenChanged?.Invoke(true);
        }

        /// <summary>Hides the map; gives the cursor back to the game unless another menu took over.</summary>
        public void Close() => Close(relockCursor: true);

        private void Close(bool relockCursor)
        {
            if (!_isOpen) return;

            _isOpen = false; // set first: locking the cursor below raises LockChanged
            MapUi.Show(_overlay, false);
            if (relockCursor && _player != null && _player.Health.IsAlive && !IsInventoryOpen)
                CursorController.SetLocked(true);
            OpenChanged?.Invoke(false);
        }

        private bool IsInventoryOpen => _inventoryScreen != null && _inventoryScreen.IsOpen;

        private void Bind(PlayerCharacter player)
        {
            Close(relockCursor: false);
            _player = player;
        }

        private void OnCursorLockChanged(bool locked)
        {
            // Esc (or anything else) locking the cursor closes the map.
            if (locked && _isOpen) Close(relockCursor: false);
        }

        private void LateUpdate()
        {
            if (!_isOpen) return;

            if (_player == null || !_player.Health.IsAlive || _player.Raid == null)
            {
                Close(relockCursor: false);
                return;
            }
            if (IsInventoryOpen)
            {
                // The inventory took the cursor; leave it unlocked for it.
                Close(relockCursor: false);
                return;
            }

            _timeLeft.text = $"Raid time  {NavigationMath.FormatClock(_player.Raid.SecondsLeft)}";

            Rect area = _mapArea.layout;
            if (area.width <= 0f || area.height <= 0f) return; // first frame after showing: no layout yet

            MapProjection projection = MapProjection.Fit(
                _worldBounds.CenterX - _worldBounds.SizeX * 0.5f, _worldBounds.CenterZ - _worldBounds.SizeZ * 0.5f,
                _worldBounds.SizeX, _worldBounds.SizeZ, area.width, area.height, _padding);
            if (!projection.IsValid) return;

            if (!SameLayout(projection, _projection))
            {
                _projection = projection;
                LayoutStatic();
            }

            Vector3 position = _player.transform.position;
            LayoutZones(position);
            LayoutTeammates();

            _projection.WorldToMap(position.x, position.z, out float playerX, out float playerY);
            MapUi.Move(_arrow, playerX - ArrowSize * 0.5f, playerY - ArrowSize * 0.5f);
            MapUi.SetRotation(_arrow, NavigationMath.BearingFromYaw(_player.Look.Yaw));
        }

        // ---------------- Environment ----------------

        /// <summary>Reads the ground bounds and obstacle footprints from the renderers under the environment root.</summary>
        private void DiscoverEnvironment()
        {
            _discovered = true;
            _obstacles.Clear();
            foreach (VisualElement view in _obstacleViews) view.RemoveFromHierarchy();
            _obstacleViews.Clear();

            bool hasGround = false;
            if (_environmentRoot == null)
            {
                Debug.LogWarning($"[{nameof(MapView)}] No environment root assigned; the map shows an empty {_fallbackWorldSize} m area.", this);
            }
            else
            {
                foreach (Renderer renderer in _environmentRoot.GetComponentsInChildren<Renderer>())
                {
                    if (renderer == null || !renderer.enabled) continue;
                    Bounds bounds = renderer.bounds;
                    var rect = new WorldRect(bounds.center.x, bounds.center.z, bounds.size.x, bounds.size.z);
                    if (!hasGround && renderer.gameObject.name == _groundName)
                    {
                        _worldBounds = rect;
                        hasGround = true;
                    }
                    else if (rect.SizeX > 0.05f && rect.SizeZ > 0.05f)
                    {
                        _obstacles.Add(rect);
                    }
                }
            }

            if (!hasGround) _worldBounds = new WorldRect(0f, 0f, _fallbackWorldSize, _fallbackWorldSize);

            // Right above the ground, below every marker.
            for (int i = 0; i < _obstacles.Count; i++)
            {
                VisualElement block = MapUi.Box("map-cover");
                block.style.backgroundColor = _coverColor;
                MapUi.Border(block, 1f, new Color(0f, 0f, 0f, 0.5f));
                _mapArea.Insert(1 + i, block);
                _obstacleViews.Add(block);
            }

            _projection = default; // force a layout pass
        }

        private static bool SameLayout(in MapProjection a, in MapProjection b) =>
            Mathf.Approximately(a.Scale, b.Scale) && Mathf.Approximately(a.OffsetX, b.OffsetX) &&
            Mathf.Approximately(a.OffsetY, b.OffsetY);

        /// <summary>Positions the ground and obstacles; only needed when the map is resized.</summary>
        private void LayoutStatic()
        {
            MapUi.Place(_ground, _projection.OffsetX, _projection.OffsetY, _projection.MapWidth, _projection.MapHeight);
            _ground.MarkDirtyRepaint();

            for (int i = 0; i < _obstacles.Count && i < _obstacleViews.Count; i++)
            {
                WorldRect rect = _obstacles[i];
                _projection.WorldRectToMap(rect.CenterX, rect.CenterZ, rect.SizeX, rect.SizeZ,
                    out float left, out float top, out float width, out float height);
                MapUi.Place(_obstacleViews[i], left, top, Mathf.Max(1f, width), Mathf.Max(1f, height));
            }
        }

        /// <summary>Grid lines every <see cref="_gridSpacing"/> metres, aligned to world coordinates.</summary>
        private void DrawGrid(MeshGenerationContext context)
        {
            if (!_projection.IsValid) return;

            Painter2D painter = context.painter2D;
            painter.strokeColor = _gridColor;
            painter.lineWidth = 1f;
            painter.BeginPath();

            float minX = _projection.WorldMinX, minZ = _projection.WorldMinZ;
            float maxX = minX + _projection.WorldSizeX, maxZ = minZ + _projection.WorldSizeZ;
            float width = _projection.MapWidth, height = _projection.MapHeight;
            for (float x = Mathf.CeilToInt(minX / _gridSpacing) * _gridSpacing; x <= maxX; x += _gridSpacing)
            {
                float px = (x - minX) * _projection.Scale;
                painter.MoveTo(new Vector2(px, 0f));
                painter.LineTo(new Vector2(px, height));
            }
            for (float z = Mathf.CeilToInt(minZ / _gridSpacing) * _gridSpacing; z <= maxZ; z += _gridSpacing)
            {
                float py = (maxZ - z) * _projection.Scale;
                painter.MoveTo(new Vector2(0f, py));
                painter.LineTo(new Vector2(width, py));
            }
            painter.Stroke();
        }

        // ---------------- Dynamic markers ----------------

        private void LayoutZones(Vector3 playerPosition)
        {
            IReadOnlyList<ExtractionZone> zones = ExtractionZone.All;
            while (_zoneViews.Count < zones.Count) _zoneViews.Add(CreateZoneView());

            for (int i = 0; i < _zoneViews.Count; i++)
            {
                ZoneView view = _zoneViews[i];
                ExtractionZone zone = i < zones.Count ? zones[i] : null;
                MapUi.Show(view.Area, zone != null);
                MapUi.Show(view.Label, zone != null);
                if (zone == null) continue;

                Vector3 center = zone.transform.position;
                Vector3 size = zone.Size;
                _projection.WorldRectToMap(center.x, center.z, size.x, size.z,
                    out float left, out float top, out float width, out float height);
                MapUi.Place(view.Area, left, top, Mathf.Max(4f, width), Mathf.Max(4f, height));

                float distance = NavigationMath.GroundDistance(playerPosition.x, playerPosition.z, center.x, center.z);
                view.Label.text = $"{zone.DisplayName}  {NavigationMath.FormatDistance(distance)}";
                // Label above the zone, or below it when the zone touches the top of the map.
                float labelTop = top - 20f >= 0f ? top - 20f : top + height + 2f;
                MapUi.Move(view.Label, left + width * 0.5f - ZoneLabelWidth * 0.5f, labelTop);
            }
        }

        private ZoneView CreateZoneView()
        {
            var view = new ZoneView
            {
                Area = MapUi.Box("map-extraction"),
                Label = MapUi.CenteredText(string.Empty, 14f, _extractColor, ZoneLabelWidth),
            };
            view.Area.style.backgroundColor = new Color(_extractColor.r, _extractColor.g, _extractColor.b, 0.3f);
            MapUi.Border(view.Area, 2f, _extractColor);
            _mapArea.Add(view.Area);
            _mapArea.Add(view.Label);
            _arrow.BringToFront();
            return view;
        }

        private void LayoutTeammates()
        {
            TacticalSquad.Collect(_player, _teammates);

            int shown = 0;
            foreach (TeammateInfo teammate in _teammates)
            {
                if (teammate.IsLocal) continue; // the arrow
                if (shown == _teammateViews.Count) _teammateViews.Add(CreateTeammateView());
                TeammateView view = _teammateViews[shown++];
                MapUi.Show(view.Dot, true);
                MapUi.Show(view.Name, true);
                _projection.WorldToMap(teammate.WorldX, teammate.WorldZ, out float x, out float y);
                MapUi.Move(view.Dot, x - TeammateDotSize * 0.5f, y - TeammateDotSize * 0.5f);
                view.Dot.style.opacity = teammate.IsAlive ? 1f : 0.4f;
                view.Name.text = teammate.Name;
                MapUi.Move(view.Name, x - 60f, y + TeammateDotSize * 0.5f + 1f);
            }
            for (int i = shown; i < _teammateViews.Count; i++)
            {
                MapUi.Show(_teammateViews[i].Dot, false);
                MapUi.Show(_teammateViews[i].Name, false);
            }
        }

        private TeammateView CreateTeammateView()
        {
            var view = new TeammateView
            {
                Dot = MapUi.Box("map-teammate"),
                Name = MapUi.CenteredText(string.Empty, 12f, _teammateColor, 120f),
            };
            MapUi.Place(view.Dot, 0f, 0f, TeammateDotSize, TeammateDotSize);
            view.Dot.style.backgroundColor = _teammateColor;
            MapUi.Radius(view.Dot, TeammateDotSize * 0.5f);
            MapUi.Border(view.Dot, 1f, Color.black);
            _mapArea.Add(view.Dot);
            _mapArea.Add(view.Name);
            _arrow.BringToFront();
            return view;
        }
    }
}
