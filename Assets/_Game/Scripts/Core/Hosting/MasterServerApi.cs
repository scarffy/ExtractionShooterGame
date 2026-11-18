using System;
using System.Text;
using ExtractionShooter.Core.Raid;

namespace ExtractionShooter.Core.Hosting
{
    /// <summary>
    /// Body of <c>POST /api/servers/heartbeat</c> (game server → master server). Public camelCase fields so Unity's
    /// <c>JsonUtility</c> writes exactly the names in the master server contract (Docs/MASTER_API.md): do not rename.
    /// Build it with <see cref="MasterServerApi.BuildHeartbeat"/>, which keeps every value inside the contract's limits.
    /// </summary>
    [Serializable]
    public sealed class HeartbeatRequest
    {
        public string id = string.Empty;
        public string name = string.Empty;
        public string host = string.Empty;
        public int port;
        public string region = string.Empty;
        public string map = string.Empty;
        public string difficulties = string.Empty;
        public int players;
        public int maxPlayers;
        public bool raidRunning;
        public string raidDifficulty = string.Empty;
        public int raidSecondsLeft;
        public string version = string.Empty;
    }

    /// <summary>One server in the master server's list (<c>GET /api/servers</c>). Field names are the contract's.</summary>
    [Serializable]
    public sealed class ServerEntry
    {
        public string id = string.Empty;
        public string name = string.Empty;
        public string host = string.Empty;
        public int port;
        public string region = string.Empty;
        public string map = string.Empty;
        public string difficulties = string.Empty;
        public int players;
        public int maxPlayers;
        public bool raidRunning;
        public string raidDifficulty = string.Empty;
        public int raidSecondsLeft;
        public string version = string.Empty;
        public int lastSeenSecondsAgo;
    }

    /// <summary>Response of <c>GET /api/servers</c>: <c>{"servers":[…]}</c>.</summary>
    [Serializable]
    public sealed class ServerListResponse
    {
        public ServerEntry[] servers;
    }

    /// <summary>The master's answer to a heartbeat or removal: <c>{"ok":true,"ttlSeconds":30}</c> or <c>{"ok":false,"error":"…"}</c>.</summary>
    [Serializable]
    public sealed class MasterServerReply
    {
        public bool ok;
        public string error = string.Empty;
        public int ttlSeconds;
    }

    /// <summary>What a game server reports about itself in a heartbeat (raw values; the builder clamps them).</summary>
    public struct ServerStatus
    {
        public string Name;
        /// <summary>Host override, or empty to let the master use the request's address.</summary>
        public string Host;
        public int Port;
        public string Region;
        public string Map;
        public MapDifficulties Difficulties;
        /// <summary>Signed-in players.</summary>
        public int Players;
        /// <summary>Players one raid takes.</summary>
        public int MaxPlayers;
        public bool RaidRunning;
        public RaidDifficulty RaidDifficulty;
        public double RaidSecondsLeft;
        public string Version;
    }

    /// <summary>
    /// The master server contract on the game side: paths, limits, validation rules and the heartbeat builder.
    /// Pure C#; the HTTP calls are made by <c>MasterServerAnnouncer</c> (server) and the server browser (client).
    /// </summary>
    public static class MasterServerApi
    {
        public const string HeartbeatPath = "/api/servers/heartbeat";
        public const string ServersPath = "/api/servers";

        public const int IdMinLength = 8, IdMaxLength = 64;
        public const int NameMaxLength = 40;
        public const int HostMaxLength = 253;
        public const int RegionMaxLength = 16;
        public const int MapMaxLength = 32;
        public const int VersionMaxLength = 32;
        public const int MaxPlayersLimit = 128;
        public const int MaxRaidSeconds = 86400;

        /// <summary>A server not heard from for this long drops off the list.</summary>
        public const int TtlSeconds = 30;

        /// <summary>The master refuses more than one heartbeat per server this often.</summary>
        public const float MinHeartbeatSeconds = 2f;

        /// <summary>PlayerPrefs key of the master server address (server browser; also the editor's dev server).</summary>
        public const string MasterUrlPrefsKey = "ExtractionShooter.MasterUrl";

        /// <summary>A new server id: a GUID in "N" format (32 hex digits). Make one per server process.</summary>
        public static string NewServerId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Checks a master server base address: an absolute http:// or https:// URL. Trims it and drops trailing slashes
        /// ("https://master.example.com/" → "https://master.example.com"). False when empty or not such a URL.
        /// </summary>
        public static bool TryNormalizeBaseUrl(string input, out string url)
        {
            url = string.Empty;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string trimmed = input.Trim().TrimEnd('/');
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
            url = trimmed;
            return true;
        }

        public static string HeartbeatUrl(string baseUrl) => baseUrl + HeartbeatPath;
        public static string ListUrl(string baseUrl) => baseUrl + ServersPath;
        public static string ServerUrl(string baseUrl, string id) => baseUrl + ServersPath + "/" + Uri.EscapeDataString(id ?? string.Empty);

