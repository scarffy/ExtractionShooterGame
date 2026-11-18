using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ExtractionShooter.Core.Hosting
{
    /// <summary>
    /// Settings of a dedicated server, read from an optional <c>key=value</c> file and the command line
    /// (the command line wins). Keys (case-insensitive, the same in both places):
    /// <list type="bullet">
    /// <item><c>name</c>: shown in the server list (1–40 characters).</item>
    /// <item><c>port</c>: UDP game port, 1–65535 (default: the transport's).</item>
    /// <item><c>region</c>: short code such as <c>sg</c> or <c>my-kl</c> ([a-z0-9-], at most 16).</item>
    /// <item><c>master</c>: master server base URL (http:// or https://); empty = do not announce.</item>
    /// <item><c>token</c>: the master's shared secret. Never logged.</item>
    /// <item><c>publicHost</c>: address players should connect to, when the master would see another one (NAT, proxy).</item>
    /// <item><c>maxPlayers</c>: raid size override, 1–128 (default: the lobby's).</item>
    /// <item><c>heartbeatSeconds</c>: seconds between announcements, 2–25 (default 10; the master forgets a server after 30).</item>
    /// </list>
    /// Command line: <c>-name "Kim's server" -port 7771 -config /srv/es/server.cfg</c> (also <c>--key value</c> and
    /// <c>-key=value</c>). Without <c>-config</c> the first <c>server.cfg</c> found in the search folders is used.
    /// Invalid values are reported as warnings and keep the previous value. Pure C#: the caller passes file access.
    /// </summary>
    public sealed class ServerConfig
    {
        public const string DefaultFileName = "server.cfg";
        public const string DefaultName = "Extraction server";
        public const float DefaultHeartbeatSeconds = 10f;
        public const float MaxHeartbeatSeconds = 25f;
        public const string ConfigArgument = "config";

        private static readonly string[] Keys = { "name", "port", "region", "master", "token", "publichost", "maxplayers", "heartbeatseconds" };

        /// <summary>Server list name.</summary>
        public string Name { get; private set; } = DefaultName;

        /// <summary>UDP game port, or 0 to keep the transport's.</summary>
        public ushort Port { get; private set; }

        /// <summary>Region code, or empty.</summary>
        public string Region { get; private set; } = string.Empty;

        /// <summary>Normalized master server base URL, or empty when the server is not announced.</summary>
        public string MasterUrl { get; private set; } = string.Empty;

        /// <summary>Shared secret for the master server, or empty. Never log it.</summary>
        public string Token { get; private set; } = string.Empty;

        /// <summary>Host sent in heartbeats, or empty to let the master use the request's address.</summary>
        public string PublicHost { get; private set; } = string.Empty;

        /// <summary>Raid size override, or 0 to keep the lobby's.</summary>
        public int MaxPlayers { get; private set; }

        public float HeartbeatSeconds { get; private set; } = DefaultHeartbeatSeconds;

        /// <summary>The config file that was read, or null.</summary>
        public string SourceFile { get; private set; }

        /// <summary>True when a master server is configured.</summary>
        public bool Announces => MasterUrl.Length > 0;

        /// <summary>
        /// Reads the config file (<c>-config</c>, or the first <see cref="DefaultFileName"/> in <paramref name="searchDirectories"/>)
        /// and then the command line over it.
        /// </summary>
        /// <param name="args">Command line arguments (Environment.GetCommandLineArgs; the executable name is ignored).</param>
        /// <param name="searchDirectories">Folders to look for server.cfg in, in order (working folder, executable folder).</param>
        /// <param name="fileExists">File.Exists.</param>
        /// <param name="readFile">File.ReadAllText; may throw.</param>
        /// <param name="warnings">Problems found (bad values, unknown keys, unreadable file).</param>
        public static ServerConfig Load(IReadOnlyList<string> args, IEnumerable<string> searchDirectories,
            Func<string, bool> fileExists, Func<string, string> readFile, List<string> warnings)
        {
            var config = new ServerConfig();
            args = args ?? Array.Empty<string>();

            string path = FindArgument(args, ConfigArgument, out bool hasValue);
            if (path != null || hasValue)
            {
                if (string.IsNullOrWhiteSpace(path)) warnings?.Add("-config: no file given.");
                else if (!fileExists(path)) warnings?.Add($"-config: file not found: {path}");
                else config.ReadFile(path, readFile, warnings);
            }
            else if (searchDirectories != null)
            {
                foreach (string directory in searchDirectories)
                {
                    if (string.IsNullOrEmpty(directory)) continue;
                    string candidate = Path.Combine(directory, DefaultFileName);
                    if (!fileExists(candidate)) continue;
                    config.ReadFile(candidate, readFile, warnings);
                    break;
                }
            }

            config.ApplyArguments(args, warnings);
            return config;
        }

        /// <summary>Applies <c>key=value</c> lines. Blank lines and lines starting with #, ; or // are skipped; values may be quoted.</summary>
        public void ApplyText(string text, string sourceName, List<string> warnings)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (i == 0 && line.Length > 0 && line[0] == '﻿') line = line.Substring(1).Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line.StartsWith("//", StringComparison.Ordinal)) continue;

                string where = $"{sourceName} line {i + 1}";
                int equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    warnings?.Add($"{where}: expected key=value.");
                    continue;
                }
                string key = line.Substring(0, equals).Trim();
                if (!IsKnownKey(key))
                {
                    warnings?.Add($"{where}: unknown key '{key}'.");
                    continue;
                }
                Set(key, Unquote(line.Substring(equals + 1).Trim()), where, warnings);
            }
        }

        /// <summary>
        /// Applies <c>-key value</c>, <c>--key value</c> and <c>-key=value</c> for the known keys. Other arguments
        /// (Unity's own: -batchmode, -nographics, -logFile …) are ignored.
        /// </summary>
        public void ApplyArguments(IReadOnlyList<string> args, List<string> warnings)
        {
            if (args == null) return;
            for (int i = 0; i < args.Count; i++)
            {
                if (!TrySplitOption(args[i], out string key, out string inlineValue) || !IsKnownKey(key)) continue;
                string value = inlineValue;
                if (value == null)
                {
                    if (i + 1 >= args.Count || IsOption(args[i + 1]))
                    {
                        warnings?.Add($"-{key}: no value given.");
                        continue;
                    }
                    value = args[++i];
                }
                Set(key, Unquote(value.Trim()), "-" + key, warnings);
            }
        }

        /// <summary>Sets one key after validating it. False (with a warning) when the key is unknown or the value is invalid.</summary>
        public bool Set(string key, string value, string source, List<string> warnings)
        {
            value = value ?? string.Empty;
            switch ((key ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "name":
                    string name = MasterServerApi.SanitizeName(value);
                    if (name.Length == 0) return Warn(warnings, source, $"name: 1–{MasterServerApi.NameMaxLength} characters.");
                    if (name.Length < value.Trim().Length) warnings?.Add($"{source}: name shortened to '{name}'.");
                    Name = name;
                    return true;

                case "port":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
                        return Warn(warnings, source, "port: a number from 1 to 65535.");
                    Port = (ushort)port;
                    return true;

                case "region":
                    if (!MasterServerApi.TryNormalizeRegion(value, out string region))
                        return Warn(warnings, source, $"region: at most {MasterServerApi.RegionMaxLength} of a-z, 0-9 and '-'.");
                    Region = region;
                    return true;

                case "master":
                    if (value.Length == 0)
                    {
                        MasterUrl = string.Empty;
                        return true;
                    }
                    if (!MasterServerApi.TryNormalizeBaseUrl(value, out string url))
                        return Warn(warnings, source, "master: an http:// or https:// address such as https://master.example.com.");
                    MasterUrl = url;
                    return true;

                case "token":
                    Token = value; // never echoed in a warning
                    return true;

                case "publichost":
                    if (!MasterServerApi.IsValidHost(value))
                        return Warn(warnings, source, "publicHost: a hostname or IP address (at most 253 characters).");
                    PublicHost = value;
                    return true;

                case "maxplayers":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxPlayers)
                        || maxPlayers < 1 || maxPlayers > MasterServerApi.MaxPlayersLimit)
                        return Warn(warnings, source, $"maxPlayers: a number from 1 to {MasterServerApi.MaxPlayersLimit}.");
                    MaxPlayers = maxPlayers;
                    return true;

                case "heartbeatseconds":
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds)
                        || float.IsNaN(seconds) || float.IsInfinity(seconds))
                        return Warn(warnings, source, "heartbeatSeconds: a number of seconds.");
                    float clamped = Math.Max(MasterServerApi.MinHeartbeatSeconds, Math.Min(MaxHeartbeatSeconds, seconds));
                    if (clamped != seconds)
                        warnings?.Add($"{source}: heartbeatSeconds kept within {MasterServerApi.MinHeartbeatSeconds:0}–{MaxHeartbeatSeconds:0} " +
                                      $"(the master forgets a server after {MasterServerApi.TtlSeconds} s).");
                    HeartbeatSeconds = clamped;
                    return true;

                default:
                    return Warn(warnings, source, $"unknown key '{key}'.");
            }
        }

        /// <summary>One line for the log. The token is shown only as set / not set.</summary>
        public string ToLogString()
        {
            var builder = new StringBuilder();
            builder.Append("name=\"").Append(Name).Append('"');
            builder.Append(" port=").Append(Port == 0 ? "(transport default)" : Port.ToString(CultureInfo.InvariantCulture));
            builder.Append(" region=").Append(Region.Length > 0 ? Region : "(none)");
            builder.Append(" master=").Append(Announces ? MasterUrl : "(none: not announced)");
            builder.Append(" token=").Append(Token.Length > 0 ? "(set)" : "(none)");
            builder.Append(" publicHost=").Append(PublicHost.Length > 0 ? PublicHost : "(auto)");
            builder.Append(" maxPlayers=").Append(MaxPlayers > 0 ? MaxPlayers.ToString(CultureInfo.InvariantCulture) : "(lobby)");
            builder.Append(" heartbeatSeconds=").Append(HeartbeatSeconds.ToString("0.##", CultureInfo.InvariantCulture));
            builder.Append(" config=").Append(SourceFile ?? "(none)");
            return builder.ToString();
        }

        // ---------------- Internals ----------------

        private void ReadFile(string path, Func<string, string> readFile, List<string> warnings)
        {
            string text;
            try
            {
                text = readFile(path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                                              || exception is NotSupportedException || exception is System.Security.SecurityException)
            {
                warnings?.Add($"Could not read {path}: {exception.Message}");
                return;
            }
            SourceFile = path;
            ApplyText(text, Path.GetFileName(path), warnings);
        }

        /// <summary>The value after <c>-name</c> (or <c>-name=value</c>), or null. <paramref name="present"/>: the option was given at all.</summary>
        private static string FindArgument(IReadOnlyList<string> args, string name, out bool present)
        {
            present = false;
            for (int i = 0; i < args.Count; i++)
            {
                if (!TrySplitOption(args[i], out string key, out string inlineValue)
                    || !string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
                present = true;
                if (inlineValue != null) return Unquote(inlineValue.Trim());
                return i + 1 < args.Count && !IsOption(args[i + 1]) ? Unquote(args[i + 1].Trim()) : null;
            }
            return null;
        }

        private static bool TrySplitOption(string arg, out string key, out string inlineValue)
        {
            key = null;
            inlineValue = null;
            if (!IsOption(arg)) return false;
            string body = arg.TrimStart('-');
            int equals = body.IndexOf('=');
            if (equals >= 0)
            {
                inlineValue = body.Substring(equals + 1);
                body = body.Substring(0, equals);
            }
            key = body;
            return key.Length > 0;
        }

        /// <summary>"-x" or "--x" with a letter after the dashes (so "-5" stays a value).</summary>
        private static bool IsOption(string arg)
        {
            if (string.IsNullOrEmpty(arg) || arg[0] != '-') return false;
            int i = arg.Length > 1 && arg[1] == '-' ? 2 : 1;
            return i < arg.Length && char.IsLetter(arg[i]);
        }

        private static bool IsKnownKey(string key)
        {
            string lower = (key ?? string.Empty).Trim().ToLowerInvariant();
            return Array.IndexOf(Keys, lower) >= 0;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                return value.Substring(1, value.Length - 2);
            return value;
        }

        private static bool Warn(List<string> warnings, string source, string message)
        {
            warnings?.Add($"{source}: {message}");
            return false;
        }
    }
}
