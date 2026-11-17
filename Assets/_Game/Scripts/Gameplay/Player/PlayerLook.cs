using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// First-person view. The owner drives yaw/pitch locally (no network delay
    /// on aiming) and places the main camera at the view point. Yaw is sent to
    /// the server with movement input; pitch only matters for aiming.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLook : NetworkBehaviour
    {
        [Tooltip("Eye position. The camera sits here and shots are validated against it.")]
        [SerializeField] private Transform _viewPoint;

        [SerializeField, Range(-89f, 0f)] private float _minPitch = -85f;
        [SerializeField, Range(0f, 89f)] private float _maxPitch = 85f;

        private PlayerCharacter _character;
        private Camera _camera;
        private float _yaw;
        private float _pitch;

        /// <summary>Owner's current yaw in degrees.</summary>
        public float Yaw => _yaw;

        /// <summary>Authoritative eye position (valid on every peer).</summary>
        public Vector3 EyePosition => _viewPoint != null ? _viewPoint.position : transform.position;

        /// <summary>Owner's aim direction.</summary>
        public Vector3 AimDirection => Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();

            if (_viewPoint == null)
                Debug.LogError($"[{nameof(PlayerLook)}] View point is not assigned on {name}.", this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;

            _yaw = transform.eulerAngles.y;
            _pitch = 0f;
            _camera = Camera.main;

            if (_camera == null)
                Debug.LogError($"[{nameof(PlayerLook)}] No camera tagged MainCamera in the scene.", this);
        }

        /// <summary>Owner: faces a new direction, level (spawn and respawn).</summary>
        public void ResetView(float yaw)
        {
            _yaw = Mathf.Repeat(yaw, 360f);
            _pitch = 0f;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _camera = null;
        }

        private void Update()
        {
            if (!IsOwner) return;

            Vector2 delta = _character.Input.LookDelta;
            _yaw = Mathf.Repeat(_yaw + delta.x, 360f);
            _pitch = Mathf.Clamp(_pitch - delta.y, _minPitch, _maxPitch);
        }

        private void LateUpdate()
        {
            if (!IsOwner || _camera == null) return;
            _camera.transform.SetPositionAndRotation(EyePosition, Quaternion.Euler(_pitch, _yaw, 0f));
        }
    }
}
