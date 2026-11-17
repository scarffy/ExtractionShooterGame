using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// Matches the body to the motor's stance on every peer. Crouching scales the body down and lowers the visor,
    /// the head and body hitboxes (instantly, so the server's hit checks match) and the camera point (smoothly, so the
    /// owner's view does not snap). All parts are scaled by crouching height / standing height around the feet.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerStance : NetworkBehaviour
    {
        [SerializeField] private Transform _body;
        [SerializeField] private Transform _visor;
        [SerializeField] private Transform _headHitbox;
        [SerializeField] private Transform _bodyHitbox;
        [SerializeField] private Transform _legsHitbox;

        [Tooltip("Camera / eye point: moves smoothly.")]
        [SerializeField] private Transform _viewPoint;

        [SerializeField, Min(0.1f)] private float _viewLerpSpeed = 10f;

        private PlayerMotor _motor;
        private Vector3 _bodyPosition, _bodyScale, _visorPosition, _headPosition, _bodyHitboxScale, _legsHitboxScale, _viewPosition;
        private float _ratio = 1f;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            if (_body != null) { _bodyPosition = _body.localPosition; _bodyScale = _body.localScale; }
            if (_visor != null) _visorPosition = _visor.localPosition;
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
            MovementSettings settings = _motor.Settings;
            _ratio = _motor.IsCrouched && settings != null ? settings.CrouchingHeight / settings.StandingHeight : 1f;

            if (_body != null)
            {
                _body.localPosition = Scaled(_bodyPosition, _ratio);
                _body.localScale = new Vector3(_bodyScale.x, _bodyScale.y * _ratio, _bodyScale.z);
            }
            if (_visor != null) _visor.localPosition = Scaled(_visorPosition, _ratio);
            if (_headHitbox != null) _headHitbox.localPosition = Scaled(_headPosition, _ratio);
            if (_bodyHitbox != null) _bodyHitbox.localScale = new Vector3(_bodyHitboxScale.x, _bodyHitboxScale.y * _ratio, _bodyHitboxScale.z);
            if (_legsHitbox != null) _legsHitbox.localScale = new Vector3(_legsHitboxScale.x, _legsHitboxScale.y * _ratio, _legsHitboxScale.z);
            if (!IsOwner && _viewPoint != null) _viewPoint.localPosition = Scaled(_viewPosition, _ratio);
        }

        private static Vector3 Scaled(Vector3 position, float ratio) => new Vector3(position.x, position.y * ratio, position.z);
    }
}
