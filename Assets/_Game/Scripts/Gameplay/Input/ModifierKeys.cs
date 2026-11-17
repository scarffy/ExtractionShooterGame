using UnityEngine.InputSystem;

namespace ExtractionShooter.Gameplay.Input
{
    /// <summary>
    /// Modifier key state read straight from the keyboard device. UI Toolkit's runtime pointer events do not
    /// reliably carry modifiers on every platform, so UI that offers modifier-clicks checks this as well.
    /// </summary>
    public static class ModifierKeys
    {
        /// <summary>Alt (Option on macOS) is held: inventory clicks move a single item.</summary>
        public static bool SingleItemHeld
        {
            get
            {
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
            }
        }
    }
}
