using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// One tick of player intent sent from the owning client to the server.
    /// Public fields so FishNet can serialize it without a custom serializer.
    /// </summary>
    public struct PlayerInputFrame
    {
        public Vector2 Move;
        public float Yaw;
        public bool Jump;

        public PlayerInputFrame(Vector2 move, float yaw, bool jump)
        {
            Move = move;
            Yaw = yaw;
            Jump = jump;
        }

        /// <summary>
        /// Returns a copy that is safe to simulate: finite values, clamped movement,
        /// yaw wrapped to 0..360. Never trust input coming from a client.
        /// </summary>
        public readonly PlayerInputFrame Sanitized()
        {
            Vector2 move = IsFinite(Move.x) && IsFinite(Move.y) ? Vector2.ClampMagnitude(Move, 1f) : Vector2.zero;
            float yaw = IsFinite(Yaw) ? Mathf.Repeat(Yaw, 360f) : 0f;
            return new PlayerInputFrame(move, yaw, Jump);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
