using UnityEngine;

namespace ExtractionShooter.Gameplay.Audio
{
    /// <summary>
    /// Volume levels from the player's settings. Master goes straight to the <see cref="AudioListener"/>; the
    /// effects and interface levels are read by whoever plays those sounds (<see cref="CharacterAudio"/>,
    /// grenades, menu clicks), so no mixer asset is needed.
    /// </summary>
    public static class SoundVolumes
    {
        /// <summary>World sounds: gunshots, footsteps, grenades (0..1).</summary>
        public static float Effects { get; private set; } = 1f;

        /// <summary>Interface sounds (0..1).</summary>
        public static float Ui { get; private set; } = 0.8f;

        public static void Set(float master, float effects, float ui)
        {
            AudioListener.volume = Mathf.Clamp01(master);
            Effects = Mathf.Clamp01(effects);
            Ui = Mathf.Clamp01(ui);
        }
    }
}
