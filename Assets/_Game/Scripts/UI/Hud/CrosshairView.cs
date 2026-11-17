using ExtractionShooter.Core.Input;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Weapons;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// The aiming reticle in the middle of the screen, drawn on the HUD's UI document.
    /// <list type="bullet">
    /// <item>Hip fire: four lines whose gap is the real shot cone (<see cref="WeaponAim.CurrentSpread"/>): it widens
    /// when moving and shows where shots can land. It also blooms a little with the current recoil kick.</item>
    /// <item>Aiming down sights: the lines fade into a single dot as the sights come up.</item>
    /// <item>Scoped weapons, fully aimed: a scope ring with fine crosshairs covers the screen.</item>
    /// </list>
    /// Hidden whenever the player is not in control (menus, inventory, dead, no weapon drawn).
    /// Built in code, so it needs no extra UXML.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CrosshairView : MonoBehaviour
    {
        [SerializeField] private Color _color = new Color(0.95f, 0.95f, 0.9f, 0.9f);
        [SerializeField] private Color _outline = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField, Min(1f)] private float _lineLength = 14f;
        [SerializeField, Min(1f)] private float _lineThickness = 3f;
        [SerializeField, Min(0f)] private float _minGap = 4f;
        [SerializeField, Min(1f)] private float _dotSize = 5f;

        [Tooltip("Extra gap per degree of current recoil offset, in degrees of cone (visual only).")]
        [SerializeField, Range(0f, 1f)] private float _recoilBloom = 0.35f;

        [Tooltip("Scope view radius as a share of the screen height.")]
        [SerializeField, Range(0.2f, 0.6f)] private float _scopeRadius = 0.42f;

        private VisualElement _root;
        private VisualElement _hip;
        private VisualElement _top, _bottom, _left, _right, _dot;
        private VisualElement _scope;
        private VisualElement _scopeRing, _scopeHorizontal, _scopeVertical;

        private void OnEnable()
        {
            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;
            _root = Box("crosshair");
            _root.style.left = 0f;
            _root.style.top = 0f;
            _root.style.right = 0f;
            _root.style.bottom = 0f;
            documentRoot.Add(_root);

            _scope = Box("crosshair-scope");
            _scope.style.left = 0f;
            _scope.style.top = 0f;
            _scope.style.right = 0f;
            _scope.style.bottom = 0f;
            _scopeRing = Box("crosshair-scope-ring");
            _scopeHorizontal = Line(Color.black, null);
            _scopeVertical = Line(Color.black, null);
            _scope.Add(_scopeRing);
            _scope.Add(_scopeHorizontal);
            _scope.Add(_scopeVertical);
            _root.Add(_scope);

            _hip = Box("crosshair-hip");
            _hip.style.left = 0f;
            _hip.style.top = 0f;
            _hip.style.right = 0f;
            _hip.style.bottom = 0f;
            _top = Line(_color, _outline);
            _bottom = Line(_color, _outline);
            _left = Line(_color, _outline);
            _right = Line(_color, _outline);
            _dot = Line(_color, _outline);
            foreach (VisualElement part in new[] { _top, _bottom, _left, _right, _dot }) _hip.Add(part);
            _root.Add(_hip);

            Show(_root, false);
        }

        private void OnDisable()
        {
            _root?.RemoveFromHierarchy();
            _root = null;
        }

        private void LateUpdate()
        {
            if (_root == null) return;

            PlayerCharacter player = PlayerCharacter.Local;
            WeaponAim aim = player != null ? player.GetComponent<WeaponAim>() : null;
            bool hasWeapon = player != null && player.Equipment != null &&
                             player.Equipment.TryGetActiveWeapon(out EquipmentSlot _, out WeaponDefinition _, out _);
            bool visible = aim != null && hasWeapon && player.Health.IsAlive && CursorController.IsLocked;
            Show(_root, visible);
            if (!visible) return;

            float width = _root.layout.width, height = _root.layout.height;
            if (width <= 0f || height <= 0f) return;
            float centerX = width * 0.5f, centerY = height * 0.5f;

            player.Equipment.TryGetActiveWeapon(out EquipmentSlot _, out WeaponDefinition _, out var item);
            player.Equipment.TryGetActiveWeaponStats(out WeaponStats stats);
            player.Equipment.TryGetAmmo(item.AmmoId, out AmmoDefinition ammo);

            bool scoped = stats.Scoped && aim.Aim01 >= 0.95f;
            Show(_scope, scoped);
            Show(_hip, !scoped);
            if (scoped)
            {
                LayoutScope(width, height, centerX, centerY);
                return;
            }

            // Cone half-angle to pixels, with the same projection as the camera.
            float spread = aim.CurrentSpread(stats, ammo) + player.Look.RecoilOffset.Magnitude * _recoilBloom;
            float fov = Mathf.Max(1f, player.Look.FieldOfView);
            float gap = Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * centerY;
            gap = Mathf.Max(_minGap, gap);

            // Lines fade out as the sights come up; the dot stays.
            float lines = 1f - aim.Aim01;
            Place(_top, centerX - _lineThickness * 0.5f, centerY - gap - _lineLength, _lineThickness, _lineLength, lines);
            Place(_bottom, centerX - _lineThickness * 0.5f, centerY + gap, _lineThickness, _lineLength, lines);
            Place(_left, centerX - gap - _lineLength, centerY - _lineThickness * 0.5f, _lineLength, _lineThickness, lines);
            Place(_right, centerX + gap, centerY - _lineThickness * 0.5f, _lineLength, _lineThickness, lines);
            Place(_dot, centerX - _dotSize * 0.5f, centerY - _dotSize * 0.5f, _dotSize, _dotSize, 1f);
        }

        /// <summary>A clear circle seen through a black ring whose border is wide enough to cover the whole screen.</summary>
        private void LayoutScope(float width, float height, float centerX, float centerY)
        {
            float radius = height * _scopeRadius;
            float border = Mathf.Max(width, height);
            float size = (radius + border) * 2f;
            IStyle ring = _scopeRing.style;
            ring.left = centerX - radius - border;
            ring.top = centerY - radius - border;
            ring.width = size;
            ring.height = size;
            ring.borderTopWidth = border;
            ring.borderBottomWidth = border;
            ring.borderLeftWidth = border;
            ring.borderRightWidth = border;
            ring.borderTopColor = Color.black;
            ring.borderBottomColor = Color.black;
            ring.borderLeftColor = Color.black;
            ring.borderRightColor = Color.black;
            ring.borderTopLeftRadius = radius + border;
            ring.borderTopRightRadius = radius + border;
            ring.borderBottomLeftRadius = radius + border;
            ring.borderBottomRightRadius = radius + border;

            Place(_scopeHorizontal, centerX - radius, centerY - 0.5f, radius * 2f, 1f, 1f);
            Place(_scopeVertical, centerX - 0.5f, centerY - radius, 1f, radius * 2f, 1f);
        }

        private static VisualElement Line(Color color, Color? outline)
        {
            VisualElement line = Box("crosshair-line");
            IStyle style = line.style;
            style.backgroundColor = color;
            if (outline is Color edge)
            {
                style.borderTopWidth = 1f;
                style.borderBottomWidth = 1f;
                style.borderLeftWidth = 1f;
                style.borderRightWidth = 1f;
                style.borderTopColor = edge;
                style.borderBottomColor = edge;
                style.borderLeftColor = edge;
                style.borderRightColor = edge;
            }
            return line;
        }

        private static VisualElement Box(string name)
        {
            var element = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            element.style.position = Position.Absolute;
            return element;
        }

        private static void Place(VisualElement element, float left, float top, float width, float height, float opacity)
        {
            IStyle style = element.style;
            style.left = left;
            style.top = top;
            style.width = width;
            style.height = height;
            style.opacity = opacity;
        }

        private static void Show(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
