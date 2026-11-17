namespace ExtractionShooter.Core.Combat
{
    /// <summary>
    /// How wide a shot can stray, as a cone half-angle in degrees. Shared by the server (which rolls the shot)
    /// and the crosshair (which shows the same cone), so what the player sees is what the server uses.
    /// Pure logic, unit-tested.
    /// </summary>
    public static class SpreadModel
    {
        /// <summary>
        /// Hip fire multiplies the weapon's base spread; aiming down sights (<paramref name="aim01"/> = 1) uses the base
        /// spread. Moving adds <paramref name="moveSpread"/> at full speed, halved when fully aimed.
        /// </summary>
        /// <param name="baseSpread">Weapon + ammo spread when fully aimed and still.</param>
        /// <param name="hipMultiplier">How much wider hip fire is (at least 1).</param>
        /// <param name="aim01">0 at the hip, 1 fully aimed.</param>
        /// <param name="move01">0 standing still, 1 at full movement speed.</param>
        public static float ConeHalfAngle(float baseSpread, float hipMultiplier, float aim01, float moveSpread, float move01)
        {
            float aim = Clamp01(aim01);
            float move = Clamp01(move01);
            float hip = hipMultiplier < 1f ? 1f : hipMultiplier;
            float still = (baseSpread < 0f ? 0f : baseSpread) * Lerp(hip, 1f, aim);
            float moving = (moveSpread < 0f ? 0f : moveSpread) * move * Lerp(1f, 0.5f, aim);
            return still + moving;
        }

        /// <summary>Moves an aim amount toward aimed (1) or hip (0) at the weapon's aim speed.</summary>
        public static float StepAim(float aim01, bool aiming, float aimSeconds, float deltaTime)
        {
            float step = aimSeconds <= 0f ? 1f : deltaTime / aimSeconds;
            return Clamp01(aim01 + (aiming ? step : -step));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
