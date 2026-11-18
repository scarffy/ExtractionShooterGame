using System.Collections.Generic;
using ExtractionShooter.Core.Settings;
using ExtractionShooter.Gameplay.Audio;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ExtractionShooter.UI.Menu
{
    /// <summary>
    /// Pushes <see cref="GameSettings"/> into the engine: volumes (<see cref="SoundVolumes"/>), quality level, VSync,
    /// frame cap, display mode / resolution and the URP render scale. Look speed and field of view need nothing here:
    /// gameplay reads them from the settings every frame. The display and quality level are only touched when their
    /// setting changes, so a window the player resized by hand is not snapped back by an unrelated change.
    /// </summary>
    public static class SettingsApplier
    {
        private static int _appliedQuality = int.MinValue;
        private static DisplayMode? _appliedMode;
        private static Vector2Int _appliedResolution = new Vector2Int(-1, -1);

#if UNITY_EDITOR
        // In the editor the URP assets are project files: put their render scale back when play mode ends.
        private static readonly Dictionary<UniversalRenderPipelineAsset, float> OriginalRenderScales =
            new Dictionary<UniversalRenderPipelineAsset, float>();
#endif

        public static void Apply(GameSettings settings)
        {
            if (settings == null) return;
            SoundVolumes.Set(settings.MasterVolume, settings.EffectsVolume, settings.UiVolume);
            ApplyQuality(settings);
            ApplyDisplay(settings);
            ApplyRenderScale(settings.RenderScale);
        }

        private static void ApplyQuality(GameSettings settings)
        {
            string[] names = QualitySettings.names;
            if (settings.QualityLevel != _appliedQuality)
            {
                _appliedQuality = settings.QualityLevel;
                if (settings.QualityLevel >= 0 && settings.QualityLevel < names.Length && settings.QualityLevel != QualitySettings.GetQualityLevel())
                    QualitySettings.SetQualityLevel(settings.QualityLevel, true);
            }

            // After the level: a quality level carries its own VSync count.
            QualitySettings.vSyncCount = settings.VSync ? 1 : 0;
            Application.targetFrameRate = settings.FrameRateCap > 0 ? settings.FrameRateCap : -1;
        }

        private static void ApplyDisplay(GameSettings settings)
        {
            var resolution = new Vector2Int(settings.ResolutionWidth, settings.ResolutionHeight);
            if (_appliedMode == settings.DisplayMode && _appliedResolution == resolution) return;
            _appliedMode = settings.DisplayMode;
            _appliedResolution = resolution;

            var mode = (FullScreenMode)(int)settings.DisplayMode;
            if (settings.UsesNativeResolution)
            {
                // Native: the display's own size (in a window that is the whole desktop).
                Display display = Display.main;
                int width = display != null && display.systemWidth > 0 ? display.systemWidth : Screen.currentResolution.width;
                int height = display != null && display.systemHeight > 0 ? display.systemHeight : Screen.currentResolution.height;
                if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != mode)
                    Screen.SetResolution(width, height, mode);
                return;
            }

            if (Screen.width != resolution.x || Screen.height != resolution.y || Screen.fullScreenMode != mode)
                Screen.SetResolution(resolution.x, resolution.y, mode);
        }

        private static void ApplyRenderScale(float scale)
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
#if UNITY_EDITOR
            if (!OriginalRenderScales.ContainsKey(urp))
            {
                if (OriginalRenderScales.Count == 0) Application.quitting += RestoreRenderScales;
                OriginalRenderScales.Add(urp, urp.renderScale);
            }
#endif
            if (!Mathf.Approximately(urp.renderScale, scale)) urp.renderScale = scale;
        }

#if UNITY_EDITOR
        private static void RestoreRenderScales()
        {
            Application.quitting -= RestoreRenderScales;
            foreach (KeyValuePair<UniversalRenderPipelineAsset, float> original in OriginalRenderScales)
                if (original.Key != null) original.Key.renderScale = original.Value;
            OriginalRenderScales.Clear();
        }
#endif

        // Static state survives play mode when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _appliedQuality = int.MinValue;
            _appliedMode = null;
            _appliedResolution = new Vector2Int(-1, -1);
        }
    }
}
