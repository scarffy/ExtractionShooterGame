using System;
using System.Collections.Generic;
using System.Text;
using ExtractionShooter.Core.Inventory;

namespace ExtractionShooter.Core.Hideout
{
    /// <summary>
    /// A saved gun bench build: a weapon and its attachments (<see cref="WeaponMods"/> format) under a player-chosen
    /// name. Plain fields so the network layer serializes it as is.
    /// </summary>
    public struct WeaponPreset
    {
        public string Name;
        public string WeaponId;
        public string Attachments;

        public WeaponPreset(string name, string weaponId, string attachments)
        {
            Name = name;
            WeaponId = weaponId;
            Attachments = string.IsNullOrEmpty(attachments) ? null : attachments;
        }

        public override string ToString() => $"{Name}: {WeaponId} [{Attachments}]";
    }

    /// <summary>What happened when saving a preset.</summary>
    public enum PresetSaveResult
    {
        Added,
        Replaced,
        Full,
        InvalidName,
        InvalidWeapon,
    }

    /// <summary>Preset names, validation and the per-player list rules. Pure logic, unit-tested.</summary>
    public static class WeaponPresets
    {
        public const int MaxNameLength = 24;

        /// <summary>
        /// Trims and checks a preset name: 1–24 characters of letters, digits, spaces and <c>- _ . # + ( )</c>;
        /// runs of spaces collapse to one.
        /// </summary>
        public static bool TryNormalizeName(string requested, out string name)
        {
            name = null;
            if (string.IsNullOrWhiteSpace(requested)) return false;

            var builder = new StringBuilder(requested.Length);
            bool space = false;
            foreach (char c in requested.Trim())
            {
                if (c == ' ')
                {
                    if (!space) builder.Append(c);
                    space = true;
                    continue;
                }
                space = false;
                bool allowed = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '#' || c == '+' || c == '(' || c == ')';
                if (!allowed) return false;
                builder.Append(c);
            }

            if (builder.Length < 1 || builder.Length > MaxNameLength) return false;
            name = builder.ToString();
            return true;
        }

        /// <summary>
        /// The preset with a normalized name and only the attachments that are known, sit in their own slot and fit the
        /// weapon. False when the name or the weapon is unusable.
        /// </summary>
        public static bool TrySanitize(in WeaponPreset preset, IItemCatalog items, IModCatalog mods, out WeaponPreset clean)
        {
            clean = default;
            if (items == null || mods == null || !TryNormalizeName(preset.Name, out string name)) return false;
            if (string.IsNullOrEmpty(preset.WeaponId) || !items.Contains(preset.WeaponId) || mods.MagazineSize(preset.WeaponId, null) <= 0)
                return false;

            string attachments = null;
            foreach (KeyValuePair<AttachmentSlot, string> fitted in WeaponMods.All(preset.Attachments))
            {
                if (!items.Contains(fitted.Value) || !mods.TryGetAttachmentSlot(fitted.Value, out AttachmentSlot slot) || slot != fitted.Key ||
                    !mods.Fits(fitted.Value, preset.WeaponId))
                    continue;
                attachments = WeaponMods.Set(attachments, slot, fitted.Value);
            }

            clean = new WeaponPreset(name, preset.WeaponId, attachments);
            return true;
        }

        /// <summary>Index of the preset with this name (case-insensitive), or -1.</summary>
        public static int IndexOf(IReadOnlyList<WeaponPreset> presets, string name)
        {
            if (presets == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < presets.Count; i++)
                if (string.Equals(presets[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>
        /// Saves a sanitized preset: a preset with the same name is replaced in place, otherwise it is added at the end
        /// while there are fewer than <paramref name="max"/>.
        /// </summary>
        public static PresetSaveResult Save(IList<WeaponPreset> presets, in WeaponPreset preset, int max, IItemCatalog items, IModCatalog mods)
        {
            if (presets == null) throw new ArgumentNullException(nameof(presets));
            if (!TryNormalizeName(preset.Name, out _)) return PresetSaveResult.InvalidName;
            if (!TrySanitize(preset, items, mods, out WeaponPreset clean)) return PresetSaveResult.InvalidWeapon;

            int existing = -1;
            for (int i = 0; i < presets.Count; i++)
                if (string.Equals(presets[i].Name, clean.Name, StringComparison.OrdinalIgnoreCase)) existing = i;
            if (existing >= 0)
            {
                presets[existing] = clean;
                return PresetSaveResult.Replaced;
            }

            if (presets.Count >= Math.Max(0, max)) return PresetSaveResult.Full;
            presets.Add(clean);
            return PresetSaveResult.Added;
        }
    }
}
