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

        public float WalkSpeed => _walkSpeed;
        public float JumpSpeed => _jumpSpeed;
        public float Gravity => _gravity;
        public float GroundedStickVelocity => _groundedStickVelocity;
    }
}
