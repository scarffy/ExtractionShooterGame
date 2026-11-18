using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ExtractionShooter.Core.Settings
{
    /// <summary>How the game window is shown. Values match UnityEngine.FullScreenMode.</summary>
    public enum DisplayMode
    {
        ExclusiveFullScreen = 0,
        FullScreenWindow = 1,
        MaximizedWindow = 2,
        Windowed = 3,
    }

    /// <summary>
    /// The player's options: controls, audio and graphics. Pure data: every setter clamps to the allowed range
    /// (NaN falls back to the default), so a value read from here is always usable.
    /// <see cref="Serialize"/> / <see cref="Parse"/> store it as "key=value" lines; unknown keys are ignored and
    /// missing or broken values keep their defaults, so saves from older or newer versions still load.
    /// Key bindings are stored separately (<see cref="BindingOverrides"/>).
    /// </summary>
    public sealed class GameSettings
    {
        public const float MinMouseSensitivity = 0.05f, MaxMouseSensitivity = 5f, DefaultMouseSensitivity = 1f;
        public const float MinAimSensitivity = 0.1f, MaxAimSensitivity = 2f, DefaultAimSensitivity = 1f;
        public const float DefaultMasterVolume = 1f, DefaultEffectsVolume = 1f, DefaultUiVolume = 0.8f;
        public const int MaxQualityLevel = 31;
        public const int MaxResolution = 16384;
        public const int MinFrameRateCap = 30, MaxFrameRateCap = 360;
        public const float MinFieldOfView = 60f, MaxFieldOfView = 100f, DefaultFieldOfView = 70f;
        public const float MinRenderScale = 0.5f, MaxRenderScale = 2f, DefaultRenderScale = 1f;
        private const int FormatVersion = 1;

        private float _mouseSensitivity = DefaultMouseSensitivity;
        private float _aimSensitivity = DefaultAimSensitivity;
        private float _masterVolume = DefaultMasterVolume;
        private float _effectsVolume = DefaultEffectsVolume;
        private float _uiVolume = DefaultUiVolume;
        private int _qualityLevel = -1;
        private DisplayMode _displayMode = DisplayMode.FullScreenWindow;
        private int _resolutionWidth;
        private int _resolutionHeight;
        private int _frameRateCap;
        private float _fieldOfView = DefaultFieldOfView;
        private float _renderScale = DefaultRenderScale;

        // ---------------- Controls ----------------

        /// <summary>Multiplier on the base mouse look speed (1 = default).</summary>
        public float MouseSensitivity
        {
            get => _mouseSensitivity;
            set => _mouseSensitivity = Clamp(value, MinMouseSensitivity, MaxMouseSensitivity, DefaultMouseSensitivity);
        }

        /// <summary>Extra multiplier on look speed while aiming down sights (1 = same feel as the hip).</summary>
        public float AimSensitivity
        {
            get => _aimSensitivity;
            set => _aimSensitivity = Clamp(value, MinAimSensitivity, MaxAimSensitivity, DefaultAimSensitivity);
        }

        public bool InvertY { get; set; }

        // ---------------- Audio (0..1) ----------------

        public float MasterVolume
        {
            get => _masterVolume;
            set => _masterVolume = Clamp(value, 0f, 1f, DefaultMasterVolume);
        }

        /// <summary>Gunshots, footsteps, grenades and other world sounds.</summary>
        public float EffectsVolume
        {
            get => _effectsVolume;
            set => _effectsVolume = Clamp(value, 0f, 1f, DefaultEffectsVolume);
        }

        /// <summary>Menu clicks and other interface sounds.</summary>
        public float UiVolume
        {
            get => _uiVolume;
            set => _uiVolume = Clamp(value, 0f, 1f, DefaultUiVolume);
        }

        // ---------------- Graphics ----------------

        /// <summary>Index into the project's quality levels, or -1 to keep the project default.</summary>
        public int QualityLevel
        {
            get => _qualityLevel;
            set => _qualityLevel = Math.Max(-1, Math.Min(MaxQualityLevel, value));
        }

        public DisplayMode DisplayMode
        {
            get => _displayMode;
            set => _displayMode = Enum.IsDefined(typeof(DisplayMode), value) ? value : DisplayMode.FullScreenWindow;
        }

        /// <summary>Window / screen width in pixels; 0 (with <see cref="ResolutionHeight"/>) uses the display's own.</summary>
        public int ResolutionWidth => _resolutionWidth;

        public int ResolutionHeight => _resolutionHeight;

        /// <summary>True when no resolution is chosen: the display's native resolution is used.</summary>
        public bool UsesNativeResolution => _resolutionWidth == 0;

        public bool VSync { get; set; } = true;

        /// <summary>Frames per second limit, or 0 for none. Ignored by most platforms while <see cref="VSync"/> is on.</summary>
        public int FrameRateCap
        {
            get => _frameRateCap;
            set => _frameRateCap = value <= 0 ? 0 : Math.Max(MinFrameRateCap, Math.Min(MaxFrameRateCap, value));
        }

        /// <summary>Vertical field of view in degrees (hip fire; aiming zooms from here).</summary>
        public float FieldOfView
        {
            get => _fieldOfView;
            set => _fieldOfView = Clamp(value, MinFieldOfView, MaxFieldOfView, DefaultFieldOfView);
        }

        /// <summary>3D render resolution relative to the screen (UI stays sharp).</summary>
        public float RenderScale
        {
            get => _renderScale;
            set => _renderScale = Clamp(value, MinRenderScale, MaxRenderScale, DefaultRenderScale);
        }

        /// <summary>Sets the resolution; either side 0 or less means native (both become 0).</summary>
        public void SetResolution(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                _resolutionWidth = 0;
                _resolutionHeight = 0;
                return;
            }

            _resolutionWidth = Math.Min(width, MaxResolution);
            _resolutionHeight = Math.Min(height, MaxResolution);
        }

        // ---------------- Defaults per page ----------------

        public void ResetControls()
        {
            var d = new GameSettings();
            MouseSensitivity = d.MouseSensitivity;
            AimSensitivity = d.AimSensitivity;
            InvertY = d.InvertY;
        }

        public void ResetAudio()
        {
            var d = new GameSettings();
            MasterVolume = d.MasterVolume;
            EffectsVolume = d.EffectsVolume;
            UiVolume = d.UiVolume;
        }

        public void ResetGraphics()
        {
            var d = new GameSettings();
            QualityLevel = d.QualityLevel;
            DisplayMode = d.DisplayMode;
            SetResolution(0, 0);
            VSync = d.VSync;
            FrameRateCap = d.FrameRateCap;
            FieldOfView = d.FieldOfView;
            RenderScale = d.RenderScale;
        }

        public GameSettings Clone() => Parse(Serialize());

        // ---------------- Persistence ----------------

        /// <summary>"key=value" lines, invariant culture.</summary>
        public string Serialize()
        {
            var text = new StringBuilder(256);
            Append(text, "version", FormatVersion.ToString(CultureInfo.InvariantCulture));
            Append(text, "mouseSensitivity", Format(MouseSensitivity));
            Append(text, "aimSensitivity", Format(AimSensitivity));
            Append(text, "invertY", InvertY ? "1" : "0");
            Append(text, "masterVolume", Format(MasterVolume));
            Append(text, "effectsVolume", Format(EffectsVolume));
            Append(text, "uiVolume", Format(UiVolume));
            Append(text, "qualityLevel", QualityLevel.ToString(CultureInfo.InvariantCulture));
            Append(text, "displayMode", ((int)DisplayMode).ToString(CultureInfo.InvariantCulture));
            Append(text, "resolution", UsesNativeResolution
                ? "native"
                : ResolutionWidth.ToString(CultureInfo.InvariantCulture) + "x" + ResolutionHeight.ToString(CultureInfo.InvariantCulture));
            Append(text, "vsync", VSync ? "1" : "0");
            Append(text, "frameRateCap", FrameRateCap.ToString(CultureInfo.InvariantCulture));
            Append(text, "fieldOfView", Format(FieldOfView));
            Append(text, "renderScale", Format(RenderScale));
            return text.ToString();
        }

        /// <summary>Reads <see cref="Serialize"/> output. Never throws: anything unreadable keeps its default.</summary>
        public static GameSettings Parse(string text)
        {
            var settings = new GameSettings();
            if (string.IsNullOrEmpty(text)) return settings;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                int separator = line.IndexOf('=');
                if (separator <= 0) continue;
                values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }

            if (TryFloat(values, "mouseSensitivity", out float f)) settings.MouseSensitivity = f;
            if (TryFloat(values, "aimSensitivity", out f)) settings.AimSensitivity = f;
            if (TryBool(values, "invertY", out bool b)) settings.InvertY = b;
            if (TryFloat(values, "masterVolume", out f)) settings.MasterVolume = f;
            if (TryFloat(values, "effectsVolume", out f)) settings.EffectsVolume = f;
            if (TryFloat(values, "uiVolume", out f)) settings.UiVolume = f;
            if (TryInt(values, "qualityLevel", out int i)) settings.QualityLevel = i;
            if (TryInt(values, "displayMode", out i)) settings.DisplayMode = (DisplayMode)i;
            if (values.TryGetValue("resolution", out string resolution)) settings.ReadResolution(resolution);
            if (TryBool(values, "vsync", out b)) settings.VSync = b;
            if (TryInt(values, "frameRateCap", out i)) settings.FrameRateCap = i;
            if (TryFloat(values, "fieldOfView", out f)) settings.FieldOfView = f;
            if (TryFloat(values, "renderScale", out f)) settings.RenderScale = f;
            return settings;
        }

        private void ReadResolution(string value)
        {
            int x = value.IndexOf('x');
            if (x <= 0) return; // "native" or garbage: keep native
            if (int.TryParse(value.Substring(0, x), NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) &&
                int.TryParse(value.Substring(x + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
                SetResolution(width, height);
        }

        private static void Append(StringBuilder text, string key, string value) => text.Append(key).Append('=').Append(value).Append('\n');

        private static string Format(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private static bool TryFloat(Dictionary<string, string> values, string key, out float value)
        {
            value = 0f;
            return values.TryGetValue(key, out string text) &&
                   float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryInt(Dictionary<string, string> values, string key, out int value)
        {
            value = 0;
            return values.TryGetValue(key, out string text) &&
                   int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryBool(Dictionary<string, string> values, string key, out bool value)
        {
            value = false;
            if (!values.TryGetValue(key, out string text)) return false;
            if (text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
            if (text == "0" || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static float Clamp(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value)) return fallback;
            return value < min ? min : value > max ? max : value;
        }
    }
}
