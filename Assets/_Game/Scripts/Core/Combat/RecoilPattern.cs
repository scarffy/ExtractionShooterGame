using System;

namespace ExtractionShooter.Core.Combat
{
    /// <summary>A view rotation in degrees. Positive pitch turns the view up, positive yaw turns it right.</summary>
    public readonly struct RecoilKick : IEquatable<RecoilKick>
    {
        public RecoilKick(float pitch, float yaw)
        {
            Pitch = pitch;
            Yaw = yaw;
        }

        /// <summary>Degrees up.</summary>
        public float Pitch { get; }

        /// <summary>Degrees right.</summary>
        public float Yaw { get; }

        public static RecoilKick Zero => default;

        /// <summary>Angular size of the offset in degrees.</summary>
        public float Magnitude => (float)Math.Sqrt(Pitch * Pitch + Yaw * Yaw);

        public static RecoilKick operator +(RecoilKick a, RecoilKick b) => new RecoilKick(a.Pitch + b.Pitch, a.Yaw + b.Yaw);
        public static RecoilKick operator -(RecoilKick a, RecoilKick b) => new RecoilKick(a.Pitch - b.Pitch, a.Yaw - b.Yaw);
        public static RecoilKick operator *(RecoilKick a, float scale) => new RecoilKick(a.Pitch * scale, a.Yaw * scale);

        public bool Equals(RecoilKick other) => Pitch.Equals(other.Pitch) && Yaw.Equals(other.Yaw);
        public override bool Equals(object obj) => obj is RecoilKick other && Equals(other);
        public override int GetHashCode() => Pitch.GetHashCode() * 397 ^ Yaw.GetHashCode();
        public override string ToString() => $"({Pitch:0.###} up, {Yaw:0.###} right)";
    }

    /// <summary>A weapon's recoil numbers (from its definition).</summary>
    public readonly struct RecoilProfile
    {
        /// <param name="verticalKick">Degrees the view climbs on the first shots of a spray.</param>
        /// <param name="horizontalKick">Largest sideways kick per shot in degrees.</param>
        /// <param name="horizontalBias">-1 drifts left, 0 sways evenly, 1 drifts right.</param>
        /// <param name="patternSeed">Picks the shape of the sideways sway; fixed per weapon so the pattern can be learned.</param>
        /// <param name="jitter">Largest random sideways wobble per shot in degrees (half that vertically).</param>
        public RecoilProfile(float verticalKick, float horizontalKick, float horizontalBias, int patternSeed, float jitter)
        {
            VerticalKick = verticalKick < 0f ? 0f : verticalKick;
            HorizontalKick = horizontalKick < 0f ? 0f : horizontalKick;
            HorizontalBias = horizontalBias < -1f ? -1f : horizontalBias > 1f ? 1f : horizontalBias;
            PatternSeed = patternSeed;
            Jitter = jitter < 0f ? 0f : jitter;
        }

        public float VerticalKick { get; }
        public float HorizontalKick { get; }
        public float HorizontalBias { get; }
        public int PatternSeed { get; }
        public float Jitter { get; }
    }

    /// <summary>
    /// Weapon recoil as a learnable spray pattern. Every shot of a spray kicks the view by
    /// <see cref="KickFor"/>: a steady climb that eases off after the first shots, plus a sideways sway whose shape is
    /// fixed per weapon (<see cref="RecoilProfile.PatternSeed"/>), plus a small jitter. The first shots go nearly
    /// straight up; the sway grows in after <see cref="SwayInShots"/> shots. When firing stops the offset recovers
    /// (<see cref="Recover"/>), and the shot counter starts over after a short pause (<see cref="NextShotIndex"/>).
    /// Pulling against the recoil eats into the offset first (<see cref="AbsorbInput"/>), so recovery never drags the
    /// view past where the player held it. Pure logic, unit-tested.
    /// </summary>
    public static class RecoilPattern
    {
        /// <summary>Shots over which the climb eases from full to <see cref="SustainedClimb"/>.</summary>
        public const int ClimbShots = 8;

        /// <summary>Share of the vertical kick that remains once a spray is under way.</summary>
        public const float SustainedClimb = 0.55f;

        /// <summary>Shots over which the sideways sway fades in.</summary>
        public const int SwayInShots = 3;

        /// <summary>Offsets smaller than this (degrees) snap to zero while recovering.</summary>
        public const float RestThreshold = 0.001f;

