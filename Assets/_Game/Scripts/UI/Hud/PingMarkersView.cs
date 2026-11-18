using System.Collections.Generic;
using ExtractionShooter.Core.Navigation;
using ExtractionShooter.Core.Squad;
using ExtractionShooter.Gameplay.Audio;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// The squad's pings in the world (<see cref="SquadPings"/>): a diamond in the ping's colour (<see cref="PingStyle"/>)
    /// with its word and distance above ("Loot · 34 m") and who placed it below. Off-screen or behind the camera the
    /// marker slides to the screen edge with an arrow pointing the way. Markers pop in, shrink a little with distance
    /// and fade out in their last second. Plays a short blip when a ping arrives (a lower double beep for danger).
    /// Shown while the local player is alive, in a raid and in control. Built in code on the HUD document.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PingMarkersView : MonoBehaviour
    {
        private const float MarkerWidth = 170f;
        private const float LabelHeight = 17f;
        private const float IconSize = 14f;
        private const float IconTop = LabelHeight + 4f;
        private const float OwnerTop = IconTop + IconSize + 3f;
        private const float MarkerHeight = OwnerTop + 14f;
        private const float ArrowSize = 14f;

        [Tooltip("Document the markers are added to (the game's HUD document).")]
        [SerializeField] private UIDocument _document;

        [Tooltip("Gap kept between an edge-pinned marker's centre and the screen's left/right edges (pixels).")]
        [SerializeField, Min(0f)] private float _edgeMarginX = 90f;

        [Tooltip("Gap kept between an edge-pinned marker's centre and the screen's top/bottom edges (pixels).")]
        [SerializeField, Min(0f)] private float _edgeMarginY = 60f;

        [Tooltip("Markers start shrinking beyond this distance (metres)...")]
        [SerializeField, Min(0f)] private float _shrinkStart = 15f;

        [Tooltip("...and reach the minimum scale here (metres).")]
        [SerializeField, Min(1f)] private float _shrinkEnd = 200f;

        [SerializeField, Range(0.2f, 1f)] private float _minScale = 0.75f;

        [Tooltip("Seconds a marker takes to fade out at the end of its life.")]
        [SerializeField, Min(0f)] private float _fadeSeconds = 1f;

        [Tooltip("Seconds of the pop-in when a ping appears.")]
        [SerializeField, Min(0f)] private float _popSeconds = 0.25f;

        [Tooltip("Scale a marker pops in at.")]
        [SerializeField, Range(1f, 3f)] private float _popScale = 1.6f;

        [Tooltip("Opacity of edge-pinned (off-screen) markers.")]
        [SerializeField, Range(0f, 1f)] private float _pinnedOpacity = 0.8f;

        [Tooltip("Volume of the ping blip (0 mutes it).")]
        [SerializeField, Range(0f, 1f)] private float _volume = 0.5f;

        [SerializeField] private Color _ownerColor = new Color(0.92f, 0.92f, 0.86f, 0.8f);

        private readonly List<Marker> _markers = new List<Marker>();

        private VisualElement _layer;
        private Camera _camera;
        private AudioSource _audio;

        private sealed class Marker
        {
            public VisualElement Root;
            public VisualElement Icon;
            public Label Glyph;
            public VisualElement Arrow;
            public Label Label;
            public Label Owner;
            public int ShownId = -1;
            public PingKind ShownKind;
            public int ShownMeters = -1;
            public bool ShownPinned;
            public float ShownArrow = float.NaN;
        }

        private void OnEnable()
        {
            if (_document == null)
            {
                Debug.LogError($"[{nameof(PingMarkersView)}] Document is not assigned on {name}.", this);
                enabled = false;
                return;
            }
            TryBuild();
            SquadPings.Received += OnPingReceived;
        }

        /// <summary>Builds the layer once the document has its root (it may enable after this component).</summary>
        private bool TryBuild()
        {
            if (_layer != null) return true;
            if (_document.rootVisualElement == null) return false;

            _layer = WorldMarkers.CreateLayer(_document.rootVisualElement, "ping-markers");
            MapUi.Show(_layer, false);
            return true;
        }

        private void OnDisable()
        {
            SquadPings.Received -= OnPingReceived;
            _layer?.RemoveFromHierarchy();
            _layer = null;
            _markers.Clear();
        }

        private void LateUpdate()
        {
            if (!TryBuild()) return;

            PlayerCharacter local = PlayerCharacter.Local;
            _camera = WorldMarkers.ResolveCamera(_camera);
            SquadPings.Prune();
            IReadOnlyList<SquadPing> pings = SquadPings.Board.Active;
            bool visible = pings.Count > 0 && _camera != null && WorldMarkers.HudVisible(local);
            MapUi.Show(_layer, visible);
            if (!visible || !WorldMarkers.TryGetSize(_layer, out float width, out float height)) return;

            double now = Time.timeAsDouble;
            Vector3 origin = local.transform.position;
            while (_markers.Count < pings.Count) _markers.Add(CreateMarker());

            for (int i = 0; i < _markers.Count; i++)
            {
                Marker marker = _markers[i];
                bool used = i < pings.Count;
                MapUi.Show(marker.Root, used);
                if (used) Layout(marker, pings[i], origin, now, width, height);
            }
        }

        private void Layout(Marker marker, in SquadPing ping, Vector3 origin, double now, float width, float height)
        {
            Vector3 position = SquadPings.PositionOf(ping);
            ScreenMarker placed = WorldMarkers.Project(_camera, position, width, height, _edgeMarginX, _edgeMarginY);
            float distance = Vector3.Distance(origin, position);
            int meters = Mathf.RoundToInt(distance);

            if (ping.Id != marker.ShownId || ping.Kind != marker.ShownKind)
            {
                marker.ShownId = ping.Id;
                marker.ShownKind = ping.Kind;
                marker.ShownMeters = -1;
                Color color = PingStyle.ColorOf(ping.Kind);
                marker.Icon.style.backgroundColor = color;
                marker.Label.style.color = color;
                ReplaceArrow(marker, color);
                marker.Glyph.text = ping.Kind == PingKind.Danger ? "!" : string.Empty;
                marker.Owner.text = ping.OwnerName;
            }
            if (meters != marker.ShownMeters)
            {
                marker.ShownMeters = meters;
                marker.Label.text = $"{PingRules.Label(ping.Kind)}  ·  {NavigationMath.FormatDistance(distance)}";
            }

            bool pinned = !placed.OnScreen;
            if (pinned != marker.ShownPinned)
            {
                marker.ShownPinned = pinned;
                MapUi.Show(marker.Arrow, pinned);
                MapUi.Show(marker.Owner, !pinned);
            }
            if (pinned && !Mathf.Approximately(placed.ArrowDegrees, marker.ShownArrow))
            {
                // The arrow sits just outside the diamond on the side the ping is, pointing at it.
                marker.ShownArrow = placed.ArrowDegrees;
                float radians = placed.ArrowDegrees * Mathf.Deg2Rad;
                float reach = IconSize * 0.5f + ArrowSize * 0.75f;
                float centerX = MarkerWidth * 0.5f + Mathf.Sin(radians) * reach;
                float centerY = IconTop + IconSize * 0.5f - Mathf.Cos(radians) * reach;
                MapUi.Move(marker.Arrow, centerX - ArrowSize * 0.5f, centerY - ArrowSize * 0.5f);
                MapUi.SetRotation(marker.Arrow, placed.ArrowDegrees);
            }

            MapUi.Move(marker.Root, placed.X - MarkerWidth * 0.5f, placed.Y - (IconTop + IconSize * 0.5f));
            double age = now - ping.PlacedAt;
            float scale = ScreenMarkerMath.DistanceScale(distance, _shrinkStart, _shrinkEnd, _minScale)
                        * ScreenMarkerMath.PopScale(age, _popSeconds, _popScale);
            WorldMarkers.SetScale(marker.Root, scale);
            marker.Root.style.opacity = ScreenMarkerMath.ExpiryOpacity(ping.SecondsLeft(now), _fadeSeconds) * (pinned ? _pinnedOpacity : 1f);
        }

        private Marker CreateMarker()
        {
            var marker = new Marker { Root = MapUi.Box("ping-marker") };
            MapUi.Place(marker.Root, 0f, 0f, MarkerWidth, MarkerHeight);

            marker.Label = MapUi.CenteredText(string.Empty, 13f, PingStyle.Location, MarkerWidth);
            marker.Label.style.top = 0f;
            marker.Label.style.height = LabelHeight;
            Outline(marker.Label);
            marker.Root.Add(marker.Label);

            marker.Icon = MapUi.Diamond(IconSize, PingStyle.Location, PingStyle.Outline);
            MapUi.Move(marker.Icon, (MarkerWidth - IconSize) * 0.5f, IconTop);
            marker.Root.Add(marker.Icon);

            // Danger's "!" sits upright over the (rotated) diamond.
            marker.Glyph = MapUi.CenteredText(string.Empty, 12f, Color.white, MarkerWidth);
            marker.Glyph.style.top = IconTop;
            marker.Glyph.style.height = IconSize;
            Outline(marker.Glyph);
            marker.Root.Add(marker.Glyph);

            ReplaceArrow(marker, PingStyle.Location);

            marker.Owner = MapUi.CenteredText(string.Empty, 11f, _ownerColor, MarkerWidth, false);
            marker.Owner.style.top = OwnerTop;
            Outline(marker.Owner);
            marker.Root.Add(marker.Owner);

            _layer.Add(marker.Root);
            return marker;
        }

        /// <summary>The edge arrow draws its colour when built, so a marker reused for another kind gets a new one.</summary>
        private static void ReplaceArrow(Marker marker, Color color)
        {
            marker.Arrow?.RemoveFromHierarchy();
            marker.Arrow = MapUi.Arrow(ArrowSize, color, PingStyle.Outline);
            marker.Root.Add(marker.Arrow);
            MapUi.Show(marker.Arrow, marker.ShownPinned);
            marker.ShownArrow = float.NaN;
        }

        private void OnPingReceived(SquadPing ping)
        {
            if (_volume <= 0f || PlayerCharacter.Local == null) return;
            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 0f; // a UI sound: same volume wherever the ping is
            }
            _audio.PlayOneShot(ProceduralAudio.PingBlip(ping.Kind == PingKind.Danger), _volume);
        }

        private static void Outline(Label label)
        {
            label.style.unityTextOutlineWidth = 0.6f;
            label.style.unityTextOutlineColor = new Color(0f, 0f, 0f, 0.85f);
        }
    }
}
