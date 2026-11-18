using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Hideout;
using ExtractionShooter.Core.Inventory;

namespace ExtractionShooter.Core.Persistence
{
    /// <summary>
    /// Converts the hideout's saved state (workbench queue, gun bench presets) between the profile and live values,
    /// validating on load: unknown recipes and weapons are dropped, attachments and names are cleaned, and lists are
    /// capped. Old profiles without these lists load empty.
    /// </summary>
    public static class HideoutCodec
    {
        /// <summary>Hard cap on queued crafts read from a file (hand-edited data cannot grow the queue).</summary>
        public const int MaxStoredCrafts = 16;

        public static StoredCraft ToStored(in CraftJob job) => new StoredCraft
        {
            RecipeId = job.RecipeId,
            StartUtcTicks = job.StartUtcTicks,
            FinishUtcTicks = job.FinishUtcTicks,
        };

        public static StoredPreset ToStored(in WeaponPreset preset) => new StoredPreset
        {
            Name = preset.Name,
            WeaponId = preset.WeaponId,
            Attachments = preset.Attachments ?? string.Empty,
        };

        /// <summary>
        /// Saved crafts whose recipe still exists, in order. Every one is kept even if the queue has fewer slots now
        /// (the player never loses a paid craft); a start after the finish is clamped.
        /// </summary>
        public static List<CraftJob> LoadCrafts(List<StoredCraft> stored, Func<string, bool> isKnownRecipe)
        {
            var jobs = new List<CraftJob>();
            if (stored == null || isKnownRecipe == null) return jobs;

            foreach (StoredCraft craft in stored)
            {
                if (jobs.Count >= MaxStoredCrafts) break;
                if (craft == null || string.IsNullOrEmpty(craft.RecipeId) || !isKnownRecipe(craft.RecipeId)) continue;
                long start = Math.Min(craft.StartUtcTicks, craft.FinishUtcTicks);
                jobs.Add(new CraftJob(craft.RecipeId, start, craft.FinishUtcTicks));
            }
            return jobs;
        }

        /// <summary>Saved presets that are still valid (see <see cref="WeaponPresets.TrySanitize"/>), unique by name, at most <paramref name="max"/>.</summary>
        public static List<WeaponPreset> LoadPresets(List<StoredPreset> stored, IItemCatalog items, IModCatalog mods, int max)
        {
            var presets = new List<WeaponPreset>();
            if (stored == null) return presets;

            foreach (StoredPreset preset in stored)
            {
                if (presets.Count >= max) break;
                if (preset == null) continue;
                if (!WeaponPresets.TrySanitize(new WeaponPreset(preset.Name, preset.WeaponId, preset.Attachments), items, mods, out WeaponPreset clean))
                    continue;
                if (WeaponPresets.IndexOf(presets, clean.Name) < 0) presets.Add(clean);
            }
            return presets;
        }
    }
}
