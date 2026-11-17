using ExtractionShooter.Core.Throwables;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Throwables;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// Full-screen white-out when the local player is caught by a flash grenade (<see cref="PlayerThrowables.Flashed"/>):
    /// solid for most of its time, then fading out. A stronger flash is whiter. A second flash extends the white-out.
    /// Cleared on death. Built in code on top of everything else in the HUD document.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThrowableFlashView : MonoBehaviour
    {
        [Tooltip("Document the overlay is added to (the game's HUD document).")]
        [SerializeField] private UIDocument _document;

        [SerializeField] private Color _color = new Color(1f, 1f, 0.97f, 1f);

        [Tooltip("Seconds at the end of a white-out over which it clears.")]
        [SerializeField, Min(0f)] private float _fadeSeconds = 1.5f;

        [Tooltip("Opacity of the weakest flash that still counts (the strongest is fully white).")]
        [SerializeField, Range(0f, 1f)] private float _minOpacity = 0.75f;

        private VisualElement _overlay;
        private PlayerThrowables _bound;
        private float _startedAt;
        private float _duration;
        private float _peak;

        private void OnEnable()
        {
            if (_document == null)
            {
                Debug.LogError($"[{nameof(ThrowableFlashView)}] Document is not assigned on {name}.", this);
                enabled = false;
                return;
            }

            TryBuild();
            PlayerCharacter.LocalPlayerChanged += Bind;
            Bind(PlayerCharacter.Local);
        }

        /// <summary>Builds the overlay once the document has its root (it may enable after this component).</summary>
        private bool TryBuild()
        {
            if (_overlay != null) return true;
            if (_document.rootVisualElement == null) return false;

            _overlay = new VisualElement { name = "flash-overlay", pickingMode = PickingMode.Ignore };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0f;
            _overlay.style.right = 0f;
            _overlay.style.top = 0f;
            _overlay.style.bottom = 0f;
            _overlay.style.backgroundColor = _color;
            _document.rootVisualElement.Add(_overlay);
            _overlay.style.display = DisplayStyle.None;
            return true;
        }

        private void OnDisable()
        {
            PlayerCharacter.LocalPlayerChanged -= Bind;
            Bind(null);
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
        }

        private void Bind(PlayerCharacter player)
        {
            if (_bound != null) _bound.Flashed -= OnFlashed;
            _bound = player != null ? player.Throwables : null;
            if (_bound != null) _bound.Flashed += OnFlashed;
            Clear();
        }

        private void OnFlashed(float seconds, float intensity)
        {
            if (seconds <= 0f || !TryBuild()) return;

            float now = Time.time;
            float remaining = _duration > 0f ? _duration - (now - _startedAt) : 0f;
            _startedAt = now;
            _duration = Mathf.Max(seconds, remaining);
            _peak = Mathf.Max(Mathf.Lerp(_minOpacity, 1f, Mathf.Clamp01(intensity)), remaining > 0f ? _peak : 0f);
            _overlay.BringToFront();
        }

        private void LateUpdate()
        {
            if (_overlay == null || _duration <= 0f) return;

            PlayerCharacter player = PlayerCharacter.Local;
            if (player == null || !player.Health.IsAlive)
            {
                Clear();
                return;
            }

            float opacity = ThrowableMath.FlashOpacity(Time.time - _startedAt, _duration, _fadeSeconds) * _peak;
            if (opacity <= 0f)
            {
                Clear();
                return;
            }
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.style.opacity = opacity;
        }

        private void Clear()
        {
            _duration = 0f;
            _peak = 0f;
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
        }
    }
}
