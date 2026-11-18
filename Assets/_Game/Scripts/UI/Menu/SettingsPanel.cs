using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Settings;
using ExtractionShooter.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Menu
{
    /// <summary>
    /// The settings screen, shared by the main menu and the pause menu. Three pages:
    /// <list type="bullet">
    /// <item>Controls: mouse sensitivity, aim (ADS) sensitivity, invert Y and the key list (<see cref="KeyBindingList"/>).</item>
    /// <item>Audio: master, effects and interface volume.</item>
    /// <item>Graphics: quality level, display mode, resolution, VSync, frame cap, field of view and render scale.</item>
    /// </list>
    /// Every change is saved to <see cref="GameSettingsStore"/> at once and applied by <see cref="SettingsApplier"/>;
    /// "Reset to defaults" resets the page that is showing. Plain C#: owned by <see cref="GameMenu"/>.
    /// </summary>
    public sealed class SettingsPanel : IDisposable
    {
        private const string HiddenClass = "hidden";
        private const string TabActiveClass = "menu-tab--active";
        private const string RowClass = "settings-row";
        private const string SectionClass = "settings-section";
        private const string NoteClass = "menu-note";

        private static readonly int[] FrameCaps = { 0, 30, 60, 90, 120, 144, 165, 240 };

        private static readonly (DisplayMode Mode, string Label)[] DisplayModes =
        {
            (DisplayMode.ExclusiveFullScreen, "Exclusive fullscreen"),
            (DisplayMode.FullScreenWindow, "Fullscreen window"),
            (DisplayMode.Windowed, "Windowed"),
        };

        /// <summary>Which page is showing.</summary>
        private enum Page { Controls, Audio, Graphics }

        private readonly VisualElement _screen;
        private readonly VisualElement[] _pages;
        private readonly Button[] _tabs;
        private readonly Button _resetButton;
        private readonly Button _backButton;
        private readonly Action _onClick;
        private readonly List<(Button Button, Action Handler)> _handlers = new List<(Button, Action)>();
        private readonly KeyBindingList _keys;

        private Slider _mouseSensitivity;
        private Slider _aimSensitivity;
        private Toggle _invertY;
        private Slider _masterVolume;
        private Slider _effectsVolume;
        private Slider _uiVolume;
        private DropdownField _quality;
        private DropdownField _displayMode;
        private DropdownField _resolution;
        private Toggle _vsync;
        private DropdownField _frameCap;
        private Slider _fieldOfView;
        private Slider _renderScale;
        private readonly List<Vector2Int> _resolutions = new List<Vector2Int>();
        private readonly List<int> _frameCaps = new List<int>();
        private Page _page = Page.Controls;
        private bool _refreshing;

        /// <param name="root">The menu document's root.</param>
        /// <param name="catalogParent">Parent for the key list's hidden copy of the input actions.</param>
        /// <param name="onClick">Plays the menu click.</param>
        public SettingsPanel(VisualElement root, Transform catalogParent, Action onClick)
        {
            _onClick = onClick;
            _screen = root.Q<VisualElement>("settings-screen");
            _pages = new[]
            {
                root.Q<VisualElement>("settings-controls"),
                root.Q<VisualElement>("settings-audio"),
                root.Q<VisualElement>("settings-graphics"),
            };
            _tabs = new[]
            {
                root.Q<Button>("settings-tab-controls"),
                root.Q<Button>("settings-tab-audio"),
                root.Q<Button>("settings-tab-graphics"),
            };
            _resetButton = root.Q<Button>("settings-reset-button");
            _backButton = root.Q<Button>("settings-back-button");

            for (int i = 0; i < _tabs.Length; i++)
            {
                Page page = (Page)i;
                Listen(_tabs[i], () => ShowPage(page));
            }
            Listen(_resetButton, ResetPage);
            Listen(_backButton, () => BackRequested?.Invoke());

            BuildControls(_pages[(int)Page.Controls]);
            _keys = new KeyBindingList(root.Q<VisualElement>("settings-bindings") ?? new VisualElement(), catalogParent, onClick);
            BuildAudio(_pages[(int)Page.Audio]);
            BuildGraphics(_pages[(int)Page.Graphics]);
            ShowPage(Page.Controls);
            SetVisible(_screen, false);
        }

        /// <summary>Back was pressed (or Escape): the owner closes the screen.</summary>
        public event Action BackRequested;

        public bool IsOpen { get; private set; }

        /// <summary>True while a key is being rebound (Escape belongs to the rebind then).</summary>
        public bool IsCapturingInput => _keys.IsListening;

        /// <summary>The frame a rebind ended; Escape on that frame was the rebind's cancel key.</summary>
        public int LastRebindEndFrame => _keys.LastListenEndFrame;

        public void Open()
        {
            IsOpen = true;
            RefreshValues();
            _keys.Refresh();
            SetVisible(_screen, true);
            _screen?.BringToFront();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _keys.CancelListening();
            SetVisible(_screen, false);
            PlayerPrefs.Save(); // sliders save to memory as they move; write to disk once here
        }

        public void Dispose()
        {
            foreach ((Button button, Action handler) in _handlers)
                if (button != null) button.clicked -= handler;
            _handlers.Clear();
            _keys.Dispose();
        }

        // ---------------- Pages ----------------

        private void BuildControls(VisualElement page)
        {
            if (page == null) return;
            var mouse = new VisualElement();
            page.Insert(0, mouse); // above the key list declared in the UXML
            AddSection(mouse, "Mouse");
            _mouseSensitivity = AddSlider(mouse, "Mouse sensitivity", GameSettings.MinMouseSensitivity, GameSettings.MaxMouseSensitivity,
                v => Edit(s => s.MouseSensitivity = v));
            _aimSensitivity = AddSlider(mouse, "Aim (ADS) sensitivity", GameSettings.MinAimSensitivity, GameSettings.MaxAimSensitivity,
                v => Edit(s => s.AimSensitivity = v));
            _invertY = AddToggle(mouse, "Invert Y", v => Edit(s => s.InvertY = v));
        }

        private void BuildAudio(VisualElement page)
        {
            if (page == null) return;
            AddSection(page, "Volume");
            _masterVolume = AddSlider(page, "Master", 0f, 100f, v => Edit(s => s.MasterVolume = Mathf.Round(v) / 100f));
            _effectsVolume = AddSlider(page, "Effects", 0f, 100f, v => Edit(s => s.EffectsVolume = Mathf.Round(v) / 100f));
            _uiVolume = AddSlider(page, "Interface", 0f, 100f, v => Edit(s => s.UiVolume = Mathf.Round(v) / 100f));
        }

        private void BuildGraphics(VisualElement page)
        {
            if (page == null) return;
            AddSection(page, "Display");
            _quality = AddDropdown(page, "Quality", new List<string>(QualitySettings.names), i => Edit(s => s.QualityLevel = i));

            var modes = new List<string>();
            foreach (var mode in DisplayModes) modes.Add(mode.Label);
            _displayMode = AddDropdown(page, "Display mode", modes, i => Edit(s => s.DisplayMode = DisplayModes[i].Mode));

            _resolution = AddDropdown(page, "Resolution", new List<string>(), i =>
                Edit(s => s.SetResolution(_resolutions[i].x, _resolutions[i].y)));
            _vsync = AddToggle(page, "VSync", v => Edit(s => s.VSync = v));
            _frameCap = AddDropdown(page, "Frame rate limit", new List<string>(), i => Edit(s => s.FrameRateCap = _frameCaps[i]));
            AddNote(page, "The frame rate limit applies with VSync off.");

            AddSection(page, "View");
            _fieldOfView = AddSlider(page, "Field of view (vertical °)", GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView,
                v => Edit(s => s.FieldOfView = Mathf.Round(v)));
            _renderScale = AddSlider(page, "Render scale (%)", GameSettings.MinRenderScale * 100f, GameSettings.MaxRenderScale * 100f,
                v => Edit(s => s.RenderScale = Mathf.Round(v / 5f) * 5f / 100f));
        }

        /// <summary>
        /// Puts the stored values into every control without raising their change callbacks (no index setters: their
        /// change events are queued when this runs inside a click and would arrive after <see cref="_refreshing"/>).
        /// </summary>
        private void RefreshValues()
        {
            GameSettings s = GameSettingsStore.Current;
            _refreshing = true;
            try
            {
                _mouseSensitivity?.SetValueWithoutNotify(s.MouseSensitivity);
                _aimSensitivity?.SetValueWithoutNotify(s.AimSensitivity);
                _invertY?.SetValueWithoutNotify(s.InvertY);
                _masterVolume?.SetValueWithoutNotify(s.MasterVolume * 100f);
                _effectsVolume?.SetValueWithoutNotify(s.EffectsVolume * 100f);
                _uiVolume?.SetValueWithoutNotify(s.UiVolume * 100f);
                _vsync?.SetValueWithoutNotify(s.VSync);
                _fieldOfView?.SetValueWithoutNotify(s.FieldOfView);
                _renderScale?.SetValueWithoutNotify(s.RenderScale * 100f);

                if (_quality != null)
                {
                    int level = s.QualityLevel >= 0 ? s.QualityLevel : QualitySettings.GetQualityLevel();
                    if (level >= 0 && level < _quality.choices.Count) _quality.SetValueWithoutNotify(_quality.choices[level]);
                }

                if (_displayMode != null)
                {
                    int mode = Array.FindIndex(DisplayModes, m => m.Mode == s.DisplayMode);
                    _displayMode.SetValueWithoutNotify(DisplayModes[mode >= 0 ? mode : 1].Label);
                }

                RefreshResolutions(s);
                RefreshFrameCaps(s);
            }
            finally
            {
                _refreshing = false;
            }
        }

        /// <summary>"Native" plus every distinct size the display offers, largest first, plus the stored one if it is not listed.</summary>
        private void RefreshResolutions(GameSettings s)
        {
            if (_resolution == null) return;
            _resolutions.Clear();
            _resolutions.Add(Vector2Int.zero);
            foreach (Resolution r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!_resolutions.Contains(size)) _resolutions.Add(size);
            }
            var stored = new Vector2Int(s.ResolutionWidth, s.ResolutionHeight);
            if (!_resolutions.Contains(stored)) _resolutions.Add(stored);
            _resolutions.Sort(1, _resolutions.Count - 1, Comparer<Vector2Int>.Create((a, b) =>
                a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y)));

            var labels = new List<string>(_resolutions.Count);
            foreach (Vector2Int size in _resolutions) labels.Add(size == Vector2Int.zero ? "Native" : $"{size.x} × {size.y}");
            _resolution.choices = labels;
            _resolution.SetValueWithoutNotify(labels[_resolutions.IndexOf(stored)]);
        }

        private void RefreshFrameCaps(GameSettings s)
        {
            if (_frameCap == null) return;
            _frameCaps.Clear();
            _frameCaps.AddRange(FrameCaps);
            if (!_frameCaps.Contains(s.FrameRateCap)) _frameCaps.Add(s.FrameRateCap);
            _frameCaps.Sort();

            var labels = new List<string>(_frameCaps.Count);
            foreach (int cap in _frameCaps) labels.Add(cap == 0 ? "Unlimited" : $"{cap} fps");
            _frameCap.choices = labels;
            _frameCap.SetValueWithoutNotify(labels[_frameCaps.IndexOf(s.FrameRateCap)]);
        }

        private void ShowPage(Page page)
        {
            _keys?.CancelListening();
            _page = page;
            for (int i = 0; i < _pages.Length; i++)
            {
                SetVisible(_pages[i], i == (int)page);
                if (_tabs[i] != null) _tabs[i].EnableInClassList(TabActiveClass, i == (int)page);
            }
        }

        private void ResetPage()
        {
            switch (_page)
            {
                case Page.Controls:
                    Edit(s => s.ResetControls());
                    _keys.ResetToDefaults();
                    break;
                case Page.Audio:
                    Edit(s => s.ResetAudio());
                    break;
                case Page.Graphics:
                    Edit(s => s.ResetGraphics());
                    break;
            }
            RefreshValues();
        }

        private void Edit(Action<GameSettings> change)
        {
            if (_refreshing) return;
            GameSettings settings = GameSettingsStore.Current.Clone();
            change(settings);
            GameSettingsStore.Save(settings);
        }

        // ---------------- Control helpers ----------------

        private static void AddSection(VisualElement page, string title)
        {
            var label = new Label(title);
            label.AddToClassList(SectionClass);
            page.Add(label);
        }

        private static void AddNote(VisualElement page, string text)
        {
            var label = new Label(text);
            label.AddToClassList(NoteClass);
            page.Add(label);
        }

        private static Slider AddSlider(VisualElement page, string label, float min, float max, Action<float> changed)
        {
            var slider = new Slider(label, min, max) { showInputField = true };
            slider.AddToClassList(RowClass);
            slider.RegisterValueChangedCallback(evt => changed(evt.newValue));
            page.Add(slider);
            return slider;
        }

        private static Toggle AddToggle(VisualElement page, string label, Action<bool> changed)
        {
            var toggle = new Toggle(label);
            toggle.AddToClassList(RowClass);
            toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
            page.Add(toggle);
            return toggle;
        }

        private static DropdownField AddDropdown(VisualElement page, string label, List<string> choices, Action<int> changed)
        {
            var dropdown = new DropdownField(label, choices, choices.Count > 0 ? 0 : -1);
            dropdown.AddToClassList(RowClass);
            dropdown.RegisterValueChangedCallback(_ =>
            {
                if (dropdown.index >= 0) changed(dropdown.index);
            });
            page.Add(dropdown);
            return dropdown;
        }

        private void Listen(Button button, Action action)
        {
            if (button == null) return;
            Action handler = () =>
            {
                _onClick?.Invoke();
                action();
            };
            button.clicked += handler;
            _handlers.Add((button, handler));
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null) element.EnableInClassList(HiddenClass, !visible);
        }
    }
}
