using System.Collections.Generic;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Gameplay.Combat;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Networking
{
    /// <summary>
    /// Server safety net: anything with <see cref="Health"/> that falls below <see cref="_killHeight"/>
    /// (through a gap in the walls or the ground) dies, so it drops its loot and respawns instead of
    /// falling forever. Boundary walls are the first line; this catches everything else.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutOfBoundsKiller : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        [Tooltip("World height below which a character is killed.")]
        [SerializeField] private float _killHeight = -20f;

        [Tooltip("Seconds between checks.")]
        [SerializeField, Min(0.05f)] private float _checkInterval = 0.5f;

        private readonly List<Health> _fallen = new List<Health>();
        private float _nextCheck;

        private void Awake()
        {
            if (_networkManager == null)
                Debug.LogError($"[{nameof(OutOfBoundsKiller)}] Network manager is not assigned on {name}.", this);
        }

        private void Update()
        {
            if (_networkManager == null || !_networkManager.IsServerStarted || Time.time < _nextCheck) return;
            _nextCheck = Time.time + _checkInterval;

            // Collect first: a death spawns a corpse, which changes the spawned collection.
            _fallen.Clear();
            foreach (NetworkObject networkObject in _networkManager.ServerManager.Objects.Spawned.Values)
            {
                if (networkObject == null || networkObject.transform.position.y >= _killHeight) continue;

                Health health = networkObject.GetComponent<Health>();
                if (health != null && health.IsAlive) _fallen.Add(health);
            }

            foreach (Health health in _fallen)
            {
                Vector3 position = health.transform.position;
                health.ServerKill(new DamageInfo(health.Max, DamageInfo.NoInstigator, position, Vector3.down));
            }
            _fallen.Clear();
        }
    }
}
