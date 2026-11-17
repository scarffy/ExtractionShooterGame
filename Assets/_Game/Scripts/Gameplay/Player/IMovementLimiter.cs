namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// Something on the player that restricts movement (aiming down sights, a fractured leg).
    /// The server's <see cref="PlayerMotor"/> asks every limiter on the player each tick.
    /// </summary>
    public interface IMovementLimiter
    {
        /// <summary>False blocks sprinting.</summary>
        bool AllowsSprint { get; }

        /// <summary>Multiplies the movement speed (1 = no effect).</summary>
        float SpeedMultiplier { get; }
    }
}
