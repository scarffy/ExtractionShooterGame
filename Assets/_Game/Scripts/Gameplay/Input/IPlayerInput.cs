using UnityEngine;

namespace ExtractionShooter.Gameplay.Input
{
    /// <summary>
    /// Read-only view of the local player's intent. Gameplay code depends on
    /// this, never on a concrete input backend.
    /// </summary>
    public interface IPlayerInput
    {
        /// <summary>Movement on the XZ plane, magnitude 0..1.</summary>
        Vector2 Move { get; }

        /// <summary>Look delta for this frame, in degrees.</summary>
        Vector2 LookDelta { get; }

        bool JumpHeld { get; }

        bool FireHeld { get; }

        /// <summary>True on the frame the trigger was pulled (semi-auto weapons).</summary>
        bool FirePressed { get; }

        /// <summary>True on the frame Reload was pressed.</summary>
        bool ReloadPressed { get; }

        /// <summary>True on the frame Interact was pressed (gameplay only).</summary>
        bool InteractPressed { get; }

        /// <summary>True on the frame the inventory key was pressed. Works while menus are open.</summary>
        bool ToggleInventoryPressed { get; }

        /// <summary>Weapon slot key pressed this frame: 0 = primary 1, 1 = primary 2, 2 = pistol, -1 = none.</summary>
        int WeaponSlotPressed { get; }

        /// <summary>
        /// True while the "single item" modifier (Alt / Option) is held. Works while menus are open:
        /// the inventory uses it to move one item instead of the whole stack.
        /// </summary>
        bool SingleItemModifierHeld { get; }

        /// <summary>True on the frame "next weapon" was pressed (gamepad).</summary>
        bool CycleWeaponPressed { get; }
    }
}
