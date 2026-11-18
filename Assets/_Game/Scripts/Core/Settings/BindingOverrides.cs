using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ExtractionShooter.Core.Settings
{
    /// <summary>One rebound key: the binding at <see cref="Index"/> of action <see cref="Action"/> uses <see cref="Path"/>.</summary>
    public readonly struct BindingOverride
    {
        public readonly string Action;
        public readonly int Index;

        /// <summary>The binding's path when the override was made. If the game's default changes, the override is dropped.</summary>
        public readonly string DefaultPath;

        /// <summary>The control path the player chose, e.g. "&lt;Keyboard&gt;/f".</summary>
        public readonly string Path;

        public BindingOverride(string action, int index, string defaultPath, string path)
        {
            Action = action;
            Index = index;
            DefaultPath = defaultPath;
            Path = path;
        }
    }

    /// <summary>
    /// The player's key rebinds, keyed by action name and binding index. Actions are created in code, so their binding
    /// ids change every run; name + index + the default path identify a binding across runs instead.
    /// An override whose recorded default no longer matches the action's binding is ignored (the game changed it).
    /// Stored as tab-separated lines: action, index, default path, path.
    /// </summary>
    public sealed class BindingOverrides
    {
        private readonly Dictionary<string, BindingOverride> _entries = new Dictionary<string, BindingOverride>(StringComparer.Ordinal);

        public int Count => _entries.Count;

        public IEnumerable<BindingOverride> All => _entries.Values;

        /// <summary>Records a rebind. Choosing the default path again (or an empty path) removes the override.</summary>
        public void Set(string action, int index, string defaultPath, string path)
        {
            if (!IsStorable(action) || index < 0 || !IsStorable(defaultPath)) return;
            string key = Key(action, index);
            if (string.IsNullOrEmpty(path) || string.Equals(path, defaultPath, StringComparison.Ordinal) || !IsStorable(path))
            {
                _entries.Remove(key);
                return;
            }
            _entries[key] = new BindingOverride(action, index, defaultPath, path);
        }

        public void Remove(string action, int index) => _entries.Remove(Key(action, index));

        public void Clear() => _entries.Clear();

        /// <summary>The player's path for a binding, if one is stored and was made against the same default.</summary>
        public bool TryGet(string action, int index, string defaultPath, out string path)
        {
            path = null;
            if (action == null || !_entries.TryGetValue(Key(action, index), out BindingOverride entry)) return false;
            if (!string.Equals(entry.DefaultPath, defaultPath, StringComparison.Ordinal)) return false;
            path = entry.Path;
            return true;
        }

        public string Serialize()
        {
            var text = new StringBuilder();
            var keys = new List<string>(_entries.Keys);
            keys.Sort(StringComparer.Ordinal); // stable output
            foreach (string key in keys)
            {
                BindingOverride entry = _entries[key];
                text.Append(entry.Action).Append('\t')
                    .Append(entry.Index.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(entry.DefaultPath).Append('\t')
                    .Append(entry.Path).Append('\n');
            }
            return text.ToString();
        }

        /// <summary>Reads <see cref="Serialize"/> output; malformed lines are skipped. Never throws.</summary>
        public static BindingOverrides Parse(string text)
        {
            var overrides = new BindingOverrides();
            if (string.IsNullOrEmpty(text)) return overrides;

            foreach (string rawLine in text.Split('\n'))
            {
                string[] parts = rawLine.TrimEnd('\r').Split('\t');
                if (parts.Length != 4) continue;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) continue;
                overrides.Set(parts[0], index, parts[2], parts[3]);
            }
            return overrides;
        }

        private static string Key(string action, int index) => action + "#" + index.ToString(CultureInfo.InvariantCulture);

        private static bool IsStorable(string value) =>
            !string.IsNullOrEmpty(value) && value.IndexOf('\t') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0;
    }

    /// <summary>Display helpers for the key-binding list.</summary>
    public static class BindingLabels
    {
        /// <summary>
        /// "ToggleInventory" → "Toggle inventory", "Weapon1" → "Weapon 1"; a composite part is appended
        /// ("Move" + "Up" → "Move forward": 2D movement parts read as directions).
        /// </summary>
        public static string Pretty(string actionName, string partName = null)
        {
            string label = SplitWords(actionName ?? string.Empty);
            if (string.IsNullOrEmpty(partName)) return label;

            string part;
            switch (partName.ToLowerInvariant())
            {
                case "up": part = "forward"; break;
                case "down": part = "back"; break;
                default: part = SplitWords(partName).ToLowerInvariant(); break;
            }
            return label.Length > 0 ? label + " " + part : part;
        }

        /// <summary>
        /// For each path, whether another entry uses the same one (case-insensitive). Empty paths never conflict.
        /// </summary>
        public static bool[] FindConflicts(IReadOnlyList<string> paths)
        {
            var conflicts = new bool[paths.Count];
            var first = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path)) continue;
                if (first.TryGetValue(path, out int other))
                {
                    conflicts[i] = true;
                    conflicts[other] = true;
                }
                else
                {
                    first.Add(path, i);
                }
            }
            return conflicts;
        }

        private static string SplitWords(string name)
        {
            var text = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0)
                {
                    char previous = name[i - 1];
                    bool wordStart = (char.IsUpper(c) && !char.IsUpper(previous)) || (char.IsDigit(c) && !char.IsDigit(previous));
                    if (wordStart && previous != ' ') text.Append(' ');
                }
                text.Append(text.Length == 0 ? char.ToUpperInvariant(c) : i > 0 && char.IsUpper(c) && !IsAcronym(name, i) ? char.ToLowerInvariant(c) : c);
            }
            return text.ToString();
        }

        // "ADSHold": keep runs of capitals as they are.
        private static bool IsAcronym(string name, int i) =>
            (i + 1 < name.Length && char.IsUpper(name[i + 1])) || (i > 0 && char.IsUpper(name[i - 1]));
    }
}
