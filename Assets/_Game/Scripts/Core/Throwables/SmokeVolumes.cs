using System.Collections.Generic;
using UnityEngine;

namespace ExtractionShooter.Core.Throwables
{
    /// <summary>
    /// Server-side registry of smoke clouds currently in the world, so sight checks (bots) can treat smoke as opaque.
    /// Each cloud grows, holds and thins out over its life (<see cref="ThrowableMath.SmokeRadius"/>); expired clouds are
    /// dropped lazily. Times are whatever clock the caller uses consistently (the game uses <c>Time.timeAsDouble</c>).
    /// </summary>
    public static class SmokeVolumes
    {
        private struct Volume
        {
            public int Id;
            public Vector3 Center;
            public float Radius;
            public double StartTime;
            public float Duration;
            public float GrowSeconds;
            public float FadeSeconds;
        }

        private static readonly List<Volume> Active = new List<Volume>();

        /// <summary>Number of registered clouds (expired ones included until the next query).</summary>
        public static int Count => Active.Count;

        /// <summary>Registers a cloud (replacing one with the same id).</summary>
        public static void Add(int id, Vector3 center, float radius, double startTime, float duration, float growSeconds, float fadeSeconds)
        {
            Remove(id);
            Active.Add(new Volume
            {
                Id = id, Center = center, Radius = radius, StartTime = startTime, Duration = duration,
                GrowSeconds = growSeconds, FadeSeconds = fadeSeconds,
            });
        }

        /// <summary>Removes a cloud. False when there was none with that id.</summary>
        public static bool Remove(int id)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                if (Active[i].Id != id) continue;
                Active.RemoveAt(i);
                return true;
            }
            return false;
        }

        public static void Clear() => Active.Clear();

        /// <summary>True when the line from <paramref name="from"/> to <paramref name="to"/> passes through any cloud at <paramref name="now"/>.</summary>
        public static bool BlocksSight(Vector3 from, Vector3 to, double now)
        {
            bool blocked = false;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                Volume volume = Active[i];
                float elapsed = (float)(now - volume.StartTime);
                if (elapsed >= volume.Duration)
                {
                    Active.RemoveAt(i);
                    continue;
                }
                if (blocked) continue;

                float radius = ThrowableMath.SmokeRadius(elapsed, volume.Duration, volume.Radius, volume.GrowSeconds, volume.FadeSeconds);
                blocked = ThrowableMath.SegmentIntersectsSphere(from.x, from.y, from.z, to.x, to.y, to.z,
                    volume.Center.x, volume.Center.y, volume.Center.z, radius);
            }
            return blocked;
        }
    }
}