        /// <summary>
        /// The kick of shot <paramref name="shotIndex"/> (0 = first shot of a spray). Deterministic: the same index,
        /// profile and <paramref name="jitterSeed"/> always give the same kick, and the pattern without jitter only
        /// depends on the profile.
        /// </summary>
        /// <param name="jitterSeed">Varies the jitter only (for example a new value per spray).</param>
        public static RecoilKick KickFor(int shotIndex, in RecoilProfile profile, int jitterSeed)
        {
            int shot = shotIndex < 0 ? 0 : shotIndex;

            float climb = Lerp(1f, SustainedClimb, Clamp01(shot / (float)ClimbShots));
            float vertical = profile.VerticalKick * climb;

            // A sine sway whose speed and phase are fixed per weapon, pushed to one side by the bias.
            double phase = Hash01(profile.PatternSeed, 0x51ED) * Math.PI * 2.0;
            double frequency = 0.45 + 0.4 * Hash01(profile.PatternSeed, 0x2F7A);
            float sway = (float)Math.Sin(shot * frequency + phase);
            float bias = profile.HorizontalBias;
            float sideways = bias + (1f - Math.Abs(bias)) * sway;
            float horizontal = profile.HorizontalKick * sideways * Clamp01(shot / (float)SwayInShots);

            float jitterYaw = (Hash01(jitterSeed, shot * 2 + 1) * 2f - 1f) * profile.Jitter;
            float jitterPitch = (Hash01(jitterSeed, shot * 2 + 2) * 2f - 1f) * profile.Jitter * 0.5f;

            float pitch = vertical + jitterPitch;
            return new RecoilKick(pitch < 0f ? 0f : pitch, horizontal + jitterYaw);
        }

        /// <summary>
        /// How strongly recoil applies: the attachments' multiplier, eased toward <paramref name="aimMultiplier"/> as
        /// the sights come up, times <paramref name="crouchMultiplier"/> when crouched.
        /// </summary>
        public static float Scale(float attachmentMultiplier, float aim01, bool crouched, float aimMultiplier, float crouchMultiplier)
        {
            float scale = (attachmentMultiplier < 0f ? 0f : attachmentMultiplier) * Lerp(1f, aimMultiplier, Clamp01(aim01));
            if (crouched) scale *= crouchMultiplier;
            return scale < 0f ? 0f : scale;
        }

        /// <summary>
        /// Index of the shot fired at <paramref name="now"/>: the next in the spray, or 0 when more than
        /// <paramref name="resetSeconds"/> passed since the last shot (or there was none: <paramref name="lastIndex"/> &lt; 0).
        /// </summary>
        public static int NextShotIndex(int lastIndex, double lastShotTime, double now, double resetSeconds)
        {
            if (lastIndex < 0 || now - lastShotTime > resetSeconds) return 0;
            return lastIndex + 1;
        }

        /// <summary>Pause after which the spray starts over: one shot interval plus <paramref name="extraPause"/>.</summary>
        public static double ResetSeconds(double secondsBetweenShots, double extraPause) =>
            (secondsBetweenShots < 0.0 ? 0.0 : secondsBetweenShots) + (extraPause < 0.0 ? 0.0 : extraPause);

        /// <summary>Eases an offset back to zero: <paramref name="rate"/> per second, exponential (frame-rate independent).</summary>
        public static RecoilKick Recover(RecoilKick offset, float rate, float deltaTime)
        {
            if (rate <= 0f || deltaTime <= 0f) return offset;
            RecoilKick eased = offset * (float)Math.Exp(-rate * deltaTime);
            return eased.Magnitude < RestThreshold ? RecoilKick.Zero : eased;
        }

        /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> (exponential, <paramref name="sharpness"/> per second).</summary>
        public static RecoilKick Follow(RecoilKick current, RecoilKick target, float sharpness, float deltaTime)
        {
            if (sharpness <= 0f) return target;
            if (deltaTime <= 0f) return current;
            float t = 1f - (float)Math.Exp(-sharpness * deltaTime);
            return current + (target - current) * t;
        }

        /// <summary>
        /// Player look input against the recoil cancels the offset first. Returns the input that is left over for the
        /// view itself; <paramref name="absorbed"/> is how much of <paramref name="offset"/> was cancelled (same sign as the offset).
        /// </summary>
        /// <param name="offset">Current recoil offset on one axis.</param>
        /// <param name="input">Look input on the same axis, same sign convention.</param>
        public static float AbsorbInput(float offset, float input, out float absorbed)
        {
            absorbed = 0f;
            if (offset > 0f && input < 0f)
            {
                absorbed = Math.Min(-input, offset);
                return input + absorbed;
            }
            if (offset < 0f && input > 0f)
            {
                absorbed = -Math.Min(input, -offset);
                return input + absorbed;
            }
            return input;
        }

        /// <summary>Deterministic value in [0, 1) from two integers.</summary>
        public static float Hash01(int seed, int value)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)value * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h >> 8) / 16777216f;
            }
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
