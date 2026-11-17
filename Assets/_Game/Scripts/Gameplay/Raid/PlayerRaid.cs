using System;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Gameplay.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Raid
{
    /// <summary>
    /// The raid clock and extraction for one player character, run on the server.
    /// Staying inside an <see cref="ExtractionZone"/> for <see cref="_extractSeconds"/> extracts
    /// (leaving the zone resets it). When the raid time runs out the character is killed (missing in action)
    /// and loses its gear like any other death. Remaining time and progress replicate for the HUD.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter))]
    public sealed class PlayerRaid : NetworkBehaviour
    {
        [SerializeField, Min(10f)] private float _raidDurationSeconds = 1200f;
        [SerializeField, Min(0.5f)] private float _extractSeconds = 8f;

        private readonly SyncVar<int> _secondsLeft = new SyncVar<int>();
        private readonly SyncVar<byte> _extractPercent = new SyncVar<byte>();
        private readonly SyncVar<string> _zoneName = new SyncVar<string>();

        private PlayerCharacter _character;
        private double _raidEndsAt;
        private double _zoneEnteredAt;
        private ExtractionZone _zone;
        private double _startedAt;
        private bool _finished;

        /// <summary>Server: the character finished extracting (raised once).</summary>
        public event Action<PlayerRaid> ServerExtracted;

        /// <summary>Client side: time, zone or progress changed.</summary>
        public event Action Changed;

        public int SecondsLeft => _secondsLeft.Value;

        /// <summary>Zone being extracted at, or empty.</summary>
        public string ZoneName => _zoneName.Value;

        public float ExtractProgress => _extractPercent.Value / 100f;

        public bool IsExtracting => !string.IsNullOrEmpty(_zoneName.Value);

        /// <summary>Server: seconds since this character entered the raid.</summary>
        public double ServerSecondsInRaid => Time.timeAsDouble - _startedAt;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _secondsLeft.OnChange += OnIntChanged;
            _extractPercent.OnChange += OnByteChanged;
            _zoneName.OnChange += OnStringChanged;
        }

        private void OnDestroy()
        {
            _secondsLeft.OnChange -= OnIntChanged;
            _extractPercent.OnChange -= OnByteChanged;
            _zoneName.OnChange -= OnStringChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _startedAt = Time.timeAsDouble;
            _raidEndsAt = _startedAt + _raidDurationSeconds;
            _secondsLeft.Value = Mathf.CeilToInt(_raidDurationSeconds);
        }

        private void Update()
        {
            if (!IsServerInitialized || _finished) return;

            if (!_character.Health.IsAlive)
            {
                SetZone(null);
                return;
            }

            double now = Time.timeAsDouble;
            double remaining = _raidEndsAt - now;
            _secondsLeft.Value = Mathf.Max(0, (int)Math.Ceiling(remaining));

            if (remaining <= 0)
            {
                _finished = true;
                SetZone(null);
                _character.Health.ServerKill(new DamageInfo(_character.Health.Max, DamageInfo.NoInstigator,
                    transform.position, Vector3.down));
                return;
            }

            ExtractionZone zone = ExtractionZone.Find(transform.position);
            if (zone != _zone)
            {
                _zone = zone;
                _zoneEnteredAt = now;
                SetZone(zone);
            }
            if (zone == null) return;

            float progress = Mathf.Clamp01((float)((now - _zoneEnteredAt) / _extractSeconds));
            _extractPercent.Value = (byte)Mathf.RoundToInt(progress * 100f);

            if (progress >= 1f)
            {
                _finished = true;
                ServerExtracted?.Invoke(this);
            }
        }

        private void SetZone(ExtractionZone zone)
        {
            _zone = zone;
            _zoneName.Value = zone != null ? zone.DisplayName : string.Empty;
            _extractPercent.Value = 0;
        }

        private void OnIntChanged(int previous, int current, bool asServer) => RaiseChanged(asServer);
        private void OnByteChanged(byte previous, byte current, bool asServer) => RaiseChanged(asServer);
        private void OnStringChanged(string previous, string current, bool asServer) => RaiseChanged(asServer);

        private void RaiseChanged(bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }
    }
}
