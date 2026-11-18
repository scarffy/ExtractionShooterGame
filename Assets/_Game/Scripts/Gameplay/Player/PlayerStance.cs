using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// Matches the hitboxes and the camera to the motor's stance on every peer. Crouching lowers the head and body
    /// hitboxes (instantly, so the server's hit checks match) and the camera point (smoothly, so the owner's view does not
    /// snap), scaled by crouching height / standing height around the feet. The visible body poses itself to match
    /// (<see cref="Visuals.CharacterBody"/>, fed by <see cref="PlayerVisuals"/> with <see cref="CrouchHeightRatio"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerStance : NetworkBehaviour
    {
        [SerializeField] private Transform _headHitbox;
        [SerializeField] private Transform _bodyHitbox;
        [SerializeField] private Transform _legsHitbox;

        [Tooltip("Camera / eye point: moves smoothly.")]
        [SerializeField] private Transform _viewPoint;

        [SerializeField, Min(0.1f)] private float _viewLerpSpeed = 10f;

        private PlayerMotor _motor;
        private Vector3 _headPosition, _bodyHitboxScale, _legsHitboxScale, _viewPosition;
        private float _ratio = 1f;

        /// <summary>Crouched height / standing height from the movement settings (what the hitboxes scale by when crouched).</summary>
        public float CrouchHeightRatio
        {
            get
            {
                MovementSettings settings = _motor != null ? _motor.Settings : null;
                return settings != null && settings.StandingHeight > 0f ? settings.CrouchingHeight / settings.StandingHeight : 1f;
            }
        }

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            if (_headHitbox != null) _headPosition = _headHitbox.localPosition;
            if (_bodyHitbox != null) _bodyHitboxScale = _bodyHitbox.localScale;
            if (_legsHitbox != null) _legsHitboxScale = _legsHitbox.localScale;
            if (_viewPoint != null) _viewPosition = _viewPoint.localPosition;
            _motor.StanceChanged += Apply;
        }

        private void OnDestroy()
        {
            if (_motor != null) _motor.StanceChanged -= Apply;
        }

        private void Update()
        {
            if (_viewPoint == null) return;
            Vector3 target = Scaled(_viewPosition, _ratio);
            _viewPoint.localPosition = Vector3.Lerp(_viewPoint.localPosition, target, 1f - Mathf.Exp(-_viewLerpSpeed * Time.deltaTime));
        }

        private void Apply()
        {
            _ratio = _motor.IsCrouched ? CrouchHeightRatio : 1f;
            if (_headHitbox != null) _headHitbox.localPosition = Scaled(_headPosition, _ratio);
            if (_bodyHitbox != null) _bodyHitbox.localScale = new Vector3(_bodyHitboxScale.x, _bodyHitboxScale.y * _ratio, _bodyHitboxScale.z);
            if (_legsHitbox != null) _legsHitbox.localScale = new Vector3(_legsHitboxScale.x, _legsHitboxScale.y * _ratio, _legsHitboxScale.z);
            if (!IsOwner && _viewPoint != null) _viewPoint.localPosition = Scaled(_viewPosition, _ratio);
        }

        private static Vector3 Scaled(Vector3 position, float ratio) => new Vector3(position.x, position.y * ratio, position.z);
    }
}
