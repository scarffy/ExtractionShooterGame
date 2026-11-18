using System;
using ExtractionShooter.Core.Settings;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Settings
{
    /// <summary>
    /// The player's <see cref="GameSettings"/> for this machine, kept in PlayerPrefs. Gameplay reads
    /// <see cref="Current"/> where it needs a value (look speed, field of view), so a change applies on the next frame;
    /// the menu applies audio and display settings when <see cref="Changed"/> fires.
    /// </summary>
    public static class GameSettingsStore
    {
        private const string PrefsKey = "ExtractionShooter.Settings";

        private static GameSettings _current;

        /// <summary>Raised after <see cref="Save"/> with the new settings.</summary>
        public static event Action<GameSettings> Changed;

        /// <summary>The settings in use. Treat as read-only: to change them, <see cref="GameSettings.Clone"/>, edit, <see cref="Save"/>.</summary>
        public static GameSettings Current => _current ??= GameSettings.Parse(PlayerPrefs.GetString(PrefsKey, string.Empty));

        /// <summary>
        /// Makes <paramref name="settings"/> current and raises <see cref="Changed"/>. Written to PlayerPrefs in memory only
        /// (a slider saves every step); call <c>PlayerPrefs.Save()</c> when done editing. Unity also writes on quit.
        /// </summary>
        public static void Save(GameSettings settings)
        {
            if (settings == null) return;
            _current = settings.Clone();
            PlayerPrefs.SetString(PrefsKey, _current.Serialize());
            Changed?.Invoke(_current);
        }

        // Static state survives play mode when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            Changed = null;
        }
    }
}
