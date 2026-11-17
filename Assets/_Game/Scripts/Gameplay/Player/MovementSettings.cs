using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// Tunable movement values, shared by every player using this asset.
    /// </summary>
    [CreateAssetMenu(fileName = "MovementSettings", menuName = "Extraction Shooter/Movement Settings")]
    public sealed class MovementSettings : ScriptableObject
    {
        [Tooltip("Ground speed in metres per second.")]
        [SerializeField, Min(0f)] private float _walkSpeed = 5f;

        [Tooltip("Upward velocity applied on jump, metres per second.")]
        [SerializeField, Min(0f)] private float _jumpSpeed = 5f;

        [Tooltip("Downward acceleration in metres per second squared (positive value).")]
        [SerializeField, Min(0f)] private float _gravity = 15f;

        [Tooltip("Small downward velocity kept while grounded so the controller stays snapped to slopes.")]
        [SerializeField, Min(0f)] private float _groundedStickVelocity = 1f;

        [Header("Sprint and stamina")]
        [SerializeField, Min(0f)] private float _sprintSpeed = 7f;
        [SerializeField, Min(1f)] private float _maxStamina = 100f;
        [SerializeField, Min(0f)] private float _sprintDrainPerSecond = 18f;
        [SerializeField, Min(0f)] private float _staminaRegenPerSecond = 14f;

        [Tooltip("Seconds after sprinting stops before stamina refills.")]
        [SerializeField, Min(0f)] private float _staminaRegenDelay = 1f;

        [Tooltip("Stamina needed to start a sprint (avoids stutter-sprinting on an empty bar).")]
        [SerializeField, Min(0f)] private float _minStaminaToSprint = 15f;

        [Tooltip("Stamina a jump costs.")]
        [SerializeField, Min(0f)] private float _jumpStaminaCost = 8f;

        [Header("Crouch")]
        [SerializeField, Min(0f)] private float _crouchSpeed = 2.6f;
        [SerializeField, Min(0.5f)] private float _standingHeight = 2f;
        [SerializeField, Min(0.5f)] private float _crouchingHeight = 1.3f;

        [Header("Other speed limits")]
        [Tooltip("Speed multiplier while aiming down sights.")]
        [SerializeField, Range(0.1f, 1f)] private float _aimingSpeedMultiplier = 0.6f;

        [Tooltip("Speed multiplier with a fractured leg (no sprinting either).")]
        [SerializeField, Range(0.1f, 1f)] private float _fracturedSpeedMultiplier = 0.55f;

        public float WalkSpeed => _walkSpeed;
        public float SprintSpeed => _sprintSpeed;
        public float MaxStamina => _maxStamina;
        public float SprintDrainPerSecond => _sprintDrainPerSecond;
        public float StaminaRegenPerSecond => _staminaRegenPerSecond;
        public float StaminaRegenDelay => _staminaRegenDelay;
        public float MinStaminaToSprint => _minStaminaToSprint;
        public float JumpStaminaCost => _jumpStaminaCost;
        public float CrouchSpeed => _crouchSpeed;
        public float StandingHeight => _standingHeight;
        public float CrouchingHeight => _crouchingHeight;
        public float AimingSpeedMultiplier => _aimingSpeedMultiplier;
        public float FracturedSpeedMultiplier => _fracturedSpeedMultiplier;
        public float JumpSpeed => _jumpSpeed;
        public float Gravity => _gravity;
        public float GroundedStickVelocity => _groundedStickVelocity;
    }
}
