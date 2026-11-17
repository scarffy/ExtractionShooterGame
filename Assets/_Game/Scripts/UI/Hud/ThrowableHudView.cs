using ExtractionShooter.Core.Input;
using ExtractionShooter.Core.Throwables;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Throwables;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// Bottom-right grenade readout above the equipment HUD: the selected type (B cycles) and how many of it the
    /// player carries, plus the key hints. Dimmed when none of the selected type is carried. Reads the local player's
    /// <see cref="PlayerThrowables"/>; shown while the player is alive and in control. Built in code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThrowableHudView : MonoBehaviour
    {
        [Tooltip("Document the readout is added to (the game's HUD document).")]
        [SerializeField] private UIDocument _document;

        [SerializeField] private Color _textColor = new Color(0.92f, 0.92f, 0.88f, 1f);
        [SerializeField] private Color _emptyColor = new Color(0.92f, 0.92f, 0.88f, 0.4f);

        [Tooltip("Distance from the bottom of the screen (pixels), clear of the equipment HUD.")]
        [SerializeField, Min(0f)] private float _bottom = 180f;

        private readonly int[] _counts = new int[ThrowableMath.KindCount];

        private VisualElement _root;
        private Label _selected;
        private Label _others;
        private ThrowableKind _shownKind;
        private int _shownCount = -1;
        private int _shownOthers = -1;

        private void OnEnable()
        {
            if (_document == null)
            {
                Debug.LogError($"[{nameof(ThrowableHudView)}] Document is not assigned on {name}.", this);
                enabled = false;
                return;
            }
            TryBuild();
        }

        /// <summary>Builds the readout once the document has its root (it may enable after this component).</summary>
        private bool TryBuild()
        {
            if (_root != null) return true;
            if (_document.rootVisualElement == null) return false;

            _root = new VisualElement { name = "throwable-hud", pickingMode = PickingMode.Ignore };
            _root.style.position = Position.Absolute;
            _root.style.right = 32f;
            _root.style.bottom = _bottom;
            _root.style.alignItems = Align.FlexEnd;
            _root.style.paddingLeft = 12f;
            _root.style.paddingRight = 12f;
            _root.style.paddingTop = 4f;
            _root.style.paddingBottom = 4f;
            _root.style.backgroundColor = new Color(0.04f, 0.05f, 0.055f, 0.6f);

            _selected = Text(18f, _textColor);
            _others = Text(13f, _emptyColor);
            _root.Add(_selected);
            _root.Add(_others);
            _document.rootVisualElement.Add(_root);
            SetVisible(false);
            return true;
        }

        private void OnDisable()
        {
            _root?.RemoveFromHierarchy();
            _root = null;
            _shownCount = -1;
        }

        private void LateUpdate()
        {
            if (!TryBuild()) return;

            PlayerCharacter player = PlayerCharacter.Local;
            PlayerThrowables throwables = player != null ? player.Throwables : null;
            bool visible = throwables != null && player.Health.IsAlive && CursorController.IsLocked;
            SetVisible(visible);
            if (!visible) return;

            throwables.CountCarried(_counts);
            ThrowableKind kind = throwables.Selected;
            int count = _counts[(int)kind];
            int others = 0;
            for (int i = 0; i < _counts.Length; i++)
                if (i != (int)kind) others += _counts[i];
            if (kind == _shownKind && count == _shownCount && others == _shownOthers) return;

            _shownKind = kind;
            _shownCount = count;
            _shownOthers = others;
            _selected.text = $"{ThrowableDefinition.ShortName(kind)}  x{count}";
            _selected.style.color = count > 0 ? _textColor : _emptyColor;
            _others.text = others > 0 ? "[G] throw   [B] switch" : "[G] throw";
        }

        private void SetVisible(bool visible) => _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        private static Label Text(float size, Color color)
        {
            var label = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }
    }
}
