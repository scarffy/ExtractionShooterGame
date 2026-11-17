using System;
using ExtractionShooter.Core.Combat;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Combat
{
    /// <summary>
    /// Server-owned hit points. Only the server writes the value; every peer
    /// reads it through the SyncVar. Incoming damage passes through every
    /// <see cref="IDamageModifier"/> on this object (armor) before it is applied.
    /// Respawning is not this component's job: it reports death and accepts <see cref="ServerRestore"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Health : NetworkBehaviour, IDamageable
    {
        [SerializeField, Min(1)] private int _maxHealth = 100;

        private readonly SyncVar<int> _current = new SyncVar<int>(0);

        private IDamageModifier[] _modifiers;

        /// <summary>Raised once per peer when the value changes (previous, current).</summary>
        public event Action<int, int> Changed;

        /// <summary>Server only. Raised after damage is applied.</summary>
        public event Action<DamageInfo> ServerDamaged;

        /// <summary>Server only. Raised when health reaches zero.</summary>
        public event Action<DamageInfo> ServerDied;

        public int Current => _current.Value;
        public int Max => _maxHealth;
        public bool IsAlive => _current.Value > 0;

        private void Awake()
        {
            _current.OnChange += OnCurrentChanged;
            _modifiers = GetComponents<IDamageModifier>();
        }

        private void OnDestroy()
        {
            _current.OnChange -= OnCurrentChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _current.Value = _maxHealth;
        }

        public void ApplyDamage(in DamageInfo damage)
        {
            if (!IsServerInitialized)
            {
                Debug.LogWarning($"[{nameof(Health)}] Damage can only be applied on the server.", this);
                return;
            }
            if (!IsAlive || damage.Amount <= 0) return;

            // Armor and other modifiers on this object adjust the hit first.
            DamageInfo applied = damage;
            foreach (IDamageModifier modifier in _modifiers)
                applied = modifier.ModifyIncomingDamage(applied);
            if (applied.Amount <= 0) return;

            _current.Value = Mathf.Max(0, _current.Value - applied.Amount);
            ServerDamaged?.Invoke(applied);

            if (!IsAlive) ServerDied?.Invoke(applied);
        }

        /// <summary>Server only. Heals up to max health. Returns the amount healed.</summary>
        public int ServerHeal(int amount)
        {
            if (!IsServerInitialized || !IsAlive || amount <= 0) return 0;

            int healed = Mathf.Min(amount, _maxHealth - _current.Value);
            _current.Value += healed;
            return healed;
        }

        /// <summary>Server only. Kills outright, ignoring armor (falling out of the world).</summary>
        public void ServerKill(in DamageInfo cause)
        {
            if (!IsServerInitialized || !IsAlive) return;

            _current.Value = 0;
            ServerDied?.Invoke(cause);
        }

        /// <summary>Server only. Restores full health.</summary>
        public void ServerRestore()
        {
            if (!IsServerInitialized) return;
            _current.Value = _maxHealth;
        }

        private void OnCurrentChanged(int previous, int current, bool asServer)
        {
            // On a host the callback fires for both the server and client side; raise once.
            if (asServer && IsClientInitialized) return;
            Changed?.Invoke(previous, current);
        }
    }
}
