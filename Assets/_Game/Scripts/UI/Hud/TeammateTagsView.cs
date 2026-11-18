using System.Collections.Generic;
using ExtractionShooter.Core.Squad;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// Name tags over squadmates' heads: the player's name and a small health bar (red when low), or the name greyed
    /// with "KIA" once they are down. Only squadmates (from the replicated <see cref="SquadMember"/> tags), never
    /// enemies or yourself. Tags shrink and fade with distance, vanish beyond the max distance, and hide when the
    /// teammate is off-screen or behind the camera (the compass and map still show them). Shown while the local player
    /// is alive, in a raid and in control. Built in code on the HUD document, behind the rest of the HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TeammateTagsView : MonoBehaviour
    {
        private const float TagWidth = 140f;
        private const float NameHeight = 16f;
        private const float BarWidth = 56f;
        private const float BarHeight = 4f;
        private const float TagHeight = NameHeight + 3f + BarHeight;

        [Tooltip("Document the tags are added to (the game's HUD document).")]
        [SerializeField] private UIDocument _document;

        [Tooltip("Height of the tag above the teammate's eyes (metres).")]
        [SerializeField, Min(0f)] private float _headOffset = 0.5f;

        [Tooltip("Tags start shrinking beyond this distance (metres)...")]
        [SerializeField, Min(0f)] private float _shrinkStart = 10f;

        [Tooltip("...and reach the minimum scale here (metres).")]
        [SerializeField, Min(1f)] private float _shrinkEnd = 120f;

        [SerializeField, Range(0.2f, 1f)] private float _minScale = 0.65f;

        [Tooltip("Tags start fading beyond this distance (metres).")]
        [SerializeField, Min(0f)] private float _fadeStart = 60f;

        [Tooltip("No tag beyond this distance (metres); it reaches the minimum opacity just before.")]
        [SerializeField, Min(1f)] private float _maxDistance = 400f;

        [SerializeField, Range(0f, 1f)] private float _minOpacity = 0.45f;

        [Tooltip("Health fraction below which the bar turns red.")]
        [SerializeField, Range(0f, 1f)] private float _lowHealth = 0.3f;

        [SerializeField] private Color _nameColor = new Color(0.4f, 0.7f, 1f, 1f);
        [SerializeField] private Color _healthColor = new Color(0.45f, 0.85f, 0.4f, 1f);
        [SerializeField] private Color _lowHealthColor = new Color(0.9f, 0.3f, 0.25f, 1f);
        [SerializeField] private Color _deadColor = new Color(0.62f, 0.62f, 0.62f, 0.85f);
        [SerializeField] private Color _barBackground = new Color(0f, 0f, 0f, 0.55f);

        private readonly List<Tag> _tags = new List<Tag>();

        private VisualElement _layer;
        private Camera _camera;

        private sealed class Tag
        {
            public VisualElement Root;
            public Label Name;
            public VisualElement Bar;
            public VisualElement Fill;
            public string ShownName;
            public bool ShownAlive = true;
            public float ShownHealth = -1f;
        }

        private void OnEnable()
        {
            if (_document == null)
            {
                Debug.LogError($"[{nameof(TeammateTagsView)}] Document is not assigned on {name}.", this);
                enabled = false;
                return;
            }
            TryBuild();
        }

        /// <summary>Builds the layer once the document has its root (it may enable after this component).</summary>
        private bool TryBuild()
        {
            if (_layer != null) return true;
            if (_document.rootVisualElement == null) return false;

            _layer = WorldMarkers.CreateLayer(_document.rootVisualElement, "teammate-tags");
            MapUi.Show(_layer, false);
            return true;
        }

        private void OnDisable()
        {
            _layer?.RemoveFromHierarchy();
            _layer = null;
            _tags.Clear();
        }

        private void LateUpdate()
        {
            if (!TryBuild()) return;

            PlayerCharacter local = PlayerCharacter.Local;
            _camera = WorldMarkers.ResolveCamera(_camera);
            SquadMember localMember = local != null ? local.GetComponent<SquadMember>() : null;
            bool visible = localMember != null && _camera != null && WorldMarkers.HudVisible(local);
            MapUi.Show(_layer, visible);
            if (!visible || !WorldMarkers.TryGetSize(_layer, out float width, out float height)) return;

            Vector3 eye = _camera.transform.position;
            int shown = 0;
            foreach (SquadMember member in SquadMember.ClientAllMembers)
            {
                if (member == null || !localMember.IsSquadmate(member)) continue;
                PlayerCharacter teammate = member.GetComponent<PlayerCharacter>();
                if (teammate == null) continue;

                Vector3 head = teammate.Look.EyePosition + Vector3.up * _headOffset;
                float distance = Vector3.Distance(eye, head);
                if (distance > _maxDistance) continue;

                ScreenMarker marker = WorldMarkers.Project(_camera, head, width, height, 0f, 0f);
                if (!marker.OnScreen) continue;

                if (shown == _tags.Count) _tags.Add(CreateTag());
                Tag tag = _tags[shown++];
                MapUi.Show(tag.Root, true);
                Refresh(tag, member, teammate);

                MapUi.Move(tag.Root, marker.X - TagWidth * 0.5f, marker.Y - TagHeight);
                WorldMarkers.SetScale(tag.Root, ScreenMarkerMath.DistanceScale(distance, _shrinkStart, _shrinkEnd, _minScale));
                tag.Root.style.opacity = ScreenMarkerMath.DistanceOpacity(distance, _fadeStart, _maxDistance, _minOpacity);
            }

            for (int i = shown; i < _tags.Count; i++) MapUi.Show(_tags[i].Root, false);
        }

        /// <summary>Updates the name and bar, touching the styles only when something changed.</summary>
        private void Refresh(Tag tag, SquadMember member, PlayerCharacter teammate)
        {
            bool alive = teammate.Health.IsAlive;
            string displayName = !string.IsNullOrEmpty(member.PlayerName) ? member.PlayerName : "Teammate";
            float health01 = alive && teammate.Health.Max > 0 ? Mathf.Clamp01((float)teammate.Health.Current / teammate.Health.Max) : 0f;

            if (displayName != tag.ShownName || alive != tag.ShownAlive)
            {
                tag.ShownName = displayName;
                tag.ShownAlive = alive;
                tag.Name.text = alive ? displayName : $"{displayName}  KIA";
                tag.Name.style.color = alive ? _nameColor : _deadColor;
                MapUi.Show(tag.Bar, alive);
                tag.ShownHealth = -1f;
            }

            if (alive && !Mathf.Approximately(health01, tag.ShownHealth))
            {
                tag.ShownHealth = health01;
                tag.Fill.style.width = (BarWidth - 2f) * health01;
                tag.Fill.style.backgroundColor = health01 < _lowHealth ? _lowHealthColor : _healthColor;
            }
        }

        private Tag CreateTag()
        {
            var tag = new Tag { Root = MapUi.Box("teammate-tag") };
            MapUi.Place(tag.Root, 0f, 0f, TagWidth, TagHeight);

            tag.Name = MapUi.CenteredText(string.Empty, 13f, _nameColor, TagWidth);
            tag.Name.style.top = 0f;
            tag.Name.style.height = NameHeight;
            TextShadow(tag.Name);
            tag.Root.Add(tag.Name);

            tag.Bar = MapUi.Box("teammate-tag-bar");
            MapUi.Place(tag.Bar, (TagWidth - BarWidth) * 0.5f, NameHeight + 3f, BarWidth, BarHeight);
            tag.Bar.style.backgroundColor = _barBackground;
            MapUi.Border(tag.Bar, 1f, new Color(0f, 0f, 0f, 0.6f));
            tag.Root.Add(tag.Bar);

            tag.Fill = MapUi.Box("teammate-tag-fill");
            MapUi.Place(tag.Fill, 0f, 0f, BarWidth - 2f, BarHeight - 2f);
            tag.Fill.style.backgroundColor = _healthColor;
            tag.Bar.Add(tag.Fill);

            _layer.Add(tag.Root);
            return tag;
        }

        /// <summary>A dark outline so names read against bright sky.</summary>
        private static void TextShadow(Label label)
        {
            label.style.unityTextOutlineWidth = 0.6f;
            label.style.unityTextOutlineColor = new Color(0f, 0f, 0f, 0.85f);
        }
    }
}
