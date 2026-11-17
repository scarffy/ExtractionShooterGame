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

        /// <summary>True while sprint is held (Left Shift, left stick press).</summary>
        bool SprintHeld { get; }

        /// <summary>True on the frame crouch was pressed (C, right stick press). Crouch toggles.</summary>
        bool CrouchPressed { get; }

        /// <summary>True on the frame melee was pressed (V, right bumper).</summary>
        bool MeleePressed { get; }

        bool FireHeld { get; }

        /// <summary>True while aim down sights is held (right mouse, left trigger).</summary>
        bool AimHeld { get; }

        /// <summary>True on the frame the trigger was pulled (semi-auto weapons).</summary>
        bool FirePressed { get; }

        /// <summary>True on the frame Reload was pressed.</summary>
        bool ReloadPressed { get; }

        /// <summary>True on the frame Interact was pressed (gameplay only).</summary>
        bool InteractPressed { get; }

        /// <summary>True on the frame the inventory key was pressed. Works while menus are open.</summary>
        bool ToggleInventoryPressed { get; }

        /// <summary>True on the frame the map key (M) was pressed. Works while menus are open, so M also closes the map.</summary>
        bool ToggleMapPressed { get; }

        /// <summary>Weapon slot key pressed this frame: 0 = primary 1, 1 = primary 2, 2 = pistol, -1 = none.</summary>
        int WeaponSlotPressed { get; }

        /// <summary>
        /// True while the "single item" modifier (Alt / Option) is held. Works while menus are open:
        /// the inventory uses it to move one item instead of the whole stack.
        /// </summary>
        bool SingleItemModifierHeld { get; }

        /// <summary>True on the frame "next weapon" was pressed (gamepad).</summary>
        bool CycleWeaponPressed { get; }

        /// <summary>True on the frame "throw grenade" was pressed (G, left bumper). Throws the selected type.</summary>
        bool ThrowPressed { get; }

        /// <summary>True on the frame "next grenade type" was pressed (B, d-pad left).</summary>
        bool CycleThrowablePressed { get; }
    }
}
