using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Session;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace ExtractionShooter.Gameplay.Interaction
{
    /// <summary>
    /// How a door fits its doorway and what opens it. Given by the door point before spawning and replicated with the door.
    /// </summary>
    [Serializable]
    public struct LockedDoorSettings
    {
        /// <summary>Lock id a <see cref="KeyDefinition"/> must match. Empty: a plain door anyone can open.</summary>
        public string LockId;

        /// <summary>Name in the prompt ("Vault Door").</summary>
        public string DisplayName;

        /// <summary>Doorway width (local x), height and panel thickness (local z), in metres.</summary>
        public Vector3 Size;

        /// <summary>Slides sideways into the wall instead of swinging on a hinge.</summary>
        public bool Slides;

        /// <summary>Swings toward local +z (slides toward +x) instead of -z (-x).</summary>
        public bool Reverse;
    }

    /// <summary>
    /// A server-authoritative door that blocks its doorway until opened. A locked door opens for a player carrying a key
    /// with the same lock id anywhere on them (rig, pockets, backpack, safe box); opening uses up one use of that key
    /// (see <see cref="KeyUses"/>). Once open it stays open for the rest of the raid; <see cref="ServerReset"/> locks it
    /// again when a new raid starts. The open state is a SyncVar, so every client (and late joiners) see the same door,
    /// and the panel swings or slides there locally.
    /// While closed, a carving <see cref="NavMeshObstacle"/> cuts the doorway out of the NavMesh so bots path around it;
    /// the NavMesh surface ignores objects with an obstacle, so the bake keeps the doorway walkable once it opens.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LockedDoor : NetworkBehaviour, IInteractable
    {
        private const string LockedPromptDash = " — ";

        [Tooltip("Used to name the key a lock needs in the prompt.")]
        [SerializeField] private ItemDatabase _itemDatabase;

        [Tooltip("Pivot the panel turns or slides with (the hinge edge for swinging doors).")]
        [SerializeField] private Transform _hinge;

        [Tooltip("Unit cube (mesh and box collider) under the hinge, scaled to the doorway.")]
        [SerializeField] private Transform _panel;

        [Tooltip("Small light on the panel: locked / open colour.")]
        [SerializeField] private Renderer _lockLight;

        [SerializeField] private Material _lockedMaterial;
        [SerializeField] private Material _openMaterial;

        [Tooltip("Cuts the closed doorway out of the NavMesh (carving). Disabled while open.")]
        [SerializeField] private NavMeshObstacle _obstacle;

        [Tooltip("Settings of a door placed by hand. Spawned doors get theirs from the door point.")]
        [SerializeField] private LockedDoorSettings _settings = new LockedDoorSettings
            { DisplayName = "Door", Size = new Vector3(1.4f, 2.4f, 0.1f) };

        [Tooltip("How far a swinging door opens, in degrees.")]
        [SerializeField, Range(30f, 170f)] private float _swingDegrees = 100f;

        [Tooltip("Seconds to open fully.")]
        [SerializeField, Min(0.05f)] private float _openSeconds = 0.8f;

        [Tooltip("Max distance from the player's eyes to the door panel to open it (server check).")]
        [SerializeField, Min(0.5f)] private float _useRange = 3f;

        private readonly SyncVar<LockedDoorSettings> _replicatedSettings = new SyncVar<LockedDoorSettings>();
        private readonly SyncVar<bool> _open = new SyncVar<bool>();

        private readonly List<int> _usesBuffer = new List<int>();
        private readonly List<KeyMatch> _matchBuffer = new List<KeyMatch>();

        private Collider _panelCollider;
        private float _openAmount;
        private string _keyName;
        private int _hasKeyFrame = -1;
        private PlayerCharacter _hasKeyPlayer;
        private bool _hasKey;

        private readonly struct KeyMatch
        {
            public KeyMatch(ItemContainer holder, int slot, KeyDefinition key)
            {
                Holder = holder;
                Slot = slot;
                Key = key;
            }

            public ItemContainer Holder { get; }
            public int Slot { get; }
            public KeyDefinition Key { get; }
        }

        /// <summary>Raised on every peer when the door opens or is locked again (true = open).</summary>
        public event Action<bool> OpenChanged;

        public bool IsOpen => _open.Value;

        /// <summary>True if a key is needed (a lock id is set).</summary>
        public bool IsLocked => !string.IsNullOrEmpty(_settings.LockId);

        public string LockId => _settings.LockId;

        public string DisplayName => string.IsNullOrEmpty(_settings.DisplayName) ? "Door" : _settings.DisplayName;

        /// <summary>Name of the key this door needs ("Vault Key"), or the lock id if no key item opens it.</summary>
        public string KeyName
        {
            get
            {
                if (_keyName == null) _keyName = FindKeyName();
                return _keyName;
            }
        }

        private void Awake()
        {
            if (_panel != null) _panelCollider = _panel.GetComponent<Collider>();
            if (_hinge == null || _panel == null)
                Debug.LogError($"[{nameof(LockedDoor)}] Hinge or panel is not assigned on {name}.", this);

            _open.OnChange += OnOpenChanged;
            _replicatedSettings.OnChange += OnSettingsChanged;
            ApplySettings(_settings);
            ApplyPose(0f);
            ApplyOpenState(false);
        }

        private void OnDestroy()
        {
            _open.OnChange -= OnOpenChanged;
            _replicatedSettings.OnChange -= OnSettingsChanged;
        }

        /// <summary>Server only, before spawning: fits the door to its doorway and sets its lock.</summary>
        public void ServerConfigure(LockedDoorSettings settings)
        {
            if (IsSpawned)
            {
                Debug.LogError($"[{nameof(LockedDoor)}] Configure must be called before the door is spawned.", this);
                return;
            }
            ApplySettings(settings);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _replicatedSettings.Value = _settings;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized) ApplySettings(_replicatedSettings.Value);

            // Joining while the door is already open: show it open, don't play the swing.
            _openAmount = _open.Value ? 1f : 0f;
            ApplyPose(_openAmount);
            ApplyOpenState(_open.Value);
        }

        private void Update()
        {
            float target = _open.Value ? 1f : 0f;
            if (Mathf.Approximately(_openAmount, target)) return;

            _openAmount = Mathf.MoveTowards(_openAmount, target, Time.deltaTime / _openSeconds);
            ApplyPose(_openAmount);
        }

        // ---------------- IInteractable (owning client) ----------------

        public string GetPrompt(PlayerCharacter player)
        {
            if (!IsLocked) return $"Open {DisplayName}";
            return HasKey(player) ? $"Unlock {DisplayName} ({KeyName})" : $"Locked{LockedPromptDash}needs {KeyName}";
        }

        /// <summary>True while closed, so the prompt also explains a lock the player has no key for.</summary>
        public bool CanInteract(PlayerCharacter player) =>
            player != null && player.Health.IsAlive && IsSpawned && !_open.Value;

        public void Interact(PlayerCharacter player)
        {
            if (player == null || player.Interactor == null || _open.Value) return;
            if (IsLocked && !HasKey(player)) return; // nothing to try; the prompt says which key is missing
            player.Interactor.RequestOpenDoor(this);
        }

        // ---------------- Server ----------------

        /// <summary>
        /// Server only: opens the door for a living player within reach, using one use of a matching carried key if it
        /// is locked (the most worn one first; a key on its last use is removed). False if nothing happened.
        /// </summary>
        public bool ServerTryOpen(PlayerCharacter player)
        {
            if (!IsServerInitialized || _open.Value || !ServerIsWithinReach(player)) return false;

            if (IsLocked)
            {
                if (!TryFindKey(player, true, out KeyMatch match)) return false;
                ServerUseKey(match);
            }

            _open.Value = true;
            return true;
        }

        /// <summary>Server only: closes and locks the door again (a new raid starts).</summary>
        public void ServerReset()
        {
            if (IsServerInitialized) _open.Value = false;
        }

        private bool ServerIsWithinReach(PlayerCharacter player)
        {
            if (player == null || !player.IsSpawned || !player.Health.IsAlive) return false;
            Vector3 eye = player.Look.EyePosition;
            Vector3 closest = _panelCollider != null ? _panelCollider.ClosestPoint(eye) : transform.position;
            return (closest - eye).sqrMagnitude <= _useRange * _useRange;
        }

        private static void ServerUseKey(KeyMatch match) =>
            KeyUses.UseOne(match.Holder.ServerInventory, match.Slot, match.Key.MaxUses);

        // ---------------- Keys ----------------

        /// <summary>Whether the player carries a key for this door (owning client: its own copy of its containers).</summary>
        public bool HasKey(PlayerCharacter player)
        {
            // The prompt asks every frame while the door is looked at: search once per frame.
            if (_hasKeyFrame == Time.frameCount && ReferenceEquals(_hasKeyPlayer, player)) return _hasKey;
            _hasKeyFrame = Time.frameCount;
            _hasKeyPlayer = player;
            _hasKey = TryFindKey(player, IsServerInitialized, out _);
            return _hasKey;
        }

        /// <summary>The matching key to use, from the containers this peer can see.</summary>
        private bool TryFindKey(PlayerCharacter player, bool asServer, out KeyMatch match)
        {
            match = default;
            if (!IsLocked || player == null || player.Equipment == null) return false;

            _matchBuffer.Clear();
            _usesBuffer.Clear();
            foreach (ItemContainer holder in KeyHolders(player, asServer))
            {
                ItemDatabase database = holder.ItemDatabase != null ? holder.ItemDatabase : _itemDatabase;
                if (database == null) continue;

                int count = holder.SlotCount;
                for (int slot = 0; slot < count; slot++)
                {
                    ItemStack stack = asServer && holder.ServerInventory != null ? holder.ServerInventory[slot] : holder.GetSlot(slot);
                    if (stack.IsEmpty || !database.TryGet(stack.ItemId, out ItemDefinition definition)) continue;
                    if (!(definition is KeyDefinition key) || !key.Opens(_settings.LockId)) continue;

                    _matchBuffer.Add(new KeyMatch(holder, slot, key));
                    _usesBuffer.Add(key.UsesLeft(stack));
                }
            }

            int pick = KeyUses.PickKey(_usesBuffer);
            if (pick < 0) return false;
            match = _matchBuffer[pick];
            return true;
        }

        /// <summary>Everything that counts as carried for keys: rig, pockets, backpack and the safe box.</summary>
        private static IEnumerable<ItemContainer> KeyHolders(PlayerCharacter player, bool asServer)
        {
            foreach (ItemContainer storage in player.Equipment.CarriedStorage)
                if (storage != null && storage.IsSpawned) yield return storage;

            // The safe box lives on the player's session: the server knows it per raid, the owner through its session.
            ItemContainer safeBox = asServer
                ? (player.Inventory != null ? player.Inventory.ServerSafeBox : null)
                : (player.IsOwner && PlayerSession.Local != null ? PlayerSession.Local.SafeBox : null);
            if (safeBox != null && safeBox.IsSpawned) yield return safeBox;
        }

        private string FindKeyName()
        {
            if (_itemDatabase != null)
            {
                foreach (ItemDefinition item in _itemDatabase.Items)
                    if (item is KeyDefinition key && key.Opens(_settings.LockId)) return key.DisplayName;
            }
            return string.IsNullOrEmpty(_settings.LockId) ? "a key" : $"{_settings.LockId} key";
        }

        // ---------------- Visuals ----------------

        private void OnSettingsChanged(LockedDoorSettings previous, LockedDoorSettings next, bool asServer)
        {
            if (!asServer) ApplySettings(next);
        }

        private void OnOpenChanged(bool previous, bool next, bool asServer)
        {
            // Host: apply once (the server callback); clients apply theirs.
            if (!asServer && IsServerInitialized) return;
            ApplyOpenState(next);
            OpenChanged?.Invoke(next);
        }

        /// <summary>Fits the panel, lock light and NavMesh obstacle to the doorway (local x = across, z = through the wall).</summary>
        private void ApplySettings(LockedDoorSettings settings)
        {
            _settings = settings;
            _keyName = null;

            float width = Mathf.Max(0.3f, settings.Size.x);
            float height = Mathf.Max(0.5f, settings.Size.y);
            float thickness = Mathf.Max(0.02f, settings.Size.z);

            if (_hinge != null) _hinge.localPosition = HingeRest(width);
            if (_panel != null)
            {
                _panel.localPosition = new Vector3(settings.Slides ? 0f : width * 0.5f, height * 0.5f, 0f);
                _panel.localScale = new Vector3(width, height, thickness);
            }
            if (_lockLight != null)
            {
                // By the free edge, poking out of both faces.
                float edge = settings.Slides ? width * 0.5f - 0.25f : width - 0.2f;
                _lockLight.transform.localPosition = new Vector3(edge, Mathf.Min(1.15f, height * 0.5f), 0f);
                _lockLight.transform.localScale = new Vector3(0.08f, 0.08f, thickness + 0.04f);
                _lockLight.enabled = IsLocked;
            }
            if (_obstacle != null)
            {
                _obstacle.center = new Vector3(0f, height * 0.5f, 0f);
                _obstacle.size = new Vector3(width, height, Mathf.Max(thickness, 0.4f));
            }
            ApplyPose(_openAmount);
        }

        private Vector3 HingeRest(float width) => _settings.Slides ? Vector3.zero : new Vector3(-width * 0.5f, 0f, 0f);

        private void ApplyPose(float amount)
        {
            if (_hinge == null) return;
            float eased = amount * amount * (3f - 2f * amount);
            float sign = _settings.Reverse ? -1f : 1f;
            float width = Mathf.Max(0.3f, _settings.Size.x);

            if (_settings.Slides)
            {
                _hinge.localPosition = HingeRest(width) + new Vector3(-sign * width * eased, 0f, 0f);
                _hinge.localRotation = Quaternion.identity;
            }
            else
            {
                _hinge.localPosition = HingeRest(width);
                _hinge.localRotation = Quaternion.Euler(0f, sign * _swingDegrees * eased, 0f);
            }
        }

        private void ApplyOpenState(bool open)
        {
            if (_obstacle != null) _obstacle.enabled = !open;
            if (_lockLight != null)
            {
                Material material = open ? _openMaterial : _lockedMaterial;
                if (material != null) _lockLight.sharedMaterial = material;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsLocked ? new Color(0.95f, 0.3f, 0.25f) : new Color(0.4f, 0.85f, 0.4f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, _settings.Size.y * 0.5f, 0f), _settings.Size);
        }
#endif
    }
}
