using System.Collections.Generic;
using ExtractionShooter.Core.Navigation;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Session;
using UnityEngine;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// The squad list the map and tactical panel show. Uses the registered <see cref="TeammateRoster.Source"/>
    /// (the squad system's hook); without one, the local player alone.
    /// </summary>
    internal static class TacticalSquad
    {
        /// <summary>Clears <paramref name="results"/> and fills it with the squad, local player first.</summary>
        public static void Collect(PlayerCharacter localPlayer, List<TeammateInfo> results)
        {
            results.Clear();

            ITeammateSource source = TeammateRoster.Source;
            if (source != null)
            {
                source.GetTeammates(results);
                if (results.Count > 0) return;
            }

            if (localPlayer != null) results.Add(Describe(localPlayer, true));
        }

        /// <summary>A player character as a squad entry (name from the session for the local player).</summary>
        public static TeammateInfo Describe(PlayerCharacter character, bool isLocal)
        {
            string name = isLocal && PlayerSession.Local != null && !string.IsNullOrEmpty(PlayerSession.Local.PlayerName)
                ? PlayerSession.Local.PlayerName
                : character.name;
            float health01 = character.Health.Max > 0 ? (float)character.Health.Current / character.Health.Max : 0f;
            Vector3 position = character.transform.position;
            return new TeammateInfo(name, isLocal, character.Health.IsAlive, health01, position.x, position.z,
                NavigationMath.BearingFromYaw(character.Look.Yaw));
        }
    }
}
