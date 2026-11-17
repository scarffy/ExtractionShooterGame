using System;
using UnityEngine;

namespace ExtractionShooter.Core.Input
{
    /// <summary>
    /// Single place that owns the cursor lock state, so gameplay input and UI
    /// agree on whether the mouse belongs to the game or to menus.
    /// </summary>
    public static class CursorController
    {
        /// <summary>Raised with the new state whenever the lock changes.</summary>
        public static event Action<bool> LockChanged;

        public static bool IsLocked => Cursor.lockState == CursorLockMode.Locked;

        public static void SetLocked(bool locked)
        {
            if (locked == IsLocked) return;

            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            LockChanged?.Invoke(locked);
        }

        public static void Toggle() => SetLocked(!IsLocked);

        // Static state survives play mode when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => LockChanged = null;
    }
}
