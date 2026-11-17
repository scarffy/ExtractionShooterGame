using ExtractionShooter.Core.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtractionShooter.Gameplay.Input
{
    /// <summary>
    /// <see cref="IPlayerInput"/> backed by the Input System (keyboard/mouse and gamepad).
    /// Enabled only on the owning client by <see cref="Player.PlayerCharacter"/>.
    /// Gameplay input reads as zero while the cursor is unlocked (menus open).
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
        private InputAction _reload;
        private InputAction _interact;
        private InputAction _toggleInventory;
        private InputAction _weapon1;
        private InputAction _weapon2;
        private InputAction _weapon3;
        private InputAction _cycleWeapon;
        private InputAction _singleItemModifier;
        private InputAction _togglePause;

        public Vector2 Move => IsGameplayActive ? Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f) : Vector2.zero;

        public Vector2 LookDelta
        {
            get
            {
                if (!IsGameplayActive) return Vector2.zero;

                Vector2 delta = _lookPointer.ReadValue<Vector2>() * _mouseSensitivity
                              + _lookStick.ReadValue<Vector2>() * (_gamepadLookSpeed * Time.deltaTime);
                if (_invertY) delta.y = -delta.y;
                return delta;
            }
        }

        public bool JumpHeld => IsGameplayActive && _jump.IsPressed();

        public bool FireHeld => IsGameplayActive && _fire.IsPressed();

        public bool FirePressed => IsGameplayActive && _fire.WasPressedThisFrame();

        public bool ReloadPressed => IsGameplayActive && _reload.WasPressedThisFrame();

        public bool InteractPressed => IsGameplayActive && _interact.WasPressedThisFrame();

        public bool ToggleInventoryPressed => _toggleInventory.WasPressedThisFrame();

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

            _reload = new InputAction("Reload", InputActionType.Button, "<Keyboard>/r");
            _reload.AddBinding("<Gamepad>/buttonEast");

            _interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonWest");

            _toggleInventory = new InputAction("ToggleInventory", InputActionType.Button, "<Keyboard>/tab");
            _toggleInventory.AddBinding("<Gamepad>/select");

            _weapon1 = new InputAction("Weapon1", InputActionType.Button, "<Keyboard>/1");
            _weapon2 = new InputAction("Weapon2", InputActionType.Button, "<Keyboard>/2");
            _weapon3 = new InputAction("Weapon3", InputActionType.Button, "<Keyboard>/3");
            _cycleWeapon = new InputAction("CycleWeapon", InputActionType.Button, "<Gamepad>/buttonNorth");

            _singleItemModifier = new InputAction("SingleItemModifier", InputActionType.Button, "<Keyboard>/leftAlt");
            _singleItemModifier.AddBinding("<Keyboard>/rightAlt");

            _togglePause = new InputAction("TogglePause", InputActionType.Button, "<Keyboard>/escape");
            _togglePause.AddBinding("<Gamepad>/start");
        }

        private void OnEnable()
        {
            _togglePause.performed += OnTogglePause;
            SetActionsEnabled(true);
        }

        private void OnDisable()
        {
            _togglePause.performed -= OnTogglePause;
            SetActionsEnabled(false);
        }

        private void OnDestroy()
        {
            _move.Dispose();
            _lookPointer.Dispose();
            _lookStick.Dispose();
            _jump.Dispose();
            _fire.Dispose();
            _reload.Dispose();
            _interact.Dispose();
            _toggleInventory.Dispose();
            _weapon1.Dispose();
            _weapon2.Dispose();
            _weapon3.Dispose();
            _cycleWeapon.Dispose();
            _singleItemModifier.Dispose();
            _togglePause.Dispose();
        }

        private void SetActionsEnabled(bool enabled)
        {
            InputAction[] actions = { _move, _lookPointer, _lookStick, _jump, _fire, _reload, _interact, _toggleInventory,
                _weapon1, _weapon2, _weapon3, _cycleWeapon, _singleItemModifier, _togglePause };
            foreach (InputAction action in actions)
            {
                if (enabled) action.Enable();
                else action.Disable();
            }
        }

        private static void OnTogglePause(InputAction.CallbackContext _) => CursorController.Toggle();
    }
}
