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
    /// <item>Treatment: using a medical item consumes it up front, then heals over its use time and stops bleeding /
    /// sets the fracture at the end. Meanwhile no shooting, no sprinting and slower movement. Death ends it.</item>
    /// </list>
    /// The owner sees everything (HUD); others see bleeding and the fracture (for presentation later).
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

        private Health _health;
        private PlayerMotor _motor;
        private MedicalDefinition _treatment;
        private float _treatmentElapsed;
        private int _treatmentHealed;
        private float _bleedTimer;

        /// <summary>Owner side: anything shown on the HUD changed.</summary>
        public event Action Changed;

        public bool IsBleeding => _bleeding.Value;
        public bool IsFractured => _fractured.Value;

        /// <summary>Owner and server: a treatment is in progress.</summary>
        public bool IsTreating => IsServerInitialized ? _treatment != null : !string.IsNullOrEmpty(_treatmentName.Value);

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
            _bleeding.OnChange += OnBoolChanged;
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

        /// <summary>
        /// Server: starts a treatment if it would do something (heal below max, stop bleeding, set a fracture) and none is
        /// running. The caller consumes the item only when this returns true.
        /// </summary>
        public bool ServerStartTreatment(MedicalDefinition medical)
        {
            if (!IsServerInitialized || medical == null || _treatment != null || !_health.IsAlive) return false;

            bool useful = (medical.HealAmount > 0 && _health.Current < _health.Max) ||
                          (medical.StopsBleeding && _bleeding.Value) ||
                          (medical.FixesFracture && _fractured.Value);
            if (!useful) return false;

            _treatment = medical;
            _treatmentElapsed = 0f;
            _treatmentHealed = 0;
            _treatmentName.Value = medical.DisplayName;
            _treatmentPercent.Value = 0;
            return true;
        }

        private void Update()
        {
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
            int due = InjuryRules.HealDue(_treatment.HealAmount, _treatmentElapsed, _treatment.UseSeconds);
            if (due > _treatmentHealed)
            {
                _health.ServerHeal(due - _treatmentHealed);
                _treatmentHealed = due;
            }

            float progress = Mathf.Clamp01(_treatmentElapsed / _treatment.UseSeconds);
            byte percent = (byte)Mathf.RoundToInt(progress * 100f);
            if (percent != _treatmentPercent.Value) _treatmentPercent.Value = percent;

            if (progress >= 1f)
            {
                if (_treatment.StopsBleeding) SetBleeding(false);
                if (_treatment.FixesFracture) _fractured.Value = false;
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
            _treatmentName.Value = string.Empty;
            _treatmentPercent.Value = 0;
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
