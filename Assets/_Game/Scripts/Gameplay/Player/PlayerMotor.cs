using ExtractionShooter.Gameplay.Combat;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// Server-authoritative movement (no client prediction yet).
    /// Owner: sends an input frame every network tick.
    /// Server: simulates the CharacterController with the latest frame.
    /// A server-authoritative NetworkTransform replicates the result.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : NetworkBehaviour
    {
        [SerializeField] private MovementSettings _settings;

        private CharacterController _controller;
        private PlayerCharacter _character;
        private PlayerLook _look;
        private Health _health;

        private PlayerInputFrame _serverInput;
        private float _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _character = GetComponent<PlayerCharacter>();
            _look = GetComponent<PlayerLook>();
            _health = GetComponent<Health>();

            if (_settings == null)
                Debug.LogError($"[{nameof(PlayerMotor)}] Movement settings are not assigned on {name}.", this);
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
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Pure clients never simulate; an active controller would fight the NetworkTransform.
            if (!IsServerInitialized) _controller.enabled = false;
        }

        /// <summary>Server only. Moves the player instantly (spawn, respawn).</summary>
        public void ServerTeleport(Pose pose)
        {
            if (!IsServerInitialized) return;

            _controller.enabled = false;
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            _controller.enabled = true;

            _verticalVelocity = 0f;
            float yaw = pose.rotation.eulerAngles.y;
            _serverInput = new PlayerInputFrame(Vector2.zero, yaw, false);

            // The owner aims with its own yaw; without this a respawned player keeps facing where they died.
            if (Owner.IsValid) TargetTeleported(Owner, yaw);
        }

        [TargetRpc]
        private void TargetTeleported(NetworkConnection connection, float yaw) => _look.ResetView(yaw);

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
            return new PlayerInputFrame(input.Move, _look.Yaw, input.JumpHeld);
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

            transform.rotation = Quaternion.Euler(0f, _serverInput.Yaw, 0f);
            Vector3 horizontal = (transform.right * move.x + transform.forward * move.y) * _settings.WalkSpeed;

            if (_controller.isGrounded)
            {
                _verticalVelocity = canAct && _serverInput.Jump
                    ? _settings.JumpSpeed
                    : -_settings.GroundedStickVelocity;
            }
            _verticalVelocity -= _settings.Gravity * deltaTime;

            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * deltaTime);
        }
    }
}
