using System.Collections.Generic;
using ExtractionShooter.Core.Input;
using ExtractionShooter.Core.Settings;
using ExtractionShooter.Gameplay.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtractionShooter.Gameplay.Input
{
    /// <summary>
    /// <see cref="IPlayerInput"/> backed by the Input System (keyboard/mouse and gamepad).
    /// Enabled only on the owning client by <see cref="Player.PlayerCharacter"/>.
    /// Gameplay input reads as zero while the cursor is unlocked (menus open).
    /// Look speed and invert Y combine the serialized values with the player's settings (<see cref="GameSettingsStore"/>);
    /// rebound keys come from <see cref="InputBindingStore"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InputSystemPlayerInput : MonoBehaviour, IPlayerInput
    {
        [Header("Look")]
        [Tooltip("Degrees per pixel of mouse movement.")]
        [SerializeField, Min(0f)] private float _mouseSensitivity = 0.1f;

        [Tooltip("Degrees per second at full gamepad stick deflection.")]
        [SerializeField, Min(0f)] private float _gamepadLookSpeed = 180f;

        [SerializeField] private bool _invertY;

        private InputAction _move;
        private InputAction _lookPointer;
        private InputAction _lookStick;
        private InputAction _jump;
        private InputAction _fire;
        private InputAction _aim;
        private InputAction _sprint;
        private InputAction _crouch;
        private InputAction _melee;
        private InputAction _reload;
        private InputAction _interact;
        private InputAction _toggleInventory;
        private InputAction _toggleMap;
        private InputAction _weapon1;
        private InputAction _weapon2;
        private InputAction _weapon3;
        private InputAction _cycleWeapon;
        private InputAction _throw;
        private InputAction _cycleThrowable;
        private InputAction _ping;
        private InputAction _quickHeal;
        private InputAction _cancelUse;
        private InputAction _singleItemModifier;
        private InputAction[] _actions;

        /// <summary>
        /// Every action above, in declaration order. The key-binding settings enumerate this list (keyboard and mouse
        /// bindings become rebindable rows), so a new action only needs adding here and in <see cref="Awake"/>.
        /// Escape / Start is not here: the pause menu owns it.
        /// </summary>
        public IReadOnlyList<InputAction> Actions => _actions;

        /// <summary>The key shown for an action's first binding ("X"), e.g. for HUD hints; empty if unknown.</summary>
        public string BindingLabel(string actionName)
        {
            if (_actions == null) return string.Empty;
            foreach (InputAction action in _actions)
                if (action != null && action.name == actionName) return action.GetBindingDisplayString(0);
            return string.Empty;
        }

        public Vector2 Move => IsGameplayActive ? Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f) : Vector2.zero;

        public Vector2 LookDelta
        {
            get
            {
                if (!IsGameplayActive) return Vector2.zero;

                GameSettings settings = GameSettingsStore.Current;
                Vector2 delta = _lookPointer.ReadValue<Vector2>() * (_mouseSensitivity * settings.MouseSensitivity)
                              + _lookStick.ReadValue<Vector2>() * (_gamepadLookSpeed * Time.deltaTime);
                if (_invertY != settings.InvertY) delta.y = -delta.y;
                return delta;
            }
        }

        public bool JumpHeld => IsGameplayActive && _jump.IsPressed();

        public bool FireHeld => IsGameplayActive && _fire.IsPressed();

        public bool AimHeld => IsGameplayActive && _aim.IsPressed();

        public bool SprintHeld => IsGameplayActive && _sprint.IsPressed();

        public bool CrouchPressed => IsGameplayActive && _crouch.WasPressedThisFrame();

        public bool MeleePressed => IsGameplayActive && _melee.WasPressedThisFrame();

        public bool FirePressed => IsGameplayActive && _fire.WasPressedThisFrame();

        public bool ReloadPressed => IsGameplayActive && _reload.WasPressedThisFrame();

        public bool InteractPressed => IsGameplayActive && _interact.WasPressedThisFrame();

        public bool ToggleInventoryPressed => _toggleInventory.WasPressedThisFrame();

        public bool ToggleMapPressed => _toggleMap.WasPressedThisFrame();

        public int WeaponSlotPressed
        {
            get
            {
                if (!IsGameplayActive) return -1;
                if (_weapon1.WasPressedThisFrame()) return 0;
                if (_weapon2.WasPressedThisFrame()) return 1;
                if (_weapon3.WasPressedThisFrame()) return 2;
                return -1;
            }
        }

        public bool CycleWeaponPressed => IsGameplayActive && _cycleWeapon.WasPressedThisFrame();

        public bool ThrowPressed => IsGameplayActive && _throw.WasPressedThisFrame();

        public bool CycleThrowablePressed => IsGameplayActive && _cycleThrowable.WasPressedThisFrame();

        public bool PingPressed => IsGameplayActive && _ping.WasPressedThisFrame();
        public bool QuickHealPressed => IsGameplayActive && _quickHeal.WasPressedThisFrame();
        public bool CancelUsePressed => IsGameplayActive && _cancelUse.WasPressedThisFrame();

        public bool SingleItemModifierHeld => _singleItemModifier.IsPressed();

        private static bool IsGameplayActive => CursorController.IsLocked;

        private void Awake()
        {
            _move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _move.AddBinding("<Gamepad>/leftStick");

            _lookPointer = new InputAction("LookPointer", InputActionType.Value, "<Pointer>/delta", expectedControlType: "Vector2");
            _lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlType: "Vector2");

            _jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _fire = new InputAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            _fire.AddBinding("<Gamepad>/rightTrigger");

            _aim = new InputAction("Aim", InputActionType.Button, "<Mouse>/rightButton");
            _aim.AddBinding("<Gamepad>/leftTrigger");

            _sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftStickPress");

            _crouch = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/c");
            _crouch.AddBinding("<Gamepad>/rightStickPress");

            _melee = new InputAction("Melee", InputActionType.Button, "<Keyboard>/v");
            _melee.AddBinding("<Gamepad>/rightShoulder");

            _reload = new InputAction("Reload", InputActionType.Button, "<Keyboard>/r");
            _reload.AddBinding("<Gamepad>/buttonEast");

            _interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonWest");

            _toggleInventory = new InputAction("ToggleInventory", InputActionType.Button, "<Keyboard>/tab");
            _toggleInventory.AddBinding("<Gamepad>/select");

            _toggleMap = new InputAction("ToggleMap", InputActionType.Button, "<Keyboard>/m");

            _weapon1 = new InputAction("Weapon1", InputActionType.Button, "<Keyboard>/1");
            _weapon2 = new InputAction("Weapon2", InputActionType.Button, "<Keyboard>/2");
            _weapon3 = new InputAction("Weapon3", InputActionType.Button, "<Keyboard>/3");
            _cycleWeapon = new InputAction("CycleWeapon", InputActionType.Button, "<Gamepad>/buttonNorth");

            _throw = new InputAction("Throw", InputActionType.Button, "<Keyboard>/g");
            _throw.AddBinding("<Gamepad>/leftShoulder");
            _cycleThrowable = new InputAction("CycleThrowable", InputActionType.Button, "<Keyboard>/b");
            _cycleThrowable.AddBinding("<Gamepad>/dpad/left");

            _ping = new InputAction("Ping", InputActionType.Button, "<Mouse>/middleButton");
            _ping.AddBinding("<Keyboard>/z");
            _ping.AddBinding("<Gamepad>/dpad/up");

            _quickHeal = new InputAction("QuickHeal", InputActionType.Button, "<Keyboard>/4");
            _quickHeal.AddBinding("<Gamepad>/dpad/down");
            _cancelUse = new InputAction("CancelUse", InputActionType.Button, "<Keyboard>/x");
            _cancelUse.AddBinding("<Gamepad>/dpad/right");

            _singleItemModifier = new InputAction("SingleItemModifier", InputActionType.Button, "<Keyboard>/leftAlt");
            _singleItemModifier.AddBinding("<Keyboard>/rightAlt");

            _actions = new[] { _move, _lookPointer, _lookStick, _jump, _fire, _aim, _sprint, _crouch, _melee, _reload, _interact, _toggleInventory, _toggleMap,
                _weapon1, _weapon2, _weapon3, _cycleWeapon, _throw, _cycleThrowable, _ping, _quickHeal, _cancelUse, _singleItemModifier };
        }

        private void OnEnable()
        {
            InputBindingStore.ApplyTo(_actions);
            InputBindingStore.Changed += OnBindingsChanged;
            SetActionsEnabled(true);
        }

        private void OnDisable()
        {
            InputBindingStore.Changed -= OnBindingsChanged;
            SetActionsEnabled(false);
        }

        /// <summary>The player rebound a key in the settings: take the new bindings (actions are re-enabled around it).</summary>
        private void OnBindingsChanged()
        {
            SetActionsEnabled(false);
            InputBindingStore.ApplyTo(_actions);
            SetActionsEnabled(true);
        }

        private void OnDestroy()
        {
            _move.Dispose();
            _lookPointer.Dispose();
            _lookStick.Dispose();
            _jump.Dispose();
            _fire.Dispose();
            _aim.Dispose();
            _sprint.Dispose();
            _crouch.Dispose();
            _melee.Dispose();
            _reload.Dispose();
            _interact.Dispose();
            _toggleInventory.Dispose();
            _toggleMap.Dispose();
            _weapon1.Dispose();
            _weapon2.Dispose();
            _weapon3.Dispose();
            _cycleWeapon.Dispose();
            _throw.Dispose();
            _cycleThrowable.Dispose();
            _ping.Dispose();
            _quickHeal.Dispose();
            _cancelUse.Dispose();
            _singleItemModifier.Dispose();
        }

        private void SetActionsEnabled(bool enabled)
        {
            foreach (InputAction action in _actions)
            {
                if (enabled) action.Enable();
                else action.Disable();
            }
        }
    }
}
