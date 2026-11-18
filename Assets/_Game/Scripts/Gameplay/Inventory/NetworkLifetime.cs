using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// Server only: despawns this networked object after a fixed time (corpses, dropped items), or when a new raid
    /// starts on the map (<see cref="ServerDespawnAll"/>), so nothing from the last raid is left to loot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkLifetime : NetworkBehaviour
    {
        [Tooltip("Seconds before the object is despawned. 0 keeps it forever.")]
        [SerializeField, Min(0f)] private float _lifetime = 300f;

        private static readonly List<NetworkLifetime> ServerActive = new List<NetworkLifetime>();

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerActive.Add(this);
            if (_lifetime > 0f) StartCoroutine(DespawnAfterLifetime());
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerActive.Remove(this);
        }

        /// <summary>Server: despawns every temporary object now (a new raid replaces the map's contents).</summary>
        public static int ServerDespawnAll()
        {
            var expired = new List<NetworkLifetime>(ServerActive);
            int count = 0;
            foreach (NetworkLifetime item in expired)
            {
                if (item == null || !item.IsSpawned) continue;
                item.Despawn();
                count++;
            }
            return count;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ServerActive.Clear();

        private IEnumerator DespawnAfterLifetime()
        {
            yield return new WaitForSeconds(_lifetime);
            if (IsSpawned) Despawn();
        }
    }
}
