using System;
using UnityEngine;

namespace ExtractionShooter.Core.Input
{
    /// <summary>
    /// Whether a game menu (main menu, pause menu, settings) owns the screen. Other screens check it so they do not
    /// open underneath (the inventory ignores Tab while paused); the cursor itself stays with <see cref="CursorController"/>.
    /// </summary>
    public static class MenuState
    {
        /// <summary>Raised with the new state whenever it changes.</summary>
        public static event Action<bool> Changed;

        public static bool IsOpen { get; private set; }

        public static void SetOpen(bool open)
        {
            if (open == IsOpen) return;
            IsOpen = open;
            Changed?.Invoke(open);
        }

        // Static state survives play mode when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsOpen = false;
            Changed = null;
        }
    }
}
