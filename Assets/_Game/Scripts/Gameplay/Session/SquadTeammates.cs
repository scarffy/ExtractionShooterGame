using System.Collections.Generic;
using ExtractionShooter.Core.Navigation;
using ExtractionShooter.Gameplay.Player;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>
    /// Feeds the map and tactical panel with the local player's squad during a raid: the local player first, then
    /// squadmates, from the replicated <see cref="SquadMember"/> tags. Registered for the whole session.
    /// </summary>
    public sealed class SquadTeammates : ITeammateSource
    {
        private static readonly SquadTeammates Instance = new SquadTeammates();

        public void GetTeammates(List<TeammateInfo> results)
        {
            PlayerCharacter local = PlayerCharacter.Local;
            if (local == null) return;
            SquadMember localMember = local.GetComponent<SquadMember>();
            Vector3 origin = local.transform.position;

            results.Add(Describe(local, localMember, true, origin));
            if (localMember == null) return;

            foreach (SquadMember member in SquadMember.ClientAllMembers)
            {
                if (member == null || !localMember.IsSquadmate(member)) continue;
                PlayerCharacter character = member.GetComponent<PlayerCharacter>();
                if (character != null) results.Add(Describe(character, member, false, origin));
            }
        }

        private static TeammateInfo Describe(PlayerCharacter character, SquadMember member, bool isLocal, Vector3 origin)
        {
            Vector3 position = character.transform.position;
            string name = member != null && !string.IsNullOrEmpty(member.PlayerName) ? member.PlayerName : isLocal ? "You" : "Teammate";
            float health01 = character.Health != null && character.Health.Max > 0 ? (float)character.Health.Current / character.Health.Max : 0f;
            float bearing = NavigationMath.BearingTo(origin.x, origin.z, position.x, position.z);
            return new TeammateInfo(name, isLocal, character.Health != null && character.Health.IsAlive, health01, position.x, position.z, bearing);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register() => TeammateRoster.Register(Instance);
    }
}