        /// <summary>The contract's id rule: 8–64 characters of [A-Za-z0-9_-].</summary>
        public static bool IsValidId(string id)
        {
            if (id == null || id.Length < IdMinLength || id.Length > IdMaxLength) return false;
            foreach (char c in id)
                if (!IsAsciiLetterOrDigit(c) && c != '_' && c != '-') return false;
            return true;
        }

        /// <summary>A server name as the master accepts it: control characters removed, trimmed, at most 40 characters. Empty when nothing is left.</summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
                if (!char.IsControl(c)) builder.Append(c);
            string result = builder.ToString().Trim();
            if (result.Length > NameMaxLength) result = result.Substring(0, NameMaxLength).TrimEnd();
            return result;
        }

        /// <summary>A region code: lower case, 0–16 characters of [a-z0-9-] (empty is allowed). False when it breaks the rule.</summary>
        public static bool TryNormalizeRegion(string region, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(region)) return true;
            string lower = region.Trim().ToLowerInvariant();
            if (lower.Length > RegionMaxLength) return false;
            foreach (char c in lower)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false;
            normalized = lower;
            return true;
        }

        /// <summary>A host override: empty, or at most 253 characters of a hostname or an IPv4 / IPv6 literal.</summary>
        public static bool IsValidHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return true;
            if (host.Length > HostMaxLength) return false;
            foreach (char c in host)
                if (!IsAsciiLetterOrDigit(c) && c != '.' && c != '-' && c != ':' && c != '_' && c != '%') return false;
            return true;
        }

        /// <summary>The contract's name of a map's difficulty pair: "EasyNormal" or "NormalHard".</summary>
        public static string DifficultiesName(MapDifficulties difficulties) =>
            difficulties == MapDifficulties.NormalHard ? "NormalHard" : "EasyNormal";

        /// <summary>
        /// The heartbeat for a server, every value inside the contract's limits: name sanitized (falls back to
        /// "Extraction server"), region dropped when invalid, map and version cut to 32 characters, max players 1–128,
        /// players 0..max, seconds left 0–86400 (rounded up), raid difficulty empty when no raid runs.
        /// </summary>
        public static HeartbeatRequest BuildHeartbeat(string id, ServerStatus status)
        {
            int maxPlayers = Clamp(status.MaxPlayers, 1, MaxPlayersLimit);
            string name = SanitizeName(status.Name);
            string map = Cut(SanitizeName(status.Map), MapMaxLength);
            string version = Cut(SanitizeName(status.Version), VersionMaxLength);
            double secondsLeft = status.RaidRunning && !double.IsNaN(status.RaidSecondsLeft) ? Math.Ceiling(status.RaidSecondsLeft) : 0d;

            return new HeartbeatRequest
            {
                id = id ?? string.Empty,
                name = name.Length > 0 ? name : ServerConfig.DefaultName,
                host = IsValidHost(status.Host) ? status.Host ?? string.Empty : string.Empty,
                port = Clamp(status.Port, 1, 65535),
                region = TryNormalizeRegion(status.Region, out string region) ? region : string.Empty,
                map = map.Length > 0 ? map : "Unknown",
                difficulties = DifficultiesName(status.Difficulties),
                players = Clamp(status.Players, 0, maxPlayers),
                maxPlayers = maxPlayers,
                raidRunning = status.RaidRunning,
                raidDifficulty = status.RaidRunning ? status.RaidDifficulty.DisplayName() : string.Empty,
                raidSecondsLeft = (int)Math.Max(0d, Math.Min(MaxRaidSeconds, secondsLeft)),
                version = version.Length > 0 ? version : "0",
            };
        }

        private static string Cut(string text, int max) => text.Length > max ? text.Substring(0, max).TrimEnd() : text;

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private static bool IsAsciiLetterOrDigit(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
    }

    /// <summary>
    /// When the next heartbeat goes out: the normal interval while the master answers; after failures the interval
    /// doubles up to a cap, never sooner than the interval (the master allows one heartbeat per 2 s per server).
    /// </summary>
    public static class HeartbeatSchedule
    {
        /// <summary>
        /// Seconds to wait before the next heartbeat. No failures: <paramref name="intervalSeconds"/>. After failures:
        /// interval × 1, 2, 4, 8 … capped at <paramref name="maxBackoffSeconds"/> (10 s interval: 10, 20, 40, 60, 60 …).
        /// </summary>
        public static float NextDelaySeconds(float intervalSeconds, int consecutiveFailures, float maxBackoffSeconds)
        {
            float interval = Math.Max(MasterServerApi.MinHeartbeatSeconds, intervalSeconds);
            if (consecutiveFailures <= 0) return interval;
            float cap = Math.Max(interval, maxBackoffSeconds);
            int exponent = Math.Min(consecutiveFailures - 1, 16);
            return Math.Min(cap, interval * (1 << exponent));
        }

        /// <summary>The longest wait (used straight away when retrying cannot help soon, e.g. a rejected token).</summary>
        public static float CeilingSeconds(float intervalSeconds, float maxBackoffSeconds) =>
            Math.Max(Math.Max(MasterServerApi.MinHeartbeatSeconds, intervalSeconds), maxBackoffSeconds);
    }
}
