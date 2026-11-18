using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExtractionShooter.Core.Hosting
{
    /// <summary>How the server browser orders the list.</summary>
    public enum ServerSortOrder : byte
    {
        /// <summary>Most players first, then by name (the master's order).</summary>
        Players = 0,

        /// <summary>Lowest ping first; servers without a ping last.</summary>
        Ping = 1,
    }

    /// <summary>What the server browser shows.</summary>
    public struct ServerBrowserFilter
    {
        /// <summary>Text to find in the name, map or region (case-insensitive); empty shows all.</summary>
        public string Search;

        /// <summary>Only this region; empty for all.</summary>
        public string Region;

        /// <summary>Also list servers running another game version (they cannot be joined).</summary>
        public bool ShowOtherVersions;

        /// <summary>This game's version.</summary>
        public string Version;
    }

    /// <summary>
    /// The server browser's list logic: filtering, sorting and the text of each row. Pure C#; the panel
    /// (<c>ServerBrowserPanel</c>) fetches the list and measures pings.
    /// </summary>
    public static class ServerBrowserList
    {
        /// <summary>Shown where a value is unknown (ping, region).</summary>
        public const string Unknown = "—";

        /// <summary>The servers to show, in order. <paramref name="pingMs"/> gives a server's ping in ms, or -1 when unknown.</summary>
        public static List<ServerEntry> Apply(IReadOnlyList<ServerEntry> servers, ServerBrowserFilter filter, ServerSortOrder order,
            Func<ServerEntry, int> pingMs = null)
        {
            var result = new List<ServerEntry>();
            if (servers == null) return result;
            foreach (ServerEntry entry in servers)
                if (entry != null && Matches(entry, filter)) result.Add(entry);

            Comparison<ServerEntry> byPlayers = ComparePlayers;
            if (order == ServerSortOrder.Ping && pingMs != null)
            {
                result.Sort((a, b) =>
                {
                    int pa = pingMs(a), pb = pingMs(b);
                    bool ka = pa >= 0, kb = pb >= 0;
                    if (ka != kb) return ka ? -1 : 1;
                    if (ka && pa != pb) return pa.CompareTo(pb);
                    return byPlayers(a, b);
                });
            }
            else
            {
                result.Sort(byPlayers);
            }
            return result;
        }

        /// <summary>Whether a server passes the filter (version, region, search text).</summary>
        public static bool Matches(ServerEntry entry, ServerBrowserFilter filter)
        {
            if (entry == null) return false;
            if (!filter.ShowOtherVersions && !IsVersionMatch(entry, filter.Version)) return false;
            if (!string.IsNullOrEmpty(filter.Region) && !string.Equals(entry.region ?? string.Empty, filter.Region, StringComparison.OrdinalIgnoreCase))
                return false;

            string search = filter.Search != null ? filter.Search.Trim() : string.Empty;
            if (search.Length == 0) return true;
            return Contains(entry.name, search) || Contains(entry.map, search) || Contains(entry.region, search);
        }

        /// <summary>Same game version (exact, ordinal): only those can be joined.</summary>
        public static bool IsVersionMatch(ServerEntry entry, string version) =>
            entry != null && string.Equals(entry.version ?? string.Empty, version ?? string.Empty, StringComparison.Ordinal);

        /// <summary>Most players first, then name (case-insensitive), then address, so the order is stable.</summary>
        public static int ComparePlayers(ServerEntry a, ServerEntry b)
        {
            int players = b.players.CompareTo(a.players);
            if (players != 0) return players;
            int name = string.Compare(a.name ?? string.Empty, b.name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            return name != 0 ? name : string.CompareOrdinal(EndpointKey(a), EndpointKey(b));
        }

        /// <summary>The regions in the list, sorted, without duplicates or blanks.</summary>
        public static List<string> Regions(IReadOnlyList<ServerEntry> servers)
        {
            var regions = new List<string>();
            if (servers == null) return regions;
            foreach (ServerEntry entry in servers)
            {
                string region = entry?.region;
                if (string.IsNullOrEmpty(region) || regions.Contains(region)) continue;
                regions.Add(region);
            }
            regions.Sort(StringComparer.Ordinal);
            return regions;
        }

        /// <summary>"host:port": identifies a server for pings and row updates.</summary>
        public static string EndpointKey(ServerEntry entry) =>
            entry == null ? string.Empty : (entry.host ?? string.Empty) + ":" + entry.port.ToString(CultureInfo.InvariantCulture);

        // ---------------- Text ----------------

        /// <summary>"3/12".</summary>
        public static string PlayersText(ServerEntry entry) =>
            entry.players.ToString(CultureInfo.InvariantCulture) + "/" + entry.maxPlayers.ToString(CultureInfo.InvariantCulture);

        /// <summary>Seconds left in the raid now: the reported value, minus its age at fetch time and the time since the fetch.</summary>
        public static int SecondsLeftNow(ServerEntry entry, double secondsSinceFetch)
        {
            if (entry == null || !entry.raidRunning) return 0;
            double left = entry.raidSecondsLeft - Math.Max(0, entry.lastSeenSecondsAgo) - Math.Max(0d, secondsSinceFetch);
            return left > 0d ? (int)Math.Ceiling(left) : 0;
        }

        /// <summary>"Raid running · Normal · 13:32 left", "Raid running · Hard · ending", or "No raid running".</summary>
        public static string RaidText(ServerEntry entry, double secondsSinceFetch = 0d)
        {
            if (entry == null || !entry.raidRunning) return "No raid running";
            string text = "Raid running";
            if (!string.IsNullOrEmpty(entry.raidDifficulty)) text += " · " + entry.raidDifficulty;
            int left = SecondsLeftNow(entry, secondsSinceFetch);
            return text + " · " + (left > 0 ? FormatDuration(left) + " left" : "ending");
        }

        /// <summary>"3/12 · Raid running · Normal · 13:32 left".</summary>
        public static string Summary(ServerEntry entry, double secondsSinceFetch = 0d) =>
            PlayersText(entry) + " · " + RaidText(entry, secondsSinceFetch);

        /// <summary>"EasyNormal" → "Easy/Normal", "NormalHard" → "Normal/Hard"; anything else as sent.</summary>
        public static string DifficultiesText(string difficulties)
        {
            switch (difficulties)
            {
                case "EasyNormal": return "Easy/Normal";
                case "NormalHard": return "Normal/Hard";
                default: return string.IsNullOrEmpty(difficulties) ? Unknown : difficulties;
            }
        }

        /// <summary>"13:32", or "1:02:03" from an hour.</summary>
        public static string FormatDuration(int seconds)
        {
            if (seconds < 0) seconds = 0;
            int hours = seconds / 3600, minutes = seconds / 60 % 60, secs = seconds % 60;
            return hours > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, secs)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, secs);
        }

        /// <summary>"42 ms", or "—" when unknown (negative).</summary>
        public static string PingText(int pingMs) => pingMs < 0 ? Unknown : pingMs.ToString(CultureInfo.InvariantCulture) + " ms";

        /// <summary>"SG", "MY-KL", or "—".</summary>
        public static string RegionText(string region) => string.IsNullOrEmpty(region) ? Unknown : region.ToUpperInvariant();

        private static bool Contains(string text, string search) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
