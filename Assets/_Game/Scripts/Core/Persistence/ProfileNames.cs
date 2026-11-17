using System.Text;

namespace ExtractionShooter.Core.Persistence
{
    /// <summary>
    /// Player names double as profile file names, so they are restricted to a safe set:
    /// 3–16 characters of letters, digits, '_' and '-'. Comparison is case-insensitive.
    /// </summary>
    public static class ProfileNames
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        /// <summary>Trims and validates a requested name. Returns false (and null) when it cannot be used.</summary>
        public static bool TryNormalize(string requested, out string name)
        {
            name = null;
            if (string.IsNullOrWhiteSpace(requested)) return false;

            string trimmed = requested.Trim();
            if (trimmed.Length < MinLength || trimmed.Length > MaxLength) return false;

            var builder = new StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                bool allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!allowed) return false;
                builder.Append(c);
            }

            name = builder.ToString();
            return true;
        }

        /// <summary>File-system key for a valid name (lower case, so names differing only in case share a profile).</summary>
        public static string ToKey(string name) => name.ToLowerInvariant();
    }
}
