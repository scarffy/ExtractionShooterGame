using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Input;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Input;
using ExtractionShooter.Gameplay.Interaction;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Raid;
using ExtractionShooter.Gameplay.Weapons;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// Root of the networked player. Gives other systems one entry point to the
    /// player's parts and decides what runs only for the owning client.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health), typeof(PlayerMotor), typeof(PlayerLook))]
    public sealed class PlayerCharacter : NetworkBehaviour
    {
        [Tooltip("Input backend. Enabled only on the owning client.")]
        [SerializeField] private InputSystemPlayerInput _input;

        /// <summary>The player controlled by this client, or null.</summary>
        public static PlayerCharacter Local { get; private set; }

        /// <summary>Raised when the local player spawns (value) or despawns (null).</summary>
        public static event Action<PlayerCharacter> LocalPlayerChanged;

        private static readonly List<PlayerCharacter> ServerActiveList = new List<PlayerCharacter>();

        /// <summary>Server: every spawned player character (alive or dead). Bots use it to find targets.</summary>
        public static IReadOnlyList<PlayerCharacter> ServerActive => ServerActiveList;

        public Health Health { get; private set; }
        public PlayerMotor Motor { get; private set; }
        public PlayerLook Look { get; private set; }
        public PlayerInventory Inventory { get; private set; }
        public PlayerEquipment Equipment { get; private set; }
        public PlayerInteractor Interactor { get; private set; }
        public WeaponReloader Reloader { get; private set; }
        public PlayerRaid Raid { get; private set; }

        /// <summary>Owner-only input. Null-safe on other peers: returns neutral input.</summary>
        public IPlayerInput Input => IsOwner && _input != null ? _input : NeutralInput.Instance;

        private void Awake()
        {
            Health = GetComponent<Health>();
            Motor = GetComponent<PlayerMotor>();
            Look = GetComponent<PlayerLook>();
            Inventory = GetComponent<PlayerInventory>();
            Equipment = GetComponent<PlayerEquipment>();
            Interactor = GetComponent<PlayerInteractor>();
            Reloader = GetComponent<WeaponReloader>();
            Raid = GetComponent<PlayerRaid>();

            if (_input == null)
                Debug.LogError($"[{nameof(PlayerCharacter)}] Input is not assigned on {name}.", this);
            else
                _input.enabled = false;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerActiveList.Add(this);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerActiveList.Remove(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;

            if (_input != null) _input.enabled = true;
            CursorController.SetLocked(true);

            Local = this;
            LocalPlayerChanged?.Invoke(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (Local != this) return;

            if (_input != null) _input.enabled = false;
            CursorController.SetLocked(false);

            Local = null;
            LocalPlayerChanged?.Invoke(null);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            LocalPlayerChanged = null;
            ServerActiveList.Clear();
        }

        private sealed class NeutralInput : IPlayerInput
        {
            public static readonly NeutralInput Instance = new NeutralInput();
            public Vector2 Move => Vector2.zero;
            public Vector2 LookDelta => Vector2.zero;
            public bool JumpHeld => false;
            public bool FireHeld => false;
            public bool FirePressed => false;
            public bool ReloadPressed => false;
            public bool InteractPressed => false;
            public bool ToggleInventoryPressed => false;
            public int WeaponSlotPressed => -1;
            public bool CycleWeaponPressed => false;
            public bool SingleItemModifierHeld => false;
        }
    }
}
