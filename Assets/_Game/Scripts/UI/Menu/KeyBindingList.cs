using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Settings;
using ExtractionShooter.Gameplay.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ExtractionShooter.UI.Menu
{
    /// <summary>
    /// The rebindable key list in the settings. Rows come from the actions an <see cref="InputSystemPlayerInput"/> declares
    /// (a private, disabled copy made here), one row per action or composite part with a button per keyboard / mouse
    /// binding; gamepad bindings and pointer movement are left alone. Clicking a button listens for the next key or mouse
    /// button (Escape cancels); the result is stored in <see cref="InputBindingStore"/>, which updates the live player.
    /// Keys used by more than one action are highlighted. Plain C#: owned by <see cref="SettingsPanel"/>.
    /// </summary>
    public sealed class KeyBindingList : IDisposable
    {
        private const string RowClass = "binding-row";
        private const string LabelClass = "binding-row__label";
        private const string KeyClass = "binding-key";
        private const string KeyListeningClass = "binding-key--listening";
        private const string KeyConflictClass = "binding-key--conflict";
        private const string ListeningText = "Press a key…";

        private readonly VisualElement _container;
        private readonly Action _onClick;
        private readonly GameObject _catalogObject;
        private readonly InputSystemPlayerInput _catalog;
        private readonly List<Entry> _entries = new List<Entry>();
        private InputActionRebindingExtensions.RebindingOperation _operation;
        private Entry _listening;

        /// <param name="container">Rows are added here.</param>
        /// <param name="onClick">Plays the menu click.</param>
        public KeyBindingList(VisualElement container, Transform parent, Action onClick)
        {
            _container = container;
            _onClick = onClick;

            // The actions exactly as the player has them, but disabled (an action must be disabled to be rebound).
            _catalogObject = new GameObject("Key Binding Catalog");
            _catalogObject.transform.SetParent(parent, false);
            _catalog = _catalogObject.AddComponent<InputSystemPlayerInput>();
            _catalog.enabled = false;
            Build();
        }

        /// <summary>True while waiting for the player to press the new key.</summary>
        public bool IsListening => _listening != null;

        /// <summary>The frame the last listen ended (the Escape that cancelled it must not also close the menu).</summary>
        public int LastListenEndFrame { get; private set; } = -1;

        public void Dispose()
        {
            CancelListening();
            foreach (Entry entry in _entries) entry.Button.clicked -= entry.OnClicked;
            _entries.Clear();
            if (_catalogObject != null) Object.Destroy(_catalogObject);
        }

        /// <summary>Stops listening for a key, keeping the old binding.</summary>
        public void CancelListening()
        {
            if (_operation == null) return;
            _operation.Cancel(); // raises OnCancel → Finish
            if (_operation != null) Finish(false);
        }

        /// <summary>Every key back to the game's default.</summary>
        public void ResetToDefaults()
        {
            CancelListening();
            InputBindingStore.ResetAll();
            InputBindingStore.ApplyTo(_catalog.Actions);
            Refresh();
        }

        /// <summary>Re-reads the stored bindings (the store may have changed elsewhere) and redraws the keys.</summary>
        public void Refresh()
        {
            if (_operation == null) InputBindingStore.ApplyTo(_catalog.Actions);

            var paths = new List<string>(_entries.Count);
            foreach (Entry entry in _entries) paths.Add(entry.Action.bindings[entry.BindingIndex].effectivePath);
            bool[] conflicts = BindingLabels.FindConflicts(paths);

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                bool listening = entry == _listening;
                entry.Button.text = listening ? ListeningText : DisplayString(entry);
                entry.Button.EnableInClassList(KeyListeningClass, listening);
                entry.Button.EnableInClassList(KeyConflictClass, conflicts[i] && !listening);
            }
        }

        private void Build()
        {
            _container.Clear();
            IReadOnlyList<InputAction> actions = _catalog.Actions;
            if (actions == null) return;

            foreach (InputAction action in actions)
            {
                VisualElement row = null;
                string rowLabel = null;
                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    InputBinding binding = bindings[i];
                    if (binding.isComposite || !IsKeyboardOrMouseButton(binding.path)) continue;

                    // One row per action, or per composite part (Move forward / back / left / right).
                    string label = BindingLabels.Pretty(action.name, binding.isPartOfComposite ? binding.name : null);
                    if (row == null || label != rowLabel)
                    {
                        row = new VisualElement();
                        row.AddToClassList(RowClass);
                        var text = new Label(label);
                        text.AddToClassList(LabelClass);
                        row.Add(text);
                        _container.Add(row);
                        rowLabel = label;
                    }

                    var entry = new Entry(action, i, new Button());
                    entry.Button.AddToClassList(KeyClass);
                    entry.OnClicked = () => OnKeyClicked(entry);
                    entry.Button.clicked += entry.OnClicked;
                    row.Add(entry.Button);
                    _entries.Add(entry);
                }
            }
            Refresh();
        }

        private void OnKeyClicked(Entry entry)
        {
            _onClick?.Invoke();
            bool sameKey = entry == _listening;
            CancelListening();
            if (sameKey) return; // clicking the listening key again cancels

            _listening = entry;
            _operation = entry.Action.PerformInteractiveRebinding(entry.BindingIndex)
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsHavingToMatchPath("<Mouse>")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(_ => Finish(true))
                .OnCancel(_ => Finish(false));
            _operation.Start();
            Refresh();
        }

        private void Finish(bool completed)
        {
            InputActionRebindingExtensions.RebindingOperation operation = _operation;
            _operation = null;
            _listening = null;
            operation?.Dispose();
            LastListenEndFrame = Time.frameCount;

            // Store the whole set: the store raises Changed and the live player's input re-applies it.
            if (completed) InputBindingStore.CaptureFrom(_catalog.Actions);
            Refresh();
        }

        private static string DisplayString(Entry entry)
        {
            string text = entry.Action.GetBindingDisplayString(entry.BindingIndex);
            return string.IsNullOrEmpty(text) ? "—" : text;
        }

        private static bool IsKeyboardOrMouseButton(string path) =>
            !string.IsNullOrEmpty(path) &&
            (path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase) ||
             (path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase) &&
              path.IndexOf("delta", StringComparison.OrdinalIgnoreCase) < 0 &&
              path.IndexOf("position", StringComparison.OrdinalIgnoreCase) < 0 &&
              path.IndexOf("scroll", StringComparison.OrdinalIgnoreCase) < 0));

        /// <summary>One rebindable binding and its button.</summary>
        private sealed class Entry
        {
            public readonly InputAction Action;
            public readonly int BindingIndex;
            public readonly Button Button;
            public Action OnClicked;

            public Entry(InputAction action, int bindingIndex, Button button)
            {
                Action = action;
                BindingIndex = bindingIndex;
                Button = button;
            }
        }
    }
}
