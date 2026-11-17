using System.Collections.Generic;

namespace ExtractionShooter.Core.Navigation
{
    /// <summary>
    /// Supplies the local player's squad (including the local player) to the map and tactical panel.
    /// The squad system implements this and registers it with <see cref="TeammateRoster.Register"/>; until one is
    /// registered the UI shows the local player alone.
    /// </summary>
    public interface ITeammateSource
    {
        /// <summary>Clears <paramref name="results"/> and fills it with the squad, local player first.</summary>
        void GetTeammates(List<TeammateInfo> results);
    }

    /// <summary>Where the UI finds the active <see cref="ITeammateSource"/>. One source at a time.</summary>
    public static class TeammateRoster
    {
        /// <summary>The registered squad source, or null (solo).</summary>
        public static ITeammateSource Source { get; private set; }

        public static void Register(ITeammateSource source) => Source = source;

        /// <summary>Removes the source if it is the one registered (safe to call from OnDisable/OnDestroy).</summary>
        public static void Unregister(ITeammateSource source)
        {
            if (ReferenceEquals(Source, source)) Source = null;
        }
    }
}
