using System;

namespace ExtractionShooter.Core.Visuals
{
    /// <summary>
    /// Where the hips go when a body crouches, so the visible head ends where the head hitbox is: the hitbox is lowered
    /// to <c>standing head height x crouch ratio</c> straight above the feet, and the body bends its knees, lowers its hips
    /// and leans forward to put its head there. Pure logic, unit-tested.
    /// </summary>
    public static class CrouchPose
    {
        /// <summary>
        /// Hip height that puts the head at <paramref name="headHeight"/> with the torso leaning
        /// <paramref name="leanDegrees"/> forward, never below <paramref name="minHip"/>.
        /// </summary>
        /// <param name="headAboveHip">Head centre above the hip joints when upright.</param>
        public static float HipHeight(float headHeight, float headAboveHip, float leanDegrees, float minHip)
        {
            float hip = headHeight - headAboveHip * (float)Math.Cos(ToRadians(leanDegrees));
            return hip < minHip ? minHip : hip;
        }

        /// <summary>How far the hips move back so the leaning head stays straight above the feet.</summary>
        public static float HipShiftBack(float headAboveHip, float leanDegrees) =>
            headAboveHip * (float)Math.Sin(ToRadians(leanDegrees));

        /// <summary>Blends between standing (0) and crouched (1) values.</summary>
        public static float Blend(float standing, float crouched, float crouch01)
        {
            float t = crouch01 < 0f ? 0f : crouch01 > 1f ? 1f : crouch01;
            return standing + (crouched - standing) * t;
        }

        private static double ToRadians(float degrees) => degrees * Math.PI / 180.0;
    }
}
