using System;

namespace ExtractionShooter.Core.Squad
{
    /// <summary>Where a world marker goes on screen: its centre in pixels (top-left origin) and, at the edge, which way to point.</summary>
    public readonly struct ScreenMarker
    {
        public ScreenMarker(float x, float y, bool onScreen, float arrowDegrees)
        {
            X = x;
            Y = y;
            OnScreen = onScreen;
            ArrowDegrees = arrowDegrees;
        }

        /// <summary>Pixels from the left edge.</summary>
        public float X { get; }

        /// <summary>Pixels from the top edge.</summary>
        public float Y { get; }

        /// <summary>In front of the camera and inside the margins: drawn where it is.</summary>
        public bool OnScreen { get; }

        /// <summary>When pinned to the edge: direction to the target, clockwise from straight up (0 = up, 90 = right).</summary>
        public float ArrowDegrees { get; }
    }

    /// <summary>
    /// HUD arithmetic for markers that follow points in the world (teammate name tags, pings): viewport to panel
    /// pixels, pinning off-screen and behind-camera points to the screen edge, and fading/shrinking with distance.
    /// Pure logic, unit-tested.
    /// </summary>
    public static class ScreenMarkerMath
    {
        private const float RadToDeg = (float)(180.0 / Math.PI);

        /// <summary>
        /// Places a marker from a Unity viewport point (x, y in 0..1 from the bottom-left; <paramref name="depth"/> the
        /// distance in front of the camera, negative behind). Points inside the screen minus <paramref name="margin"/>
        /// pixels stay put; anything else slides along the line from the centre until it touches the margin. A point
        /// behind the camera projects mirrored through the centre, so its direction is flipped back first.
        /// </summary>
        public static ScreenMarker Place(float viewportX, float viewportY, float depth, float width, float height, float margin) =>
            Place(viewportX, viewportY, depth, width, height, margin, margin);

        /// <summary>As <see cref="Place(float,float,float,float,float,float)"/> with separate side and top/bottom margins (pixels).</summary>
        public static ScreenMarker Place(float viewportX, float viewportY, float depth, float width, float height, float marginX, float marginY)
        {
            width = Math.Max(0f, width);
            height = Math.Max(0f, height);
            marginX = Math.Max(0f, Math.Min(marginX, width * 0.5f));
            marginY = Math.Max(0f, Math.Min(marginY, height * 0.5f));

            float x = viewportX * width;
            float y = (1f - viewportY) * height;
            bool finite = IsFinite(x) && IsFinite(y) && IsFinite(depth);
            if (finite && depth > 0f && x >= marginX && x <= width - marginX && y >= marginY && y <= height - marginY)
                return new ScreenMarker(x, y, true, 0f);

            float centerX = width * 0.5f, centerY = height * 0.5f;
            float dx = finite ? x - centerX : 0f;
            float dy = finite ? y - centerY : 0f;
            if (!finite || depth <= 0f)
            {
                dx = -dx;
                dy = -dy;
            }
            // Straight behind (or no usable direction): pin to the bottom edge.
            if (Math.Abs(dx) < 1e-4f && Math.Abs(dy) < 1e-4f) dy = 1f;

            float halfWidth = centerX - marginX, halfHeight = centerY - marginY;
            float scaleX = Math.Abs(dx) > 1e-6f ? halfWidth / Math.Abs(dx) : float.MaxValue;
            float scaleY = Math.Abs(dy) > 1e-6f ? halfHeight / Math.Abs(dy) : float.MaxValue;
            float scale = Math.Min(scaleX, scaleY);
            float arrow = (float)Math.Atan2(dx, -dy) * RadToDeg;
            if (arrow < 0f) arrow += 360f;
            return new ScreenMarker(centerX + dx * scale, centerY + dy * scale, false, arrow);
        }

        /// <summary>Opacity for a marker: 1 up to <paramref name="fadeStart"/> metres, easing to <paramref name="minOpacity"/> at <paramref name="fadeEnd"/>.</summary>
        public static float DistanceOpacity(float distance, float fadeStart, float fadeEnd, float minOpacity) =>
            Lerp(1f, Clamp01(minOpacity), Ramp(distance, fadeStart, fadeEnd));

        /// <summary>Size factor for a marker: 1 up to <paramref name="near"/> metres, shrinking to <paramref name="minScale"/> at <paramref name="far"/>.</summary>
        public static float DistanceScale(float distance, float near, float far, float minScale) =>
            Lerp(1f, Math.Max(0f, minScale), Ramp(distance, near, far));

        /// <summary>Opacity in a marker's last <paramref name="fadeSeconds"/> of life: 1 until then, 0 when it expires.</summary>
        public static float ExpiryOpacity(double secondsLeft, float fadeSeconds)
        {
            if (fadeSeconds <= 0f) return secondsLeft > 0.0 ? 1f : 0f;
            return Clamp01((float)(secondsLeft / fadeSeconds));
        }

        /// <summary>Pop-in: <paramref name="startScale"/> when placed, settling to 1 over <paramref name="seconds"/>.</summary>
        public static float PopScale(double age, float seconds, float startScale)
        {
            if (seconds <= 0f || age >= seconds) return 1f;
            float t = Clamp01((float)(age / seconds));
            float eased = 1f - (1f - t) * (1f - t);
            return Lerp(startScale, 1f, eased);
        }

        private static float Ramp(float value, float from, float to)
        {
            if (float.IsNaN(value)) return 0f;
            if (to <= from) return value >= to ? 1f : 0f;
            return Clamp01((value - from) / (to - from));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
