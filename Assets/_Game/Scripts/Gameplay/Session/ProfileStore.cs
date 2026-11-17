using System;
using System.IO;
using ExtractionShooter.Core.Persistence;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>
    /// Server only: profiles as JSON files, one per player name, under
    /// <c>persistentDataPath/Profiles</c>. Writes go to a temporary file first and then replace the
    /// old one, so a crash mid-write never leaves a half-written profile.
    /// </summary>
    public sealed class ProfileStore
    {
        private const string Extension = ".json";

        private readonly string _directory;

        public ProfileStore(string directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        public static ProfileStore CreateDefault() =>
            new ProfileStore(Path.Combine(Application.persistentDataPath, "Profiles"));

        public string Directory => _directory;

        /// <summary>
        /// Loads a profile. False when none exists. A file that cannot be read is moved aside
        /// (".corrupt") and logged, so the player gets a fresh profile instead of a stuck one.
        /// </summary>
        public bool TryLoad(string playerName, out PlayerProfile profile)
        {
            profile = null;
            string path = PathFor(playerName);
            if (!File.Exists(path)) return false;

            try
            {
                profile = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(path));
                if (profile == null) throw new InvalidDataException("Empty profile.");
                profile.PlayerName = playerName;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[{nameof(ProfileStore)}] Could not read profile '{path}': {exception.Message}. Starting a new one.");
                TryMoveAside(path);
                profile = null;
                return false;
            }
        }

        public void Save(PlayerProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.PlayerName)) return;

            string path = PathFor(profile.PlayerName);
            string temporary = path + ".tmp";
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.WriteAllText(temporary, JsonUtility.ToJson(profile, true));

                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[{nameof(ProfileStore)}] Could not save profile '{path}': {exception.Message}");
            }
        }

        private string PathFor(string playerName) => Path.Combine(_directory, ProfileNames.ToKey(playerName) + Extension);

        private static void TryMoveAside(string path)
        {
            try
            {
                string aside = path + ".corrupt";
                if (File.Exists(aside)) File.Delete(aside);
                File.Move(path, aside);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[{nameof(ProfileStore)}] Could not move aside '{path}': {exception.Message}");
            }
        }
    }
}
