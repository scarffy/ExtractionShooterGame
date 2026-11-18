using System;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Combat
{
    /// <summary>
    /// Server-authoritative injuries and treatment for a player.
    /// <list type="bullet">
    /// <item>Bleeding: a body or leg hit that gets through armor may start it (<see cref="InjuryRules"/>); it drains
    /// health every few seconds until a bandage or medkit stops it.</item>
    /// <item>Fractured leg: a heavy leg hit may break it: no sprinting and slower movement until a splint sets it.</item>
    /// <item>Treatment: using a medical item takes its use time (no shooting or sprinting, slower movement); when it
    /// completes the item is used up and it heals, stops bleeding and/or sets the fracture, all at once
    /// (<see cref="TreatmentRules"/>). The owner cancels with the cancel key, by firing or by switching weapons: the item
    /// is kept and nothing happens. Losing the item meanwhile (dropped, moved out) also cancels it. Death ends it.</item>
    /// <item>Quick heal (owner): uses the best medical item carried for the current wounds.</item>
    /// </list>
    /// The owner sees everything (HUD); everyone sees bleeding, the fracture and what is being used
    /// (<see cref="UseVisual"/>, for the body and hand animation).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health), typeof(PlayerMotor))]
    public sealed class PlayerInjuries : NetworkBehaviour, IMovementLimiter
    {
        [Header("Bleeding")]
        [SerializeField, Min(1)] private int _bleedMinDamage = 12;
        [SerializeField, Range(0f, 1f)] private float _bleedChance = 0.35f;
        [SerializeField, Min(1)] private int _bleedDamage = 1;
        [SerializeField, Min(0.1f)] private float _bleedTickSeconds = 1.5f;

        [Header("Fracture")]
        [SerializeField, Min(1)] private int _fractureMinDamage = 20;
        [SerializeField, Range(0f, 1f)] private float _fractureChance = 0.45f;

        [Header("Treatment")]
        [Tooltip("Speed multiplier while treating.")]
        [SerializeField, Range(0.1f, 1f)] private float _treatmentSpeedMultiplier = 0.6f;

        private readonly SyncVar<bool> _bleeding = new SyncVar<bool>();
        private readonly SyncVar<bool> _fractured = new SyncVar<bool>();
        private readonly SyncVar<string> _treatmentName = new SyncVar<string>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncVar<byte> _treatmentPercent = new SyncVar<byte>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncVar<UseItemVisual> _useVisual = new SyncVar<UseItemVisual>();

        private Health _health;
        private PlayerMotor _motor;
        private PlayerCharacter _character;
        private MedicalDefinition _treatment;
        private Func<bool> _consume;
        private float _treatmentElapsed;
        private float _useStartedAt;
        private float _bleedTimer;

        /// <summary>Owner side: anything shown on the HUD changed.</summary>
        public event Action Changed;

        public bool IsBleeding => _bleeding.Value;
        public bool IsFractured => _fractured.Value;

        /// <summary>A treatment is in progress (server, owner and other players).</summary>
        public bool IsTreating => IsServerInitialized ? _treatment != null : _useVisual.Value != UseItemVisual.None;

        /// <summary>What is being used (None when not treating). Everyone sees it.</summary>
        public UseItemVisual UseVisual => _useVisual.Value;

        /// <summary>Local time the current use started (for animation on any peer); valid while <see cref="IsTreating"/>.</summary>
        public float UseStartedAt => _useStartedAt;

        /// <summary>Owner: the item being used, or empty.</summary>
        public string TreatmentName => _treatmentName.Value;

        /// <summary>Owner: treatment progress 0..1.</summary>
        public float TreatmentProgress => _treatmentPercent.Value / 100f;

        bool IMovementLimiter.AllowsSprint => !IsFractured && !IsTreating;

        float IMovementLimiter.SpeedMultiplier
        {
            get
            {
                float multiplier = IsTreating ? _treatmentSpeedMultiplier : 1f;
                if (IsFractured && _motor.Settings != null) multiplier *= _motor.Settings.FracturedSpeedMultiplier;
                return multiplier;
            }
        }

        private void Awake()
        {
            _health = GetComponent<Health>();
            _motor = GetComponent<PlayerMotor>();
            _character = GetComponent<PlayerCharacter>();
            _bleeding.OnChange += OnBoolChanged;
            _useVisual.OnChange += OnUseVisualChanged;
            _fractured.OnChange += OnBoolChanged;
            _treatmentName.OnChange += OnNameChanged;
            _treatmentPercent.OnChange += OnPercentChanged;
        }

        private void OnDestroy()
        {
            _bleeding.OnChange -= OnBoolChanged;
            _fractured.OnChange -= OnBoolChanged;
            _treatmentName.OnChange -= OnNameChanged;
            _treatmentPercent.OnChange -= OnPercentChanged;
            _useVisual.OnChange -= OnUseVisualChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _health.ServerDamaged += OnServerDamaged;
            _health.ServerDied += OnServerDied;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _health.ServerDamaged -= OnServerDamaged;
            _health.ServerDied -= OnServerDied;
        }

        /// <summary>Server: the player's wounds now.</summary>
        public WoundState ServerWounds => new WoundState(_health.Max - _health.Current, _bleeding.Value, _fractured.Value);

        /// <summary>
        /// Server: starts a treatment if it would do something (heal below max, stop bleeding, set a fracture) and none is
        /// running. <paramref name="consume"/> is called when it completes: it takes the item and returns false if the item
        /// is gone, in which case the treatment does nothing.
        /// </summary>
        public bool ServerStartTreatment(MedicalDefinition medical, Func<bool> consume)
        {
            if (!IsServerInitialized || medical == null || _treatment != null || !_health.IsAlive) return false;
            if (!TreatmentRules.IsUseful(medical.ToOption(), ServerWounds)) return false;

            _treatment = medical;
            _consume = consume;
            _treatmentElapsed = 0f;
            _treatmentName.Value = medical.DisplayName;
            _treatmentPercent.Value = 0;
            _useVisual.Value = medical.UseVisual == UseItemVisual.None ? UseItemVisual.Bandage : medical.UseVisual;
            _useStartedAt = Time.time;
            return true;
        }

        /// <summary>Owner: stops the current treatment; the item is kept and nothing happens.</summary>
        public void RequestCancelTreatment()
        {
            if (IsOwner && IsTreating) ServerCancelTreatment();
        }

        [ServerRpc]
        private void ServerCancelTreatment() => EndTreatment();

        /// <summary>Owner: the cancel key, firing or switching weapons stops a treatment; the quick-heal key starts one.</summary>
        private void HandleOwnerInput()
        {
            if (_character == null || !_health.IsAlive) return;
            var input = _character.Input;
            if (input == null) return;

            if (IsTreating)
            {
                if (input.CancelUsePressed || input.FirePressed || input.WeaponSlotPressed >= 0 || input.CycleWeaponPressed)
                    RequestCancelTreatment();
            }
            else if (input.QuickHealPressed && _character.Inventory != null)
            {
                _character.Inventory.RequestQuickHeal();
            }
        }

        private void Update()
        {
            if (IsOwner) HandleOwnerInput();
            if (!IsServerInitialized || !_health.IsAlive) return;
            float deltaTime = Time.deltaTime;

            if (_bleeding.Value)
            {
                _bleedTimer += deltaTime;
                if (_bleedTimer >= _bleedTickSeconds)
                {
                    _bleedTimer -= _bleedTickSeconds;
                    _health.ServerApplyDirect(new DamageInfo(_bleedDamage, DamageInfo.NoInstigator, transform.position, Vector3.down,
                        HitZone.Internal));
                }
            }

            if (_treatment == null) return;

            _treatmentElapsed += deltaTime;
            float progress = Mathf.Clamp01(_treatmentElapsed / _treatment.UseSeconds);
            byte percent = (byte)Mathf.RoundToInt(progress * 100f);
            if (percent != _treatmentPercent.Value) _treatmentPercent.Value = percent;

            if (progress >= 1f)
            {
                MedicalDefinition medical = _treatment;
                // The item is used up only now; if it was dropped or moved away meanwhile, nothing happens.
                if (_consume == null || _consume())
                {
                    if (medical.HealAmount > 0) _health.ServerHeal(medical.HealAmount);
                    if (medical.StopsBleeding) SetBleeding(false);
                    if (medical.FixesFracture) _fractured.Value = false;
                }
                EndTreatment();
            }
        }

        private void OnServerDamaged(DamageInfo damage)
        {
            if (!_health.IsAlive) return;
            InjuryOutcome outcome = InjuryRules.FromHit(damage.Zone, damage.Amount, UnityEngine.Random.value, UnityEngine.Random.value,
                _bleedMinDamage, _bleedChance, _fractureMinDamage, _fractureChance);
            if (outcome.Bleeding) SetBleeding(true);
            if (outcome.Fracture) _fractured.Value = true;
        }

        private void OnServerDied(DamageInfo _)
        {
            EndTreatment();
            SetBleeding(false);
            _fractured.Value = false;
        }

        private void SetBleeding(bool bleeding)
        {
            if (bleeding && !_bleeding.Value) _bleedTimer = 0f;
            _bleeding.Value = bleeding;
        }

        private void EndTreatment()
        {
            _treatment = null;
            _consume = null;
            _useVisual.Value = UseItemVisual.None;
            _treatmentName.Value = string.Empty;
            _treatmentPercent.Value = 0;
        }

        private void OnUseVisualChanged(UseItemVisual previous, UseItemVisual current, bool asServer)
        {
            if (current != UseItemVisual.None && previous == UseItemVisual.None && !asServer) _useStartedAt = Time.time;
            if (!asServer) Changed?.Invoke();
        }

        private void OnBoolChanged(bool previous, bool current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }

        private void OnNameChanged(string previous, string current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }

        private void OnPercentChanged(byte previous, byte current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }
    }
}
