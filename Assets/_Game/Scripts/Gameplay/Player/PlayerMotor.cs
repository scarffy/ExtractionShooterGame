using System;
using ExtractionShooter.Gameplay.Combat;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// Server-authoritative movement (no client prediction yet).
    /// <list type="bullet">
    /// <item>Owner: sends an input frame every network tick (move, yaw, jump, sprint wish, crouch toggle state).</item>
    /// <item>Server: simulates the CharacterController. Walk, sprint (forward only, costs stamina, blocked while
    /// crouched or by any <see cref="IMovementLimiter"/>), crouch (smaller capsule, slower; standing up is refused
    /// under a low ceiling), jump (costs stamina).</item>
    /// </list>
    /// A server-authoritative NetworkTransform replicates the position; the stance replicates to everyone, stamina to
    /// the owner. <see cref="PlayerStance"/> moves the hitboxes, body and camera to match the stance.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : NetworkBehaviour
    {
        private const int HeadroomBufferSize = 8;

        [SerializeField] private MovementSettings _settings;

        private readonly SyncVar<bool> _crouched = new SyncVar<bool>();
        private readonly SyncVar<bool> _sprinting = new SyncVar<bool>();
        private readonly SyncVar<byte> _staminaPercent =
            new SyncVar<byte>(100, new SyncTypeSettings(ReadPermission.OwnerOnly));

        private readonly RaycastHit[] _headroomHits = new RaycastHit[HeadroomBufferSize];

        private CharacterController _controller;
        private PlayerCharacter _character;
        private PlayerLook _look;
        private Health _health;
        private IMovementLimiter[] _limiters = Array.Empty<IMovementLimiter>();

        private PlayerInputFrame _serverInput;
        private float _verticalVelocity;
        private float _stamina;
        private float _secondsSinceSprint;
        private bool _ownerCrouchToggle;

        /// <summary>All peers: the stance changed (crouched or sprinting).</summary>
        public event Action StanceChanged;

        public bool IsCrouched => _crouched.Value;
        public bool IsSprinting => _sprinting.Value;

        /// <summary>Owner and server: stamina 0..1.</summary>
        public float Stamina01 => IsServerInitialized && _settings != null ? _stamina / _settings.MaxStamina : _staminaPercent.Value / 100f;

        public MovementSettings Settings => _settings;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _character = GetComponent<PlayerCharacter>();
            _look = GetComponent<PlayerLook>();
            _health = GetComponent<Health>();
            _limiters = GetComponents<IMovementLimiter>();
            _crouched.OnChange += OnStanceChanged;
            _sprinting.OnChange += OnStanceChanged;

            if (_settings == null)
                Debug.LogError($"[{nameof(PlayerMotor)}] Movement settings are not assigned on {name}.", this);
        }

        private void OnDestroy()
        {
            _crouched.OnChange -= OnStanceChanged;
            _sprinting.OnChange -= OnStanceChanged;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            TimeManager.OnTick += HandleTick;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (TimeManager != null) TimeManager.OnTick -= HandleTick;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _serverInput = new PlayerInputFrame(Vector2.zero, transform.eulerAngles.y, false);
            _stamina = _settings != null ? _settings.MaxStamina : 100f;
            ApplyCapsule(false);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Pure clients never simulate; an active controller would fight the NetworkTransform.
            if (!IsServerInitialized) _controller.enabled = false;
        }

        /// <summary>Server only. Moves the player instantly (spawn, respawn), standing.</summary>
        public void ServerTeleport(Pose pose)
        {
            if (!IsServerInitialized) return;

            _controller.enabled = false;
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            _controller.enabled = true;

            _verticalVelocity = 0f;
            float yaw = pose.rotation.eulerAngles.y;
            _serverInput = new PlayerInputFrame(Vector2.zero, yaw, false);
            SetCrouched(false);
            _sprinting.Value = false;

            // The owner aims with its own yaw; without this a respawned player keeps facing where they died.
            if (Owner.IsValid) TargetTeleported(Owner, yaw);
        }

        [TargetRpc]
        private void TargetTeleported(NetworkConnection connection, float yaw)
        {
            _ownerCrouchToggle = false;
            _look.ResetView(yaw);
        }

        private void Update()
        {
            if (!IsOwner) return;

            var input = _character.Input;
            if (input.CrouchPressed) _ownerCrouchToggle = !_ownerCrouchToggle;
            // Sprinting forward stands you up, as in most shooters.
            if (input.SprintHeld && input.Move.y > 0.5f) _ownerCrouchToggle = false;
            if (!_health.IsAlive) _ownerCrouchToggle = false;
        }

        private void HandleTick()
        {
            // IsClientInitialized: on a host the server side despawns first; the client side must stop sending.
            if (IsOwner && IsClientInitialized)
                SubmitInput(BuildInputFrame());

            if (IsServerInitialized)
                Simulate((float)TimeManager.TickDelta);
        }

        private PlayerInputFrame BuildInputFrame()
        {
            var input = _character.Input;
            // Firing or aiming lowers the sprint: the weapon has to come up first.
            bool sprint = input.SprintHeld && !input.FireHeld && !input.AimHeld;
            return new PlayerInputFrame(input.Move, _look.Yaw, input.JumpHeld, sprint, _ownerCrouchToggle);
        }

        [ServerRpc]
        private void SubmitInput(PlayerInputFrame frame, Channel channel = Channel.Unreliable)
        {
            _serverInput = frame.Sanitized();
        }

        private void Simulate(float deltaTime)
        {
            if (_settings == null || !_controller.enabled) return;

            bool canAct = _health.IsAlive;
            Vector2 move = canAct ? _serverInput.Move : Vector2.zero;

            // Stance: crouch whenever asked; stand up only with headroom.
            bool wantCrouch = canAct && _serverInput.Crouch;
            if (wantCrouch != _crouched.Value && (wantCrouch || HasHeadroom())) SetCrouched(wantCrouch);

            // Sprint: forward, standing, allowed by every limiter, and enough stamina (more to start than to keep going).
            bool allowed = true;
            float speedMultiplier = 1f;
            foreach (IMovementLimiter limiter in _limiters)
            {
                allowed &= limiter.AllowsSprint;
                speedMultiplier *= Mathf.Clamp01(limiter.SpeedMultiplier);
            }
            float needed = _sprinting.Value ? 0.01f : _settings.MinStaminaToSprint;
            bool sprinting = canAct && _serverInput.Sprint && move.y > 0.5f && !_crouched.Value && allowed && _stamina >= needed;
            if (sprinting != _sprinting.Value) _sprinting.Value = sprinting;

            if (sprinting)
            {
                _stamina -= _settings.SprintDrainPerSecond * deltaTime;
                _secondsSinceSprint = 0f;
            }
            else
            {
                _secondsSinceSprint += deltaTime;
                if (_secondsSinceSprint >= _settings.StaminaRegenDelay) _stamina += _settings.StaminaRegenPerSecond * deltaTime;
            }

            float speed = sprinting ? _settings.SprintSpeed : _crouched.Value ? _settings.CrouchSpeed : _settings.WalkSpeed;
            speed *= speedMultiplier;

            transform.rotation = Quaternion.Euler(0f, _serverInput.Yaw, 0f);
            Vector3 horizontal = (transform.right * move.x + transform.forward * move.y) * speed;

            if (_controller.isGrounded)
            {
                bool jump = canAct && _serverInput.Jump && !_crouched.Value && _stamina >= _settings.JumpStaminaCost;
                if (jump)
                {
                    _stamina -= _settings.JumpStaminaCost;
                    _secondsSinceSprint = 0f;
                }
                _verticalVelocity = jump ? _settings.JumpSpeed : -_settings.GroundedStickVelocity;
            }
            _verticalVelocity -= _settings.Gravity * deltaTime;

            _stamina = Mathf.Clamp(_stamina, 0f, _settings.MaxStamina);
            byte percent = (byte)Mathf.RoundToInt(_stamina / _settings.MaxStamina * 100f);
            if (percent != _staminaPercent.Value) _staminaPercent.Value = percent;

            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * deltaTime);
        }

        private void SetCrouched(bool crouched)
        {
            if (_crouched.Value != crouched) _crouched.Value = crouched;
            ApplyCapsule(crouched);
        }

        private void ApplyCapsule(bool crouched)
        {
            if (_settings == null) return;
            float height = crouched ? _settings.CrouchingHeight : _settings.StandingHeight;
            _controller.height = height;
            _controller.center = new Vector3(0f, height * 0.5f, 0f);
        }

        /// <summary>Server: nothing solid above a crouched player up to standing height.</summary>
        private bool HasHeadroom()
        {
            float clearance = _settings.StandingHeight - _settings.CrouchingHeight;
            Vector3 origin = transform.position + Vector3.up * (_settings.CrouchingHeight - 0.05f);
            int count = Physics.RaycastNonAlloc(origin, Vector3.up, _headroomHits, clearance + 0.05f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!_headroomHits[i].collider.transform.IsChildOf(transform)) return false;
            return true;
        }

        private void OnStanceChanged(bool previous, bool current, bool asServer)
        {
            // Raised once per side (twice on a host): presentation listens on both.
            StanceChanged?.Invoke();
        }
    }
}
