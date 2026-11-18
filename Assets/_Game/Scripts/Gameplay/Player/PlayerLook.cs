using ExtractionShooter.Core.Combat;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Settings;
using ExtractionShooter.Gameplay.Weapons;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// First-person view. The owner drives yaw/pitch locally (no network delay
    /// on aiming) and places the main camera at the view point. Yaw is sent to
    /// the server with movement input; pitch only matters for aiming.
    /// Aiming down sights zooms the camera to the weapon's aim field of view and slows the look speed to match.
    /// Weapon recoil (<see cref="AddRecoil"/>) adds an offset on top of the player's own view: the camera eases to each
    /// kick and settles back once firing stops. Pulling against the recoil cancels the offset first
    /// (<see cref="RecoilPattern.AbsorbInput"/>), so recovery never drags the view past where the player held it.
    /// Shots use <see cref="AimDirection"/>, which includes the offset: they go where the camera points.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLook : NetworkBehaviour
    {
        [Tooltip("Eye position. The camera sits here and shots are validated against it.")]
        [SerializeField] private Transform _viewPoint;

        [SerializeField, Range(-89f, 0f)] private float _minPitch = -85f;
        [SerializeField, Range(0f, 89f)] private float _maxPitch = 85f;

        [Tooltip("How quickly the camera eases to each recoil kick (per second, exponential). Higher is snappier.")]
        [SerializeField, Min(1f)] private float _recoilSharpness = 30f;

        private PlayerCharacter _character;
        private Camera _camera;
        private WeaponAim _aim;
        private PlayerEquipment _equipment;
        private float _cameraFieldOfView = 60f;
        private float _yaw;
        private float _pitch;

        // Recoil offset the camera is heading for, and the one it shows (eased toward the target).
        private RecoilKick _recoilTarget;
        private RecoilKick _recoil;
        private float _recoilRecovery;
        private double _recoveryStartsAt;

        /// <summary>Owner's current yaw in degrees, without recoil sway (drives the body's facing).</summary>
        public float Yaw => _yaw;

        /// <summary>Owner: the recoil offset the camera shows right now (degrees, pitch up / yaw right).</summary>
        public RecoilKick RecoilOffset => _recoil;

        /// <summary>Authoritative eye position (valid on every peer).</summary>
        public Vector3 EyePosition => _viewPoint != null ? _viewPoint.position : transform.position;

        /// <summary>Owner's aim direction: where the camera points, recoil included.</summary>
        public Vector3 AimDirection => ViewRotation * Vector3.forward;

        private Quaternion ViewRotation =>
            Quaternion.Euler(Mathf.Clamp(_pitch - _recoil.Pitch, _minPitch, _maxPitch), _yaw + _recoil.Yaw, 0f);

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _aim = GetComponent<WeaponAim>();
            _equipment = GetComponent<PlayerEquipment>();

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
            if (_camera != null) _cameraFieldOfView = _camera.fieldOfView;

            if (_camera == null)
                Debug.LogError($"[{nameof(PlayerLook)}] No camera tagged MainCamera in the scene.", this);
        }

        /// <summary>Owner: faces a new direction, level (spawn and respawn).</summary>
        public void ResetView(float yaw)
        {
            _yaw = Mathf.Repeat(yaw, 360f);
            _pitch = 0f;
            ClearRecoil();
        }

        /// <summary>
        /// Owner: kicks the view by a recoil step (degrees, pitch up / yaw right). Recovery waits
        /// <paramref name="recoveryDelay"/> seconds after the latest kick, then settles at <paramref name="recoveryRate"/> per second.
        /// </summary>
        public void AddRecoil(RecoilKick kick, float recoveryRate, float recoveryDelay)
        {
            if (!IsOwner) return;

            RecoilKick target = _recoilTarget + kick;
            // Never climb past the top of the pitch range: the view would stop but the offset would keep growing.
            float maxUp = Mathf.Max(0f, _pitch - _minPitch);
            _recoilTarget = new RecoilKick(Mathf.Min(target.Pitch, maxUp), target.Yaw);
            _recoilRecovery = recoveryRate;
            _recoveryStartsAt = Time.timeAsDouble + Mathf.Max(0f, recoveryDelay);
        }

        private void ClearRecoil()
        {
            _recoilTarget = RecoilKick.Zero;
            _recoil = RecoilKick.Zero;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner && _camera != null) _camera.fieldOfView = _cameraFieldOfView;
            _camera = null;
            ClearRecoil();
        }

        /// <summary>Owner: the camera's field of view right now (zoomed while aiming).</summary>
        public float FieldOfView => _camera != null ? _camera.fieldOfView : BaseFieldOfView;

        /// <summary>Hip-fire field of view from the player's settings.</summary>
        private static float BaseFieldOfView => GameSettingsStore.Current.FieldOfView;

        private float AimFieldOfView()
        {
            float aim = _aim != null ? _aim.Aim01 : 0f;
            float baseFieldOfView = BaseFieldOfView;
            if (aim <= 0f || _equipment == null || !_equipment.TryGetActiveWeaponStats(out WeaponStats stats)) return baseFieldOfView;
            return Mathf.Lerp(baseFieldOfView, Mathf.Min(baseFieldOfView, stats.AimFieldOfView), aim);
        }

        private void Update()
        {
            if (!IsOwner) return;

            // Zoomed in, the same mouse movement turns less, so the reticle moves at the same speed on screen.
            // The player's aim sensitivity setting scales that while aiming.
            float aimSensitivity = Mathf.Lerp(1f, GameSettingsStore.Current.AimSensitivity, _aim != null ? _aim.Aim01 : 0f);
            Vector2 delta = _character.Input.LookDelta * (FieldOfView / BaseFieldOfView * aimSensitivity);

            // Input against the recoil cancels the offset first; the rest turns the view.
            float up = RecoilPattern.AbsorbInput(_recoilTarget.Pitch, delta.y, out float absorbedPitch);
            float right = RecoilPattern.AbsorbInput(_recoilTarget.Yaw, delta.x, out float absorbedYaw);
            var absorbed = new RecoilKick(absorbedPitch, absorbedYaw);
            _recoilTarget -= absorbed;
            _recoil -= absorbed;

            _yaw = Mathf.Repeat(_yaw + right, 360f);
            _pitch = Mathf.Clamp(_pitch - up, _minPitch, _maxPitch);

            float deltaTime = Time.deltaTime;
            if (Time.timeAsDouble >= _recoveryStartsAt)
                _recoilTarget = RecoilPattern.Recover(_recoilTarget, _recoilRecovery, deltaTime);
            _recoil = RecoilPattern.Follow(_recoil, _recoilTarget, _recoilSharpness, deltaTime);
        }

        private void LateUpdate()
        {
            if (!IsOwner || _camera == null) return;
            _camera.transform.SetPositionAndRotation(EyePosition, ViewRotation);
            _camera.fieldOfView = AimFieldOfView();
        }
    }
}
