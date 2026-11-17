using ExtractionShooter.Core.Input;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// Bottom-centre status: health and stamina bars, injury warnings (bleeding, fractured leg) and the progress of a
    /// treatment in use. Reads the local player's <see cref="Health"/>, <see cref="PlayerMotor"/> and
    /// <see cref="PlayerInjuries"/>. Shown while the player is alive and in control. Built in code.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class StatusHudView : MonoBehaviour
    {
        private const float BarWidth = 260f;
        private const float BarHeight = 8f;

        [SerializeField] private Color _healthColor = new Color(0.85f, 0.85f, 0.8f, 0.95f);
        [SerializeField] private Color _lowHealthColor = new Color(0.9f, 0.3f, 0.25f, 0.95f);
        [SerializeField] private Color _staminaColor = new Color(0.55f, 0.75f, 0.45f, 0.95f);
        [SerializeField] private Color _warningColor = new Color(0.95f, 0.35f, 0.3f, 1f);
        [SerializeField] private Color _treatmentColor = new Color(0.9f, 0.75f, 0.35f, 1f);

        private VisualElement _root;
        private VisualElement _healthFill, _staminaFill, _staminaTrack, _treatmentFill, _treatmentTrack;
        private Label _healthLabel, _bleeding, _fracture, _treatment;

        private void OnEnable()
        {
            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;

            _root = new VisualElement { name = "status-hud", pickingMode = PickingMode.Ignore };
            _root.style.position = Position.Absolute;
            _root.style.bottom = 24f;
            _root.style.left = 0f;
            _root.style.right = 0f;
            _root.style.alignItems = Align.Center;
            documentRoot.Add(_root);

            var warnings = Row();
            _bleeding = Chip("BLEEDING", _warningColor);
            _fracture = Chip("FRACTURED LEG", _warningColor);
            warnings.Add(_bleeding);
            warnings.Add(_fracture);
            _root.Add(warnings);

            _treatment = Text(string.Empty, 14f, _treatmentColor);
            _root.Add(_treatment);
            (_treatmentTrack, _treatmentFill) = Bar(_treatmentColor, BarWidth * 0.6f);
            _root.Add(_treatmentTrack);

            _healthLabel = Text("100", 16f, _healthColor);
            _root.Add(_healthLabel);
            (VisualElement healthTrack, VisualElement healthFill) = Bar(_healthColor, BarWidth);
            _healthFill = healthFill;
            _root.Add(healthTrack);

            (_staminaTrack, _staminaFill) = Bar(_staminaColor, BarWidth);
            _staminaTrack.style.marginTop = 4f;
            _root.Add(_staminaTrack);

            SetVisible(_root, false);
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
            bool visible = player != null && player.Health.IsAlive && CursorController.IsLocked;
            SetVisible(_root, visible);
            if (!visible) return;

            Health health = player.Health;
            float health01 = health.Max > 0 ? (float)health.Current / health.Max : 0f;
            _healthFill.style.width = BarWidth * health01;
            _healthFill.style.backgroundColor = health01 <= 0.3f ? _lowHealthColor : _healthColor;
            _healthLabel.text = health.Current.ToString();

            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            float stamina = motor != null ? motor.Stamina01 : 1f;
            _staminaFill.style.width = BarWidth * stamina;
            // Only worth showing when it is being used.
            _staminaTrack.style.opacity = stamina >= 0.999f && (motor == null || !motor.IsSprinting) ? 0.25f : 1f;

            PlayerInjuries injuries = player.Injuries;
            SetVisible(_bleeding, injuries != null && injuries.IsBleeding);
            SetVisible(_fracture, injuries != null && injuries.IsFractured);

            bool treating = injuries != null && injuries.IsTreating;
            SetVisible(_treatment, treating);
            SetVisible(_treatmentTrack, treating);
            if (treating)
            {
                _treatment.text = $"Using {injuries.TreatmentName}";
                _treatmentFill.style.width = BarWidth * 0.6f * injuries.TreatmentProgress;
            }
        }

        private static VisualElement Row()
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 6f;
            return row;
        }

        private static Label Chip(string text, Color color)
        {
            Label chip = Text(text, 13f, Color.white);
            chip.style.backgroundColor = color;
            chip.style.paddingLeft = 8f;
            chip.style.paddingRight = 8f;
            chip.style.paddingTop = 2f;
            chip.style.paddingBottom = 2f;
            chip.style.marginLeft = 4f;
            chip.style.marginRight = 4f;
            return chip;
        }

        private static Label Text(string text, float size, Color color)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 2f;
            return label;
        }

        private static (VisualElement track, VisualElement fill) Bar(Color color, float width)
        {
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.width = width;
            track.style.height = BarHeight;
            track.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = BarHeight;
            fill.style.width = width;
            fill.style.backgroundColor = color;
            track.Add(fill);
            return (track, fill);
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
