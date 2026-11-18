using System.Collections.Generic;
using ExtractionShooter.Core.Input;
using ExtractionShooter.Core.Navigation;
using ExtractionShooter.Core.Raid;
using ExtractionShooter.Core.Squad;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Raid;
using ExtractionShooter.Gameplay.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// Top-centre compass strip, under the raid timer: a tick every 15 degrees (letters on the eight compass points,
    /// small numbers between), scrolling with the local player's yaw, the bearing being faced as three digits, and a
    /// marker with the distance for every extraction point (pinned to the strip's edge when it is behind you; faded
    /// while the point is closed or unaffordable), and a hint under the heading when the point you stand in won't let
    /// you out yet ("Exit closed: opens in 03:12"), plus the squad's pings in their ping colour. Shown while the
    /// player is alive, in a raid and in control. Built in code.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CompassView : MonoBehaviour
    {
        private const int TickStep = 15;
        private const float LabelWidth = 44f;
        private const float TickRowHeight = 30f;
        private const float MarkerRowTop = 32f;
        private const float MarkerSize = 9f;

        [Tooltip("Distance from the top of the screen, in pixels. The raid timer sits above (18 px + its height).")]
        [SerializeField, Min(0f)] private float _top = 66f;

        [Tooltip("Width of the strip in pixels.")]
        [SerializeField, Min(120f)] private float _width = 540f;

        [Tooltip("How many degrees of heading the strip spans, edge to edge.")]
        [SerializeField, Range(60f, 300f)] private float _visibleDegrees = 150f;

        [SerializeField] private Color _color = new Color(0.92f, 0.92f, 0.86f, 0.95f);
        [SerializeField] private Color _dimColor = new Color(0.92f, 0.92f, 0.86f, 0.55f);
        [SerializeField] private Color _northColor = new Color(0.95f, 0.75f, 0.35f, 1f);
        [SerializeField] private Color _extractColor = new Color(0.35f, 0.9f, 0.45f, 1f);

        [Tooltip("Hint text when the extraction point you stand in is closed or unaffordable.")]
        [SerializeField] private Color _warningColor = new Color(0.95f, 0.35f, 0.3f, 1f);

        [SerializeField] private Color _background = new Color(0.04f, 0.05f, 0.06f, 0.45f);

        private readonly List<Tick> _ticks = new List<Tick>();
        private readonly List<Marker> _markers = new List<Marker>();
        private readonly List<Marker> _pingMarkers = new List<Marker>();

        private VisualElement _root;
        private VisualElement _strip;
        private Label _heading;
        private Label _hint;

        private struct Tick
        {
            public int Degrees;
            public VisualElement Line;
            public Label Label;
            public float LineWidth;
        }

        private sealed class Marker
        {
            public VisualElement Diamond;
            public Label Distance;
        }

        private float StripHeight => MarkerRowTop + MarkerSize + 18f;

        private void OnEnable()
        {
            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;

            _root = MapUi.Box("compass");
            _root.style.top = _top;
            _root.style.left = 0f;
            _root.style.right = 0f;
            _root.style.alignItems = Align.Center;
            documentRoot.Add(_root);

            _strip = new VisualElement { name = "compass-strip", pickingMode = PickingMode.Ignore };
            _strip.style.width = _width;
            _strip.style.height = StripHeight;
            _strip.style.overflow = Overflow.Hidden;
            _strip.style.backgroundColor = _background;
            MapUi.Radius(_strip, 3f);
            _root.Add(_strip);

            for (int degrees = 0; degrees < 360; degrees += TickStep)
            {
                string cardinal = NavigationMath.CardinalLabelAt(degrees);
                bool major = cardinal != null;
                bool north = degrees == 0;
                float lineWidth = major ? 2f : 1f;
                VisualElement line = MapUi.Box("compass-tick");
                line.style.width = lineWidth;
                line.style.height = major ? 9f : 5f;
                line.style.top = 0f;
                line.style.backgroundColor = north ? _northColor : major ? _color : _dimColor;
                Label label = MapUi.CenteredText(major ? cardinal : degrees.ToString(), major ? 15f : 11f,
                    north ? _northColor : major ? _color : _dimColor, LabelWidth, major);
                label.style.top = major ? 10f : 11f;
                _strip.Add(line);
                _strip.Add(label);
                _ticks.Add(new Tick { Degrees = degrees, Line = line, Label = label, LineWidth = lineWidth });
            }

            // Centre notch: where you are facing.
            VisualElement notch = MapUi.Box("compass-notch");
            notch.style.width = 2f;
            notch.style.height = TickRowHeight;
            notch.style.left = (_width - 2f) * 0.5f;
            notch.style.top = 0f;
            notch.style.backgroundColor = _northColor;
            _strip.Add(notch);

            _heading = MapUi.CenteredText("000", 16f, _color, 64f);
            _heading.style.position = Position.Relative;
            _heading.style.marginTop = 2f;
            _heading.style.backgroundColor = _background;
            MapUi.Radius(_heading, 3f);
            _root.Add(_heading);

            _hint = MapUi.CenteredText(string.Empty, 14f, _warningColor, _width);
            _hint.style.position = Position.Relative;
            _hint.style.marginTop = 4f;
            _root.Add(_hint);
            MapUi.Show(_hint, false);

            MapUi.Show(_root, false);
        }

        private void OnDisable()
        {
            _root?.RemoveFromHierarchy();
            _root = null;
            _ticks.Clear();
            _markers.Clear();
            _pingMarkers.Clear();
        }

        private void LateUpdate()
        {
            if (_root == null) return;

            PlayerCharacter player = PlayerCharacter.Local;
            bool visible = player != null && player.Raid != null && player.Health.IsAlive && CursorController.IsLocked;
            MapUi.Show(_root, visible);
            if (!visible) return;

            float heading = NavigationMath.BearingFromYaw(player.Look.Yaw);
            float pixelsPerDegree = _width / _visibleDegrees;
            float half = _width * 0.5f;
            float visibleHalf = _visibleDegrees * 0.5f + TickStep;

            foreach (Tick tick in _ticks)
            {
                float delta = NavigationMath.DeltaAngle(heading, tick.Degrees);
                bool onStrip = Mathf.Abs(delta) <= visibleHalf;
                MapUi.Show(tick.Line, onStrip);
                MapUi.Show(tick.Label, onStrip);
                if (!onStrip) continue;

                float x = half + delta * pixelsPerDegree;
                tick.Line.style.left = x - tick.LineWidth * 0.5f;
                tick.Label.style.left = x - LabelWidth * 0.5f;
                // Fade towards the edges so the strip reads as a drum.
                float fade = 1f - Mathf.Clamp01((Mathf.Abs(delta) - _visibleDegrees * 0.3f) / (_visibleDegrees * 0.2f));
                tick.Label.style.opacity = fade;
                tick.Line.style.opacity = fade;
            }

            _heading.text = NavigationMath.FormatBearing(heading);
            UpdateMarkers(player, heading, pixelsPerDegree, half);
            UpdatePingMarkers(player.transform.position, heading, pixelsPerDegree, half);
        }

        private void UpdateMarkers(PlayerCharacter player, float heading, float pixelsPerDegree, float half)
        {
            IReadOnlyList<ExtractionZone> zones = ExtractionZone.All;
            while (_markers.Count < zones.Count) _markers.Add(CreateMarker());

            Vector3 position = player.transform.position;
            float edge = half - MarkerSize;
            int secondsLeft = player.Raid.SecondsLeft;
            long credits = LocalCredits();
            string blocked = null;
            for (int i = 0; i < _markers.Count; i++)
            {
                Marker marker = _markers[i];
                bool used = i < zones.Count && zones[i] != null;
                MapUi.Show(marker.Diamond, used);
                MapUi.Show(marker.Distance, used);
                if (!used) continue;

                ExtractionStatus status = zones[i].StatusFor(secondsLeft, credits);
                if (blocked == null && !status.CanExtract && zones[i].Contains(position))
                    blocked = $"{zones[i].DisplayName}  ·  {status.BlockedReason}";
                float strength = status.CanExtract ? 1f : 0.35f;

                Vector3 target = zones[i].transform.position;
                float bearing = NavigationMath.BearingTo(position.x, position.z, target.x, target.z);
                float offset = NavigationMath.StripOffset(heading, bearing, pixelsPerDegree);
                bool pinned = Mathf.Abs(offset) > edge;
                float x = half + Mathf.Clamp(offset, -edge, edge);

                MapUi.Move(marker.Diamond, x - MarkerSize * 0.5f, MarkerRowTop);
                marker.Diamond.style.opacity = (pinned ? 0.5f : 1f) * strength;
                marker.Distance.text = NavigationMath.FormatDistance(
                    NavigationMath.GroundDistance(position.x, position.z, target.x, target.z));
                // Keep the text inside the strip at the edges.
                float labelLeft = Mathf.Clamp(x - LabelWidth * 0.5f, 0f, _width - LabelWidth);
                MapUi.Move(marker.Distance, labelLeft, MarkerRowTop + MarkerSize + 1f);
                marker.Distance.style.opacity = (pinned ? 0.5f : 1f) * strength;
            }

            MapUi.Show(_hint, blocked != null);
            if (blocked != null) _hint.text = blocked;
        }

        /// <summary>Squad pings (<see cref="SquadPings"/>) on the marker row, in their ping colour, with the distance.</summary>
        private void UpdatePingMarkers(Vector3 position, float heading, float pixelsPerDegree, float half)
        {
            SquadPings.Prune();
            IReadOnlyList<SquadPing> pings = SquadPings.Board.Active;
            while (_pingMarkers.Count < pings.Count) _pingMarkers.Add(CreateMarker());

            float edge = half - MarkerSize;
            for (int i = 0; i < _pingMarkers.Count; i++)
            {
                Marker marker = _pingMarkers[i];
                bool used = i < pings.Count;
                MapUi.Show(marker.Diamond, used);
                MapUi.Show(marker.Distance, used);
                if (!used) continue;

                SquadPing ping = pings[i];
                Color color = PingStyle.ColorOf(ping.Kind);
                marker.Diamond.style.backgroundColor = color;
                marker.Distance.style.color = color;

                float bearing = NavigationMath.BearingTo(position.x, position.z, ping.X, ping.Z);
                float offset = NavigationMath.StripOffset(heading, bearing, pixelsPerDegree);
                bool pinned = Mathf.Abs(offset) > edge;
                float x = half + Mathf.Clamp(offset, -edge, edge);
                MapUi.Move(marker.Diamond, x - MarkerSize * 0.5f, MarkerRowTop);
                marker.Diamond.style.opacity = pinned ? 0.5f : 1f;
                marker.Distance.text = NavigationMath.FormatDistance(
                    NavigationMath.GroundDistance(position.x, position.z, ping.X, ping.Z));
                float labelLeft = Mathf.Clamp(x - LabelWidth * 0.5f, 0f, _width - LabelWidth);
                MapUi.Move(marker.Distance, labelLeft, MarkerRowTop + MarkerSize + 1f);
                marker.Distance.style.opacity = pinned ? 0.5f : 1f;
            }
        }

        /// <summary>The local player's credits (paid extractions); 0 before the session is known.</summary>
        private static long LocalCredits()
        {
            PlayerSession session = PlayerSession.Local;
            return session != null && session.Trading != null ? session.Trading.Money : 0L;
        }

        private Marker CreateMarker()
        {
            var marker = new Marker
            {
                Diamond = MapUi.Diamond(MarkerSize, _extractColor, new Color(0f, 0f, 0f, 0.6f)),
                Distance = MapUi.CenteredText(string.Empty, 11f, _extractColor, LabelWidth),
            };
            _strip.Add(marker.Diamond);
            _strip.Add(marker.Distance);
            return marker;
        }
    }
}
