using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtractionShooter.Gameplay.Input
{
    /// <summary>
    /// The player's key rebinds (<see cref="BindingOverrides"/>), kept in PlayerPrefs and applied to every
    /// <see cref="InputSystemPlayerInput"/>. The settings screen rebinds a disabled copy of the actions, then
    /// <see cref="CaptureFrom"/> stores the result and live inputs re-apply it on <see cref="Changed"/>.
    /// Works for any action the input component declares: nothing here names an action.
    /// </summary>
    public static class InputBindingStore
    {
        private const string PrefsKey = "ExtractionShooter.KeyBindings";

        private static BindingOverrides _overrides;

        /// <summary>Raised after the stored bindings changed.</summary>
        public static event Action Changed;

        private static BindingOverrides Overrides => _overrides ??= BindingOverrides.Parse(PlayerPrefs.GetString(PrefsKey, string.Empty));

        /// <summary>Puts the stored rebinds on <paramref name="actions"/> and clears any other override they carry.</summary>
        public static void ApplyTo(IReadOnlyList<InputAction> actions)
        {
            if (actions == null) return;
            BindingOverrides overrides = Overrides;
            foreach (InputAction action in actions)
            {
                if (action == null) continue;
                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    InputBinding binding = bindings[i];
                    if (binding.isComposite) continue;
                    if (overrides.TryGet(action.name, i, binding.path, out string path))
                    {
                        if (binding.overridePath != path) action.ApplyBindingOverride(i, path);
                    }
                    else if (binding.overridePath != null)
                    {
                        action.RemoveBindingOverride(i);
                    }
                }
            }
        }

        /// <summary>Stores the overrides currently on <paramref name="actions"/> (replacing what was stored) and raises <see cref="Changed"/>.</summary>
        public static void CaptureFrom(IReadOnlyList<InputAction> actions)
        {
            if (actions == null) return;
            BindingOverrides overrides = Overrides;
            overrides.Clear();
            foreach (InputAction action in actions)
            {
                if (action == null) continue;
                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    InputBinding binding = bindings[i];
                    if (!binding.isComposite && !string.IsNullOrEmpty(binding.overridePath))
                        overrides.Set(action.name, i, binding.path, binding.overridePath);
                }
            }
            Save();
        }

        /// <summary>Forgets every rebind (defaults everywhere) and raises <see cref="Changed"/>.</summary>
        public static void ResetAll()
        {
            Overrides.Clear();
            Save();
        }

        private static void Save()
        {
            PlayerPrefs.SetString(PrefsKey, Overrides.Serialize());
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // Static state survives play mode when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _overrides = null;
            Changed = null;
        }
    }
}
