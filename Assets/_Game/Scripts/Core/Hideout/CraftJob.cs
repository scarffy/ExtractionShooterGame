using System;

namespace ExtractionShooter.Core.Hideout
{
    /// <summary>
    /// A craft in a player's workbench queue. Times are absolute UTC ticks, so a craft keeps running while the player
    /// is offline and is ready on their next visit. Plain fields so the network layer serializes it as is.
    /// </summary>
    public struct CraftJob
    {
        public string RecipeId;
        public long StartUtcTicks;
        public long FinishUtcTicks;

        public CraftJob(string recipeId, long startUtcTicks, long finishUtcTicks)
        {
            RecipeId = recipeId;
            StartUtcTicks = startUtcTicks;
            FinishUtcTicks = finishUtcTicks;
        }

        public override string ToString() => $"{RecipeId} until {FinishUtcTicks}";
    }

    /// <summary>Workbench queue timing. Pure logic, unit-tested.</summary>
    public static class CraftQueue
    {
        /// <summary>A craft of <paramref name="seconds"/> starting at <paramref name="nowUtcTicks"/>.</summary>
        public static CraftJob Start(string recipeId, int seconds, long nowUtcTicks) =>
            new CraftJob(recipeId, nowUtcTicks, nowUtcTicks + TimeSpan.TicksPerSecond * Math.Max(0, seconds));

        public static bool IsDone(in CraftJob job, long nowUtcTicks) => nowUtcTicks >= job.FinishUtcTicks;

        /// <summary>Seconds until ready (0 when done).</summary>
        public static double RemainingSeconds(in CraftJob job, long nowUtcTicks) =>
            Math.Max(0.0, (job.FinishUtcTicks - nowUtcTicks) / (double)TimeSpan.TicksPerSecond);

        /// <summary>0–1 share of the craft time elapsed.</summary>
        public static float Progress(in CraftJob job, long nowUtcTicks)
        {
            long total = job.FinishUtcTicks - job.StartUtcTicks;
            if (total <= 0 || nowUtcTicks >= job.FinishUtcTicks) return 1f;
            if (nowUtcTicks <= job.StartUtcTicks) return 0f;
            return (float)((nowUtcTicks - job.StartUtcTicks) / (double)total);
        }

        /// <summary>"0:45", "12:05", "1:02:03" (rounded up, so it never shows 0:00 before the craft is done).</summary>
        public static string FormatDuration(double seconds)
        {
            long whole = (long)Math.Ceiling(Math.Max(0.0, seconds) - 1e-9);
            long hours = whole / 3600, minutes = whole / 60 % 60, rest = whole % 60;
            return hours > 0 ? $"{hours}:{minutes:00}:{rest:00}" : $"{minutes}:{rest:00}";
        }
    }
}
